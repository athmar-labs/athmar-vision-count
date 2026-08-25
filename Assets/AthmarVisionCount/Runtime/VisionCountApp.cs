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
        private CustomerAdminView _adminView;
        private CustomerPackageInstaller _installer;
        private AppConfig _bootstrapConfig;
        private IVisionCountConfiguration _activeConfig;
        private CustomerPackageStore _packageStore;
        private AdminPinStore _pinStore;
        private SkuCatalogue _catalogue;
        private CountingEngine _counting;
        private LocalScanRepository _repository;
        private ExportFileService _exports;
        private ScanSessionRecord _reviewSession;
        private IReadOnlyDictionary<string, int> _latestCounts = new Dictionary<string, int>();
        private bool _reviewing;
        private bool _initialized;
        private readonly AdminSession _adminSession = new AdminSession();
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
            root.AddComponent<CustomerPackageInstaller>();
            root.AddComponent<CustomerAdminView>();
            root.AddComponent<VisionCountApp>();
        }

        private void Awake()
        {
            _view = GetComponent<VisionCountView>();
            _inference = GetComponent<VisionInferenceRunner>();
            _adminView = GetComponent<CustomerAdminView>();
            _installer = GetComponent<CustomerPackageInstaller>();
            if (_view == null || _inference == null || _adminView == null || _installer == null)
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
            _repository = new LocalScanRepository();
            _exports = new ExportFileService();
            _packageStore = new CustomerPackageStore();
            _pinStore = new AdminPinStore();
            _bootstrapConfig = Resources.Load<AppConfig>(ConfigResourceName);
            _view.SetLanguage(_bootstrapConfig == null ? "ar" : _bootstrapConfig.DefaultLanguage);

            if (_packageStore.TryLoadActive(out var snapshot, out var packageError))
            {
                try
                {
                    ActivateConfiguration(snapshot.Configuration, snapshot.Catalogue);
                    return;
                }
                catch (Exception exception)
                {
                    packageError = exception.Message;
                }
            }

            if (_bootstrapConfig != null && _bootstrapConfig.ModelAsset != null && _bootstrapConfig.SkuCatalogueCsv != null)
            {
                try
                {
                    ActivateConfiguration(_bootstrapConfig, SkuCatalogue.Parse(_bootstrapConfig.SkuCatalogueCsv.text));
                    return;
                }
                catch (Exception exception)
                {
                    packageError = exception.Message;
                }
            }

            EnterConfigurationRequiredState(packageError);
        }

        private void ActivateConfiguration(IVisionCountConfiguration config, SkuCatalogue catalogue)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (catalogue == null)
                throw new ArgumentNullException(nameof(catalogue));
            if (config.StoreCapturedImages)
                throw new InvalidOperationException("Captured image storage is disabled in this privacy-first release.");
            if (!config.RequireHumanConfirmation)
                throw new InvalidOperationException("Human confirmation must remain enabled.");

            _inference.StopPipeline();
            _reviewing = false;
            _reviewSession = null;
            _latestCounts = new Dictionary<string, int>();
            _activeConfig = config;
            _catalogue = catalogue;
            _counting = new CountingEngine(
                config.MinimumConfidence,
                config.DuplicateIouThreshold,
                config.TrackTtlSeconds);

            _view.HideReview();
            _view.SetLanguage(config.DefaultLanguage);
            _view.ShowCounts(_latestCounts, _catalogue);
            _view.ShowDetections(Array.Empty<Detection>(), _catalogue);
            _inference.Initialize(config, _catalogue);
            _initialized = true;
            _adminSession.End();
            _adminView.ShowLocked(_pinStore.HasPin, true);
        }

        private void EnterConfigurationRequiredState(string error)
        {
            _initialized = false;
            _activeConfig = null;
            _catalogue = null;
            _counting = null;
            _inference.StopPipeline();
            _view.SetStatus("configuration_required");
            if (!string.IsNullOrWhiteSpace(error))
                _view.SetStatusMessage(LocalizeError(error), true);
            _adminView.ShowLocked(_pinStore.HasPin, true);
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
            _adminView.AdministrationRequested += OpenAdministration;
            _adminView.PinSetupRequested += SetupAdminPin;
            _adminView.UnlockRequested += UnlockAdministration;
            _adminView.InstallRequested += InstallCustomerPackage;
            _adminView.RollbackRequested += RollbackCustomerPackage;
            _adminView.CloseRequested += CloseAdministration;
            _installer.ProgressChanged += HandleInstallationProgress;
            _installer.InstallationCompleted += HandleInstallationCompleted;
            _installer.InstallationFailed += HandleInstallationFailed;
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

            if (_adminView != null)
            {
                _adminView.AdministrationRequested -= OpenAdministration;
                _adminView.PinSetupRequested -= SetupAdminPin;
                _adminView.UnlockRequested -= UnlockAdministration;
                _adminView.InstallRequested -= InstallCustomerPackage;
                _adminView.RollbackRequested -= RollbackCustomerPackage;
                _adminView.CloseRequested -= CloseAdministration;
            }

            if (_installer != null)
            {
                _installer.ProgressChanged -= HandleInstallationProgress;
                _installer.InstallationCompleted -= HandleInstallationCompleted;
                _installer.InstallationFailed -= HandleInstallationFailed;
            }
        }

        private void OpenAdministration()
        {
            if (_initialized)
                _inference.Pause();
            _adminSession.End();
            _adminView.ShowLocked(_pinStore.HasPin, !_initialized);
        }

        private void SetupAdminPin(string pin)
        {
            try
            {
                _pinStore.SetInitialPin(pin);
                _adminSession.Start(Time.realtimeSinceStartupAsDouble);
                _adminView.ShowUnlocked(_activeConfig?.CustomerCode, _packageStore.HasPreviousPackage);
                _adminView.SetStatus("تم إنشاء رمز المدير. احتفظ به في مكان آمن.");
            }
            catch (Exception exception)
            {
                _adminView.SetStatus(exception.Message, true);
            }
        }

        private void UnlockAdministration(string pin)
        {
            if (!_pinStore.Verify(pin))
            {
                _adminView.SetStatus("رمز المدير غير صحيح / Incorrect administrator PIN", true);
                return;
            }

            _adminSession.Start(Time.realtimeSinceStartupAsDouble);
            _adminView.ShowUnlocked(_activeConfig?.CustomerCode, _packageStore.HasPreviousPackage);
        }

        private void InstallCustomerPackage(string manifestUrl, string manifestSha256)
        {
            if (!_adminSession.IsActive)
            {
                _adminView.SetStatus("يجب فتح لوحة الإدارة أولًا / Unlock administration first", true);
                return;
            }

            try
            {
                _adminView.SetBusy(true);
                _installer.Install(manifestUrl, manifestSha256, _packageStore);
            }
            catch (Exception exception)
            {
                _adminView.SetBusy(false);
                _adminView.SetStatus(exception.Message, true);
            }
        }

        private void RollbackCustomerPackage()
        {
            if (!_adminSession.IsActive)
                return;

            try
            {
                _adminView.SetBusy(true);
                var snapshot = _packageStore.Rollback();
                ActivateConfiguration(snapshot.Configuration, snapshot.Catalogue);
                _adminSession.Start(Time.realtimeSinceStartupAsDouble);
                _adminView.ShowUnlocked(snapshot.Configuration.CustomerCode, _packageStore.HasPreviousPackage);
                _adminView.SetStatus("تم الرجوع إلى حزمة العميل السابقة / Previous package restored");
            }
            catch (Exception exception)
            {
                _adminView.SetBusy(false);
                _adminView.SetStatus(exception.Message, true);
            }
        }

        private void CloseAdministration()
        {
            if (!_initialized)
                return;
            _adminSession.End();
            _adminView.Hide();
            _inference.Resume();
        }

        private void HandleInstallationProgress(string message)
        {
            _adminView.SetStatus(message);
        }

        private void HandleInstallationCompleted(CustomerPackageSnapshot snapshot)
        {
            try
            {
                ActivateConfiguration(snapshot.Configuration, snapshot.Catalogue);
                _adminSession.Start(Time.realtimeSinceStartupAsDouble);
                _adminView.ShowUnlocked(snapshot.Configuration.CustomerCode, _packageStore.HasPreviousPackage);
                _adminView.SetStatus("تم تفعيل حزمة العميل بنجاح / Customer package activated");
            }
            catch (Exception exception)
            {
                _adminView.SetBusy(false);
                _adminView.SetStatus(exception.Message, true);
            }
        }

        private void HandleInstallationFailed(string message)
        {
            _adminView.SetBusy(false);
            _adminView.SetStatus(message, true);
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
            OpenAdministration();
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
