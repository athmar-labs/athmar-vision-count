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

            var backend = backendOverride ??
                (SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
            _worker = new Worker(_model, backend);
            _inputTensor = new Tensor<float>(new TensorShape(1, InputHeight, InputWidth, 3));
        }

        public float[] Extract(Texture2D texture)
        {
            ThrowIfDisposed();
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            if (texture.width < 16 || texture.height < 16)
                throw new InvalidOperationException("The captured frame is too small for neural product embedding.");

            var transform = new TextureTransform();
            transform.SetTensorLayout(TensorLayout.NHWC);
            TextureConverter.ToTensor(texture, _inputTensor, transform);

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

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SentisProductEmbeddingExtractor));
        }
    }
}
