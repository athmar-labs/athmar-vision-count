using Unity.InferenceEngine;

namespace AthmarLabs.VisionCount
{
    public interface IVisionCountConfiguration
    {
        string ApplicationDisplayName { get; }
        string CustomerCode { get; }
        string DefaultLanguage { get; }
        ModelAsset ModelAsset { get; }
        string ModelFilePath { get; }
        string ModelVersion { get; }
        string CatalogueVersion { get; }
        int ModelInputWidth { get; }
        int ModelInputHeight { get; }
        ModelInputLayout InputLayout { get; }
        int OutputTensorIndex { get; }
        DetectionTensorLayout OutputTensorLayout { get; }
        bool OutputHasObjectness { get; }
        bool OutputCoordinatesNormalized { get; }
        int MaxDetections { get; }
        bool PreferGpu { get; }
        float InferenceIntervalSeconds { get; }
        float MinimumConfidence { get; }
        float DuplicateIouThreshold { get; }
        float NonMaxSuppressionIouThreshold { get; }
        float TrackTtlSeconds { get; }
        bool RequireHumanConfirmation { get; }
        bool StoreCapturedImages { get; }
        int RetentionDays { get; }
        string PrivacyNoticeVersion { get; }
        bool NetworkSyncEnabled { get; }
        string SyncEndpoint { get; }
    }
}
