using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [CreateAssetMenu(fileName = "AthmarVisionCountConfig", menuName = "Athmar/Vision Count/App Config")]
    public sealed class AppConfig : ScriptableObject
    {
        [Header("Model and catalogue")]
        [SerializeField] private Object modelAsset;
        [SerializeField] private TextAsset skuCatalogueCsv;
        [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.65f;
        [SerializeField, Range(0f, 1f)] private float duplicateIouThreshold = 0.45f;
        [SerializeField, Min(0.1f)] private float trackTtlSeconds = 1.25f;

        [Header("Safety and review")]
        [SerializeField] private bool requireHumanConfirmation = true;
        [SerializeField] private bool storeCapturedImages;
        [SerializeField, Min(1)] private int retentionDays = 30;
        [SerializeField] private string privacyNoticeVersion = "draft";

        [Header("Optional customer-approved sync")]
        [SerializeField] private bool networkSyncEnabled;
        [SerializeField] private string syncEndpoint = string.Empty;

        public Object ModelAsset => modelAsset;
        public TextAsset SkuCatalogueCsv => skuCatalogueCsv;
        public float MinimumConfidence => minimumConfidence;
        public float DuplicateIouThreshold => duplicateIouThreshold;
        public float TrackTtlSeconds => trackTtlSeconds;
        public bool RequireHumanConfirmation => requireHumanConfirmation;
        public bool StoreCapturedImages => storeCapturedImages;
        public int RetentionDays => retentionDays;
        public string PrivacyNoticeVersion => privacyNoticeVersion;
        public bool NetworkSyncEnabled => networkSyncEnabled;
        public string SyncEndpoint => syncEndpoint;

        private void OnValidate()
        {
            minimumConfidence = Mathf.Clamp01(minimumConfidence);
            duplicateIouThreshold = Mathf.Clamp01(duplicateIouThreshold);
            trackTtlSeconds = Mathf.Max(0.1f, trackTtlSeconds);
            retentionDays = Mathf.Max(1, retentionDays);
            syncEndpoint = syncEndpoint == null ? string.Empty : syncEndpoint.Trim();
            privacyNoticeVersion = privacyNoticeVersion == null ? string.Empty : privacyNoticeVersion.Trim();
        }
    }
}
