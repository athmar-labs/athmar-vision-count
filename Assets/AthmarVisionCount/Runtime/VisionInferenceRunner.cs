using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public sealed class VisionInferenceRunner : MonoBehaviour
    {
        public event Action<IReadOnlyList<Detection>> DetectionsReady;
        public event Action<string> StatusChanged;
        public event Action<string> Faulted;

        private IVisionCountConfiguration _config;
        private SkuCatalogue _catalogue;
        private Model _model;
        private Worker _worker;
        private Tensor<float> _inputTensor;
        private WebCamTexture _cameraTexture;
        private Awaitable _inferenceLoop;
        private BackendType _backend;
        private float[] _cpuInputBuffer;
        private Color32[] _cpuCameraPixels;
        private bool _loopStarted;
        private bool _running;
        private bool _paused;
        private double _nextInferenceAt;
        private string _activeInferenceStage = "idle";

        public Texture CameraTexture => _cameraTexture;
        public bool IsReady => _running && _cameraTexture != null && _cameraTexture.isPlaying && _worker != null;
        public bool IsPaused => _paused;
        public int VideoRotationAngle => _cameraTexture == null ? 0 : _cameraTexture.videoRotationAngle;
        public bool VideoVerticallyMirrored => _cameraTexture != null && _cameraTexture.videoVerticallyMirrored;

        public void Initialize(
            IVisionCountConfiguration config,
            SkuCatalogue catalogue,
            bool startPaused = false)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (catalogue == null)
                throw new ArgumentNullException(nameof(catalogue));
            if (config.ModelAsset == null && string.IsNullOrWhiteSpace(config.ModelFilePath))
                throw new InvalidOperationException("A bundled or runtime customer model is required.");
            if (_running || _worker != null)
                throw new InvalidOperationException("The inference runner is already initialized.");

            _config = config;
            _catalogue = catalogue;
            _paused = startPaused;
            _activeInferenceStage = "initializing";
            StartCoroutine(StartPipeline());
        }

        public void Pause()
        {
            _paused = true;
            if (_config != null)
                StatusChanged?.Invoke("paused");
        }

        public void Resume()
        {
            _paused = false;
            _nextInferenceAt = 0d;
            if (IsReady)
                StatusChanged?.Invoke("scanning");
        }

        public void StopPipeline()
        {
            StopAllCoroutines();
            _running = false;
            _paused = true;
            if (_loopStarted)
            {
                _inferenceLoop.Cancel();
                _loopStarted = false;
            }

            if (_cameraTexture != null)
            {
                if (_cameraTexture.isPlaying)
                    _cameraTexture.Stop();
                Destroy(_cameraTexture);
                _cameraTexture = null;
            }

            _inputTensor?.Dispose();
            _inputTensor = null;
            _worker?.Dispose();
            _worker = null;
            _model = null;
            _cpuInputBuffer = null;
            _cpuCameraPixels = null;
            _config = null;
            _catalogue = null;
            _activeInferenceStage = "stopped";
        }

        private IEnumerator StartPipeline()
        {
            StatusChanged?.Invoke("requesting_camera_permission");
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                ReportFault("Camera permission was denied. Enable camera access in Android settings.");
                yield break;
            }

            try
            {
                _activeInferenceStage = "create_resources";
                CreateInferenceResources();
                _activeInferenceStage = "start_camera";
                StartRearCamera();
            }
            catch (Exception exception)
            {
                ReportDetailedFault("Unable to initialize on-device inference", _activeInferenceStage, exception);
                StopPipeline();
                yield break;
            }

            var timeoutAt = Time.realtimeSinceStartupAsDouble + 10d;
            while (_cameraTexture != null && _cameraTexture.isPlaying && _cameraTexture.width <= 16 && Time.realtimeSinceStartupAsDouble < timeoutAt)
                yield return null;

            if (_cameraTexture == null || !_cameraTexture.isPlaying || _cameraTexture.width <= 16)
            {
                ReportFault("The device camera did not provide frames within the allowed time.");
                StopPipeline();
                yield break;
            }

            _running = true;
            _activeInferenceStage = "ready";
            StatusChanged?.Invoke(_paused ? "paused" : "scanning");
            _inferenceLoop = RunInferenceLoop();
            _loopStarted = true;
        }

        private void CreateInferenceResources()
        {
            EnsureConfigured();

            _model = !string.IsNullOrWhiteSpace(_config.ModelFilePath)
                ? ModelLoader.Load(_config.ModelFilePath)
                : ModelLoader.Load(_config.ModelAsset);
            if (_model == null)
                throw new InvalidOperationException("ModelLoader returned no model.");

            _backend = _config.PreferGpu && SystemInfo.supportsComputeShaders
                ? BackendType.GPUCompute
                : BackendType.CPU;
            _worker = new Worker(_model, _backend);
            if (_worker == null)
                throw new InvalidOperationException("Unable to create the inference worker.");

            var shape = _config.InputLayout == ModelInputLayout.Nhwc
                ? new TensorShape(1, _config.ModelInputHeight, _config.ModelInputWidth, 3)
                : new TensorShape(1, 3, _config.ModelInputHeight, _config.ModelInputWidth);
            _inputTensor = new Tensor<float>(shape);
            if (_inputTensor == null)
                throw new InvalidOperationException("Unable to allocate the input tensor.");

            if (_backend == BackendType.CPU)
                _cpuInputBuffer = new float[checked(_config.ModelInputWidth * _config.ModelInputHeight * 3)];
        }

        private void StartRearCamera()
        {
            EnsureConfigured();

            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
                throw new InvalidOperationException("No camera is available on this device.");

            var selected = devices[0];
            for (var index = 0; index < devices.Length; index++)
            {
                if (devices[index].isFrontFacing)
                    continue;
                selected = devices[index];
                break;
            }

            _cameraTexture = new WebCamTexture(
                selected.name,
                Math.Max(1280, _config.ModelInputWidth),
                Math.Max(720, _config.ModelInputHeight),
                30);
            _cameraTexture.Play();
        }

        private async Awaitable RunInferenceLoop()
        {
            try
            {
                while (_running)
                {
                    if (!_paused && _cameraTexture != null && _cameraTexture.didUpdateThisFrame && Time.realtimeSinceStartupAsDouble >= _nextInferenceAt)
                    {
                        EnsureRuntimeState();
                        _nextInferenceAt = Time.realtimeSinceStartupAsDouble + _config.InferenceIntervalSeconds;
                        await RunSingleInference();
                    }

                    await Awaitable.NextFrameAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception exception)
            {
                _running = false;
                ReportDetailedFault("On-device inference stopped", _activeInferenceStage, exception);
            }
            finally
            {
                _loopStarted = false;
            }
        }

        private async Awaitable RunSingleInference()
        {
            EnsureRuntimeState();

            if (_backend == BackendType.CPU)
            {
                _activeInferenceStage = "camera_to_cpu_tensor";
                FillCpuInputTensor();
            }
            else
            {
                _activeInferenceStage = "prepare_input_transform";
                var transform = new TextureTransform();
                if (_config.InputLayout == ModelInputLayout.Nhwc)
                    transform.SetTensorLayout(TensorLayout.NHWC);

                _activeInferenceStage = "texture_to_tensor";
                TextureConverter.ToTensor(_cameraTexture, _inputTensor, transform);
            }

            _activeInferenceStage = "schedule_worker";
            _worker.Schedule(_inputTensor);

            _activeInferenceStage = "peek_output";
            var rawOutput = _worker.PeekOutput(_config.OutputTensorIndex);
            if (rawOutput == null)
                throw new InvalidOperationException($"Model output at index {_config.OutputTensorIndex} was null.");
            if (!(rawOutput is Tensor<float> outputOnDevice))
                throw new InvalidOperationException("The configured model output is not a float tensor.");

            _activeInferenceStage = "readback_output";
            using var output = await outputOnDevice.ReadbackAndCloneAsync();
            if (output == null)
                throw new InvalidOperationException("Output readback returned no tensor.");

            _activeInferenceStage = "validate_output_shape";
            if (output.shape.rank != 2 && output.shape.rank != 3)
                throw new InvalidOperationException($"Unsupported model output rank {output.shape.rank}; expected rank 2 or 3.");
            if (output.shape.rank == 3 && output.shape[0] != 1)
                throw new InvalidOperationException("Only batch size 1 is supported for mobile scanning.");

            var dimensionA = output.shape[-2];
            var dimensionB = output.shape[-1];

            _activeInferenceStage = "download_output";
            var outputValues = output.DownloadToArray();
            if (outputValues == null)
                throw new InvalidOperationException("Model output download returned no data.");

            _activeInferenceStage = "decode_yolo_output";
            var detections = YoloOutputDecoder.Decode(
                outputValues,
                dimensionA,
                dimensionB,
                _config.OutputTensorLayout,
                _config.OutputHasObjectness,
                _config.OutputCoordinatesNormalized,
                _config.ModelInputWidth,
                _config.ModelInputHeight,
                _config.MinimumConfidence,
                _config.NonMaxSuppressionIouThreshold,
                _config.MaxDetections,
                _catalogue);
            if (detections == null)
                throw new InvalidOperationException("The YOLO decoder returned no detection collection.");

            _activeInferenceStage = "publish_detections";
            DetectionsReady?.Invoke(detections);
            _activeInferenceStage = "idle";
        }

        private void FillCpuInputTensor()
        {
            if (_cpuInputBuffer == null)
                throw new InvalidOperationException("CPU inference input buffer is unavailable.");

            var sourceWidth = _cameraTexture.width;
            var sourceHeight = _cameraTexture.height;
            if (sourceWidth <= 16 || sourceHeight <= 16)
                throw new InvalidOperationException("The camera frame is too small for CPU inference.");

            var requiredPixels = checked(sourceWidth * sourceHeight);
            if (_cpuCameraPixels == null || _cpuCameraPixels.Length != requiredPixels)
                _cpuCameraPixels = new Color32[requiredPixels];

            _cpuCameraPixels = _cameraTexture.GetPixels32(_cpuCameraPixels);
            if (_cpuCameraPixels == null || _cpuCameraPixels.Length != requiredPixels)
                throw new InvalidOperationException("Unable to read the current camera frame for CPU inference.");

            CpuRgbTensorWriter.ResizeRgb01(
                _cpuCameraPixels,
                sourceWidth,
                sourceHeight,
                _config.ModelInputWidth,
                _config.ModelInputHeight,
                _config.InputLayout,
                _cpuInputBuffer);
            _inputTensor.Upload(_cpuInputBuffer);
        }

        private void EnsureConfigured()
        {
            if (_config == null)
                throw new InvalidOperationException("Inference configuration is unavailable.");
            if (_catalogue == null)
                throw new InvalidOperationException("SKU catalogue is unavailable.");
            if (_config.ModelInputWidth <= 0 || _config.ModelInputHeight <= 0)
                throw new InvalidOperationException("Model input dimensions must be greater than zero.");
        }

        private void EnsureRuntimeState()
        {
            _activeInferenceStage = "preflight";
            EnsureConfigured();

            if (_cameraTexture == null)
                throw new InvalidOperationException("Camera texture is unavailable.");
            if (!_cameraTexture.isPlaying)
                throw new InvalidOperationException("Camera texture is not playing.");
            if (_worker == null)
                throw new InvalidOperationException("Inference worker is unavailable.");
            if (_inputTensor == null)
                throw new InvalidOperationException("Input tensor is unavailable.");
        }

        private void ReportDetailedFault(string prefix, string stage, Exception exception)
        {
            var safeStage = string.IsNullOrWhiteSpace(stage) ? "unknown" : stage;
            var safeException = exception ?? new InvalidOperationException("Unknown inference failure.");
            var userMessage = $"{prefix} [{safeStage}]: {safeException.GetType().Name}: {safeException.Message}";

            _paused = true;
            Faulted?.Invoke(userMessage);
            Debug.LogError(userMessage + "\n" + safeException);
        }

        private void ReportFault(string message)
        {
            _paused = true;
            Faulted?.Invoke(message);
            Debug.LogError(message);
        }

        private void OnDisable()
        {
            StopPipeline();
        }
    }
}
