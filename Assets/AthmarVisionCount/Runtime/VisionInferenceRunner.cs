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

        private AppConfig _config;
        private SkuCatalogue _catalogue;
        private Model _model;
        private Worker _worker;
        private Tensor<float> _inputTensor;
        private WebCamTexture _cameraTexture;
        private Awaitable _inferenceLoop;
        private bool _loopStarted;
        private bool _running;
        private bool _paused;
        private double _nextInferenceAt;

        public Texture CameraTexture => _cameraTexture;
        public bool IsReady => _running && _cameraTexture != null && _cameraTexture.isPlaying && _worker != null;
        public bool IsPaused => _paused;
        public int VideoRotationAngle => _cameraTexture == null ? 0 : _cameraTexture.videoRotationAngle;
        public bool VideoVerticallyMirrored => _cameraTexture != null && _cameraTexture.videoVerticallyMirrored;

        public void Initialize(AppConfig config, SkuCatalogue catalogue)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (catalogue == null)
                throw new ArgumentNullException(nameof(catalogue));
            if (config.ModelAsset == null)
                throw new InvalidOperationException("A production model is not assigned.");
            if (_running || _worker != null)
                throw new InvalidOperationException("The inference runner is already initialized.");

            _config = config;
            _catalogue = catalogue;
            StartCoroutine(StartPipeline());
        }

        public void Pause()
        {
            _paused = true;
            StatusChanged?.Invoke("paused");
        }

        public void Resume()
        {
            _paused = false;
            _nextInferenceAt = 0d;
            StatusChanged?.Invoke("scanning");
        }

        public void StopPipeline()
        {
            _running = false;
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
                CreateInferenceResources();
                StartRearCamera();
            }
            catch (Exception exception)
            {
                ReportFault("Unable to initialize on-device inference: " + exception.Message);
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
            _paused = false;
            StatusChanged?.Invoke("scanning");
            _inferenceLoop = RunInferenceLoop();
            _loopStarted = true;
        }

        private void CreateInferenceResources()
        {
            _model = ModelLoader.Load(_config.ModelAsset);
            var backend = _config.PreferGpu && SystemInfo.supportsComputeShaders
                ? BackendType.GPUCompute
                : BackendType.CPU;
            _worker = new Worker(_model, backend);

            var shape = _config.InputLayout == ModelInputLayout.Nhwc
                ? new TensorShape(1, _config.ModelInputHeight, _config.ModelInputWidth, 3)
                : new TensorShape(1, 3, _config.ModelInputHeight, _config.ModelInputWidth);
            _inputTensor = new Tensor<float>(shape);
        }

        private void StartRearCamera()
        {
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
                ReportFault("On-device inference stopped: " + exception.Message);
            }
        }

        private async Awaitable RunSingleInference()
        {
            var transform = new TextureTransform();
            if (_config.InputLayout == ModelInputLayout.Nhwc)
                transform.SetTensorLayout(TensorLayout.NHWC);

            TextureConverter.ToTensor(_cameraTexture, _inputTensor, transform);
            _worker.Schedule(_inputTensor);

            if (!(_worker.PeekOutput(_config.OutputTensorIndex) is Tensor<float> outputOnDevice))
                throw new InvalidOperationException("The configured model output is not a float tensor.");

            using var output = await outputOnDevice.ReadbackAndCloneAsync();
            if (output.shape.rank != 2 && output.shape.rank != 3)
                throw new InvalidOperationException($"Unsupported model output rank {output.shape.rank}; expected rank 2 or 3.");
            if (output.shape.rank == 3 && output.shape[0] != 1)
                throw new InvalidOperationException("Only batch size 1 is supported for mobile scanning.");

            var dimensionA = output.shape[-2];
            var dimensionB = output.shape[-1];
            var detections = YoloOutputDecoder.Decode(
                output.DownloadToArray(),
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

            DetectionsReady?.Invoke(detections);
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
