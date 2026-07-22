using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class VisionCountApp : MonoBehaviour
    {
        private const string ConfigResourceName = "AthmarVisionCountConfig";
        private const double DeleteConfirmationWindowSeconds = 5d;

        private VisionCountView _view;
        private VisionInferenceRunner _inference;
        private AppConfig _config;
        private SkuCatalogue _catalogue;
        private CountingEngine _counting;
        private LocalScanRepository _repository;
        private ExportFileService _exports;
        private ScanSessionRecord _reviewSession;
        private IReadOnlyDictionary<string, int> _latestCounts = new Dictionary<string, int>();
        private bool _reviewing;
        private bool _initialized;
        private double _deleteConfirmationExpiresAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureApplicationExists()
        {
            if (FindFirstObjectByType<VisionCountApp>() != null)
                return;

            var root = new GameObject("Athmar Vision Count");
            DontDestroyOnLoad(root);
            root.AddComponent<VisionCountView>();
            root.AddComponent<VisionInferenceRunner>();
            root.AddComponent<VisionCountApp>();
        }

        private void Awake()
        {
            _view = GetComponent<VisionCountView>();
            _inference = GetComponent<VisionInferenceRunner>();
            if (_view == null || _inference == null)
                throw new InvalidOperationException("Vision Count runtime components are missing.");

            Subscribe();
        }

        private void Start()
        {
            InitializeApplication();
        }

        private void Update()
        {
            if (_inference == null || _view == null)
                return;

            _view.SetCamera(_inference.CameraTexture, _inference.VideoRotationAngle, _inference.VideoVerticallyMirrored);
        }

        private void InitializeApplication()
        {
            try
            {
                _config = Resources.Load<AppConfig>(ConfigResourceName);
                if (_config == null)
                    throw new InvalidOperationException("Production configuration asset was not found in Resources.");
                if (_config.StoreCapturedImages)
                    throw new InvalidOperationException("This privacy-first release does not permit captured image storage.");
                if (!_config.RequireHumanConfirmation)
                    throw new InvalidOperationException("Human confirmation must remain enabled.");
                if (_config.SkuCatalogueCsv == null)
                    throw new InvalidOperationException("A production SKU catalogue is not assigned.");

                _catalogue = SkuCatalogue.Parse(_config.SkuCatalogueCsv.text);
                _counting = new CountingEngine(
                    _config.MinimumConfidence,
                    _config.DuplicateIouThreshold,
                    _config.TrackTtlSeconds);
                _repository = new LocalScanRepository();
                _exports = new ExportFileService();

                _view.SetLanguage(_config.DefaultLanguage);
                _view.ShowCounts(_latestCounts, _catalogue);
                _inference.Initialize(_config, _catalogue);
                _initialized = true;
            }
            catch (Exception exception)
            {
                _initialized = false;
                _view.SetStatusMessage(LocalizeError(exception.Message), true);
                Debug.LogError(exception);
            }
        }

        private void Subscribe()
        {
            _inference.DetectionsReady += HandleDetections;
            _inference.StatusChanged += HandleInferenceStatus;
            _inference.Faulted += HandleInferenceFault;
            _view.PauseResumeRequested += TogglePause;
            _view.ReviewRequested += OpenReview;
            _view.ConfirmRequested += ConfirmAndExport;
            _view.CancelReviewRequested += CancelReview;
            _view.DeleteDataRequested += DeleteAllData;
            _view.LanguageToggleRequested += ToggleLanguage;
            _view.ManualCountRequested += SetManualCount;
        }

        private void Unsubscribe()
        {
            if (_inference != null)
            {
                _inference.DetectionsReady -= HandleDetections;
                _inference.StatusChanged -= HandleInferenceStatus;
                _inference.Faulted -= HandleInferenceFault;
            }

            if (_view != null)
            {
                _view.PauseResumeRequested -= TogglePause;
                _view.ReviewRequested -= OpenReview;
                _view.ConfirmRequested -= ConfirmAndExport;
                _view.CancelReviewRequested -= CancelReview;
                _view.DeleteDataRequested -= DeleteAllData;
                _view.LanguageToggleRequested -= ToggleLanguage;
                _view.ManualCountRequested -= SetManualCount;
            }
        }

        private void HandleDetections(IReadOnlyList<Detection> detections)
        {
            if (!_initialized || _reviewing || _counting == null || _catalogue == null)
                return;

            _latestCounts = _counting.ProcessFrame(detections, Time.realtimeSinceStartupAsDouble);
            _view.ShowDetections(detections, _catalogue);
            _view.ShowCounts(_latestCounts, _catalogue);
        }

        private void HandleInferenceStatus(string statusKey)
        {
            _view.SetStatus(statusKey);
            _view.SetPaused(string.Equals(statusKey, "paused", StringComparison.Ordinal));
        }

        private void HandleInferenceFault(string message)
        {
            _view.SetStatusMessage(LocalizeError(message), true);
            _view.SetPaused(true);
        }

        private void TogglePause()
        {
            if (!_initialized || _reviewing)
                return;

            if (_inference.IsPaused)
                _inference.Resume();
            else
                _inference.Pause();
            _view.SetPaused(_inference.IsPaused);
        }

        private void OpenReview()
        {
            if (!_initialized || _counting == null || _catalogue == null)
                return;
            if (_latestCounts == null || _latestCounts.Count == 0)
            {
                _view.SetStatus("empty_counts");
                return;
            }

            _reviewing = true;
            _inference.Pause();
            RefreshReview();
        }

        private void RefreshReview()
        {
            _reviewSession = _counting.BuildSession(
                _view.OperatorReference,
                _view.LocationReference,
                sku => _catalogue.ResolveDisplayName(sku, _view.Language));
            _view.ShowReview(_reviewSession, _catalogue);
        }

        private void SetManualCount(string sku, int count)
        {
            if (!_reviewing || _counting == null)
                return;

            try
            {
                _counting.SetManualCount(sku, count);
                _latestCounts = _counting.GetEffectiveCounts();
                _view.ShowCounts(_latestCounts, _catalogue);
                RefreshReview();
            }
            catch (Exception exception)
            {
                _view.SetStatusMessage(LocalizeError(exception.Message), true);
            }
        }

        private void ConfirmAndExport()
        {
            if (!_reviewing || _counting == null || _repository == null || _exports == null)
                return;

            if (string.IsNullOrWhiteSpace(_view.OperatorReference) || string.IsNullOrWhiteSpace(_view.LocationReference))
            {
                _view.SetStatusMessage(
                    VisionCountLocalization.IsArabic(_view.Language)
                        ? "أدخل مرجع الموظف والموقع قبل الاعتماد."
                        : "Enter the operator and location references before confirmation.",
                    true);
                return;
            }

            try
            {
                _reviewSession = _counting.BuildSession(
                    _view.OperatorReference,
                    _view.LocationReference,
                    sku => _catalogue.ResolveDisplayName(sku, _view.Language));
                _counting.Confirm(_reviewSession);
                _repository.Save(_reviewSession);
                var exportPath = _exports.SaveConfirmedSession(_reviewSession);

                _view.HideReview();
                _reviewing = false;
                _counting.Reset();
                _latestCounts = _counting.GetEffectiveCounts();
                _view.ShowCounts(_latestCounts, _catalogue);
                _view.ShowDetections(Array.Empty<Detection>(), _catalogue);
                _view.SetStatusMessage(
                    VisionCountLocalization.Text("exported", _view.Language) + ": " + Path.GetFileName(exportPath));
                _inference.Resume();
            }
            catch (Exception exception)
            {
                _view.SetStatusMessage(LocalizeError(exception.Message), true);
                Debug.LogError(exception);
            }
        }

        private void CancelReview()
        {
            if (!_reviewing)
                return;

            _reviewing = false;
            _reviewSession = null;
            _view.HideReview();
            _inference.Resume();
        }

        private void DeleteAllData()
        {
            if (!_initialized || _repository == null || _exports == null)
                return;

            var now = Time.realtimeSinceStartupAsDouble;
            if (now > _deleteConfirmationExpiresAt)
            {
                _deleteConfirmationExpiresAt = now + DeleteConfirmationWindowSeconds;
                _view.SetStatusMessage(
                    VisionCountLocalization.IsArabic(_view.Language)
                        ? "اضغط حذف البيانات مرة أخرى خلال خمس ثوانٍ للتأكيد."
                        : "Tap Delete Local Data again within five seconds to confirm.");
                return;
            }

            try
            {
                _repository.DeleteAllLocalData();
                _exports.DeleteAll();
                _counting.Reset();
                _latestCounts = _counting.GetEffectiveCounts();
                _reviewing = false;
                _reviewSession = null;
                _view.HideReview();
                _view.ShowCounts(_latestCounts, _catalogue);
                _view.ShowDetections(Array.Empty<Detection>(), _catalogue);
                _view.SetStatus("deleted");
                _deleteConfirmationExpiresAt = 0d;
            }
            catch (Exception exception)
            {
                _view.SetStatusMessage(LocalizeError(exception.Message), true);
            }
        }

        private void ToggleLanguage()
        {
            var language = VisionCountLocalization.IsArabic(_view.Language) ? "en" : "ar";
            _view.SetLanguage(language);
            if (_catalogue != null)
            {
                _view.ShowCounts(_latestCounts, _catalogue);
                if (_reviewing)
                    RefreshReview();
            }
        }

        private string LocalizeError(string message)
        {
            if (!VisionCountLocalization.IsArabic(_view.Language))
                return message;
            return "تعذر إكمال العملية بأمان: " + message;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_initialized || _reviewing)
                return;

            if (paused)
                _inference.Pause();
            else
                _inference.Resume();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
