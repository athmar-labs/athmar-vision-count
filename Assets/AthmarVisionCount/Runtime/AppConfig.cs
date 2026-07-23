using Unity.InferenceEngine;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public enum DetectionTensorLayout
    {
        ChannelsFirst = 0,
        RowsFirst = 1
    }

    public enum ModelInputLayout
    {
        Nchw = 0,
        Nhwc = 1
    }

    [CreateAssetMenu(fileName = "AthmarVisionCountConfig", menuName = "Athmar/Vision Count/App Config")]
    public sealed class AppConfig : ScriptableObject, IVisionCountConfiguration
    {
        [Header("Product identity")]
        [SerializeField] private string applicationDisplayName = "Athmar Vision Count";
        [SerializeField] private string customerCode = "pilot";
        [SerializeField] private string defaultLanguage = "en";

        [Header("Model and catalogue")]
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private TextAsset skuCatalogueCsv;
        [SerializeField] private string modelVersion = "unassigned";
        [SerializeField] private string catalogueVersion = "unassigned";
        [SerializeField, Min(32)] private int modelInputWidth = 640;
        [SerializeField, Min(32)] private int modelInputHeight = 640;
        [SerializeField] private ModelInputLayout modelInputLayout = ModelInputLayout.Nchw;
        [SerializeField, Min(0)] private int outputTensorIndex;
        [SerializeField] private DetectionTensorLayout outputTensorLayout = DetectionTensorLayout.ChannelsFirst;
        [SerializeField] private bool outputHasObjectness;
        [SerializeField] private bool outputCoordinatesNormalized;
        [SerializeField, Min(1)] private int maxDetections = 100;
        [SerializeField] private bool preferGpu = true;
        [SerializeField, Min(0.05f)] private float inferenceIntervalSeconds = 0.25f;
        [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.65f;
        [SerializeField, Range(0f, 1f)] private float duplicateIouThreshold = 0.45f;
        [SerializeField, Range(0f, 1f)] private float nonMaxSuppressionIouThreshold = 0.45f;
        [SerializeField, Min(0.1f)] private float trackTtlSeconds = 1.25f;

        [Header("Safety and review")]
        [SerializeField] private bool requireHumanConfirmation = true;
        [SerializeField] private bool storeCapturedImages;
        [SerializeField, Min(1)] private int retentionDays = 30;
        [SerializeField] private string privacyNoticeVersion = "draft";

        [Header("Optional customer-approved sync")]
        [SerializeField] private bool networkSyncEnabled;
        [SerializeField] private string syncEndpoint = string.Empty;

        public string ApplicationDisplayName => applicationDisplayName;
        public string CustomerCode => customerCode;
        public string DefaultLanguage => defaultLanguage;
        public ModelAsset ModelAsset => modelAsset;
        public string ModelFilePath => string.Empty;
        public TextAsset SkuCatalogueCsv => skuCatalogueCsv;
        public string ModelVersion => modelVersion;
        public string CatalogueVersion => catalogueVersion;
        public int ModelInputWidth => modelInputWidth;
        public int ModelInputHeight => modelInputHeight;
        public ModelInputLayout InputLayout => modelInputLayout;
        public int OutputTensorIndex => outputTensorIndex;
        public DetectionTensorLayout OutputTensorLayout => outputTensorLayout;
        public bool OutputHasObjectness => outputHasObjectness;
        public bool OutputCoordinatesNormalized => outputCoordinatesNormalized;
        public int MaxDetections => maxDetections;
        public bool PreferGpu => preferGpu;
        public float InferenceIntervalSeconds => inferenceIntervalSeconds;
        public float MinimumConfidence => minimumConfidence;
        public float DuplicateIouThreshold => duplicateIouThreshold;
        public float NonMaxSuppressionIouThreshold => nonMaxSuppressionIouThreshold;
        public float TrackTtlSeconds => trackTtlSeconds;
        public bool RequireHumanConfirmation => requireHumanConfirmation;
        public bool StoreCapturedImages => storeCapturedImages;
        public int RetentionDays => retentionDays;
        public string PrivacyNoticeVersion => privacyNoticeVersion;
        public bool NetworkSyncEnabled => networkSyncEnabled;
        public string SyncEndpoint => syncEndpoint;

        private void OnValidate()
        {
            applicationDisplayName = Clean(applicationDisplayName, "Athmar Vision Count");
            customerCode = Clean(customerCode, "pilot");
            defaultLanguage = Clean(defaultLanguage, "en").ToLowerInvariant();
            if (defaultLanguage != "ar" && defaultLanguage != "en")
                defaultLanguage = "en";

            modelVersion = Clean(modelVersion, "unassigned");
            catalogueVersion = Clean(catalogueVersion, "unassigned");
            modelInputWidth = Mathf.Max(32, modelInputWidth);
            modelInputHeight = Mathf.Max(32, modelInputHeight);
            outputTensorIndex = Mathf.Max(0, outputTensorIndex);
            maxDetections = Mathf.Clamp(maxDetections, 1, 500);
            inferenceIntervalSeconds = Mathf.Max(0.05f, inferenceIntervalSeconds);
            minimumConfidence = Mathf.Clamp01(minimumConfidence);
            duplicateIouThreshold = Mathf.Clamp01(duplicateIouThreshold);
            nonMaxSuppressionIouThreshold = Mathf.Clamp01(nonMaxSuppressionIouThreshold);
            trackTtlSeconds = Mathf.Max(0.1f, trackTtlSeconds);
            retentionDays = Mathf.Clamp(retentionDays, 1, 365);
            syncEndpoint = syncEndpoint == null ? string.Empty : syncEndpoint.Trim();
            privacyNoticeVersion = privacyNoticeVersion == null ? string.Empty : privacyNoticeVersion.Trim();
        }

        private static string Clean(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
