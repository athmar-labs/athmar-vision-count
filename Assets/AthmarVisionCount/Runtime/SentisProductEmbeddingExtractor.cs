using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// One shared neural embedding model for every customer. Customer identity and SKU data
    /// never select or replace this model. Enrollment fails closed when the model is absent.
    /// </summary>
    public sealed class SentisProductEmbeddingExtractor : IProductEmbeddingExtractor, IDisposable
    {
        public const string ResourceName = "AthmarGenericEmbedding";
        public const string ModelId = "timm/mobilenetv3_small_075.lamb_in1k@fa65a043c25690a5779ff856052a5ae55ec03eda";
        public const int InputWidth = 224;
        public const int InputHeight = 224;
        public const int FeatureDimension = 1024;
        public const int OutputTensorIndex = 0;

        private readonly Model _model;
        private readonly Worker _worker;
        private readonly Tensor<float> _inputTensor;
        private readonly BackendType _backend;
        private readonly float[] _cpuInputBuffer;
        private bool _disposed;

        public int Dimension => FeatureDimension;

        public SentisProductEmbeddingExtractor(ModelAsset modelAsset = null, BackendType? backendOverride = null)
        {
            modelAsset ??= Resources.Load<ModelAsset>(ResourceName);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"The generic AI embedding model '{ResourceName}' is unavailable. " +
                    "Product enrollment is disabled rather than falling back to a non-neural descriptor.");
            }

            _model = ModelLoader.Load(modelAsset);
            if (_model == null)
                throw new InvalidOperationException("Unable to load the generic AI embedding model.");

            _backend = backendOverride ??
                (SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
            _worker = new Worker(_model, _backend);
            _inputTensor = new Tensor<float>(new TensorShape(1, InputHeight, InputWidth, 3));

            // Unity Inference Engine 2.4.1's TextureConverter can leave delayed GPU disposals
            // even when the destination tensor is later scheduled on a CPU worker. Worker.Schedule
            // then dereferences a null GPU backend while draining that queue. Keep the CPU fallback
            // genuinely CPU-only so enrollment works on devices without compute shaders and CI can
            // exercise the fallback rather than masking the package bug with a GPU-only test.
            if (_backend == BackendType.CPU)
                _cpuInputBuffer = new float[InputWidth * InputHeight * 3];
        }

        public float[] Extract(Texture2D texture)
        {
            ThrowIfDisposed();
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            if (texture.width < 16 || texture.height < 16)
                throw new InvalidOperationException("The captured frame is too small for neural product embedding.");

            if (_backend == BackendType.CPU)
            {
                FillCpuInputTensor(texture);
            }
            else
            {
                var transform = new TextureTransform();
                transform.SetTensorLayout(TensorLayout.NHWC);
                TextureConverter.ToTensor(texture, _inputTensor, transform);
            }

            _worker.Schedule(_inputTensor);
            var rawOutput = _worker.PeekOutput(OutputTensorIndex);
            if (!(rawOutput is Tensor<float> outputOnDevice))
                throw new InvalidOperationException("The generic embedding model output is not a float tensor.");

            using var output = outputOnDevice.ReadbackAndClone();
            if (output == null)
                throw new InvalidOperationException("The generic embedding model output could not be read back.");

            var values = output.DownloadToArray();
            if (values == null || values.Length != FeatureDimension)
            {
                throw new InvalidOperationException(
                    $"The generic embedding model must output exactly {FeatureDimension} values; " +
                    $"received {values?.Length ?? 0}.");
            }

            ProductEmbeddingMath.ValidateFinite(values);
            ProductEmbeddingMath.NormalizeInPlace(values);
            return values;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _inputTensor?.Dispose();
            _worker?.Dispose();
        }

        private void FillCpuInputTensor(Texture2D texture)
        {
            if (!texture.isReadable)
            {
                throw new InvalidOperationException(
                    "CPU product embedding requires a readable Texture2D. Capture the enrollment frame into a readable texture before extraction.");
            }

            var source = texture.GetPixels32();
            if (source == null || source.Length != texture.width * texture.height)
                throw new InvalidOperationException("Unable to read the captured frame for CPU product embedding.");

            var scaleX = texture.width > 1 && InputWidth > 1
                ? (texture.width - 1f) / (InputWidth - 1f)
                : 0f;
            var scaleY = texture.height > 1 && InputHeight > 1
                ? (texture.height - 1f) / (InputHeight - 1f)
                : 0f;

            var destination = 0;
            for (var y = 0; y < InputHeight; y++)
            {
                var sourceY = y * scaleY;
                var y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, texture.height - 1);
                var y1 = Mathf.Min(y0 + 1, texture.height - 1);
                var fy = sourceY - y0;

                for (var x = 0; x < InputWidth; x++)
                {
                    var sourceX = x * scaleX;
                    var x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, texture.width - 1);
                    var x1 = Mathf.Min(x0 + 1, texture.width - 1);
                    var fx = sourceX - x0;

                    var c00 = source[(y0 * texture.width) + x0];
                    var c10 = source[(y0 * texture.width) + x1];
                    var c01 = source[(y1 * texture.width) + x0];
                    var c11 = source[(y1 * texture.width) + x1];

                    _cpuInputBuffer[destination++] = BilinearChannel(c00.r, c10.r, c01.r, c11.r, fx, fy);
                    _cpuInputBuffer[destination++] = BilinearChannel(c00.g, c10.g, c01.g, c11.g, fx, fy);
                    _cpuInputBuffer[destination++] = BilinearChannel(c00.b, c10.b, c01.b, c11.b, fx, fy);
                }
            }

            _inputTensor.Upload(_cpuInputBuffer);
        }

        private static float BilinearChannel(byte c00, byte c10, byte c01, byte c11, float fx, float fy)
        {
            var lower = Mathf.Lerp(c00, c10, fx);
            var upper = Mathf.Lerp(c01, c11, fx);
            return Mathf.Lerp(lower, upper, fy) / 255f;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SentisProductEmbeddingExtractor));
        }
    }
}
