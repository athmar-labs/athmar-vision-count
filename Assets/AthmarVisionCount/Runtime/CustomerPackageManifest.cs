using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [Serializable]
    public sealed class CustomerPackageManifest
    {
        public int schemaVersion = 1;
        public string customerCode = string.Empty;
        public string customerName = string.Empty;
        public string defaultLanguage = "ar";
        public string modelVersion = string.Empty;
        public string catalogueVersion = string.Empty;
        public string privacyNoticeVersion = string.Empty;
        public string modelUrl = string.Empty;
        public string modelSha256 = string.Empty;
        public string catalogueUrl = string.Empty;
        public string catalogueSha256 = string.Empty;

        // Schema v2: customer product data for the generic embedding recognizer.
        public string bulkCatalogueUrl = string.Empty;
        public string bulkCatalogueSha256 = string.Empty;
        public string bulkEmbeddingIndexUrl = string.Empty;
        public string bulkEmbeddingIndexSha256 = string.Empty;
        public string bulkEmbeddingModelId = string.Empty;
        public float bulkMinimumSimilarity = ProductEvidenceResolver.DefaultBulkMinimumSimilarity;
        public float bulkMinimumMargin = ProductEvidenceResolver.DefaultBulkMinimumMargin;
        public int bulkCandidateLimit = BulkEmbeddingIndex.DefaultCandidateLimit;

        public int modelInputWidth = 640;
        public int modelInputHeight = 640;
        public string modelInputLayout = "Nchw";
        public int outputTensorIndex;
        public string outputTensorLayout = "ChannelsFirst";
        public bool outputHasObjectness;
        public bool outputCoordinatesNormalized;
        public int maxDetections = 100;
        public bool preferGpu = true;
        public float inferenceIntervalSeconds = 0.25f;
        public float minimumConfidence = 0.65f;
        public float duplicateIouThreshold = 0.45f;
        public float nonMaxSuppressionIouThreshold = 0.45f;
        public float trackTtlSeconds = 1.25f;
        public int retentionDays = 30;
        public bool networkSyncEnabled;
        public string syncEndpoint = string.Empty;

        public bool HasBulkCatalogue => schemaVersion >= 2;

        public static CustomerPackageManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("The customer manifest is empty.");

            var manifest = JsonUtility.FromJson<CustomerPackageManifest>(json);
            if (manifest == null)
                throw new FormatException("The customer manifest is not valid JSON.");
            manifest.Validate();
            return manifest;
        }

        public void Validate()
        {
            if (schemaVersion != 1 && schemaVersion != 2)
                throw new FormatException($"Unsupported customer manifest schema {schemaVersion}.");

            customerCode = RequireText(customerCode, "customerCode");
            customerName = RequireText(customerName, "customerName");
            defaultLanguage = RequireText(defaultLanguage, "defaultLanguage").ToLowerInvariant();
            if (defaultLanguage != "ar" && defaultLanguage != "en")
                throw new FormatException("defaultLanguage must be ar or en.");

            modelVersion = RequireText(modelVersion, "modelVersion");
            catalogueVersion = RequireText(catalogueVersion, "catalogueVersion");
            privacyNoticeVersion = RequireText(privacyNoticeVersion, "privacyNoticeVersion");
            if (string.Equals(privacyNoticeVersion, "draft", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("privacyNoticeVersion must identify an approved notice.");

            modelUrl = RequireHttpsUrl(modelUrl, "modelUrl");
            catalogueUrl = RequireHttpsUrl(catalogueUrl, "catalogueUrl");
            modelSha256 = NormalizeSha256(modelSha256, "modelSha256");
            catalogueSha256 = NormalizeSha256(catalogueSha256, "catalogueSha256");

            if (HasBulkCatalogue)
            {
                bulkCatalogueUrl = RequireHttpsUrl(bulkCatalogueUrl, "bulkCatalogueUrl");
                bulkCatalogueSha256 = NormalizeSha256(bulkCatalogueSha256, "bulkCatalogueSha256");
                bulkEmbeddingIndexUrl = RequireHttpsUrl(bulkEmbeddingIndexUrl, "bulkEmbeddingIndexUrl");
                bulkEmbeddingIndexSha256 = NormalizeSha256(bulkEmbeddingIndexSha256, "bulkEmbeddingIndexSha256");
                bulkEmbeddingModelId = RequireText(bulkEmbeddingModelId, "bulkEmbeddingModelId");
                RequireRange(bulkMinimumSimilarity, -1f, 1f, "bulkMinimumSimilarity");
                RequireRange(bulkMinimumMargin, 0f, 2f, "bulkMinimumMargin");
                if (bulkCandidateLimit < 16 || bulkCandidateLimit > 4096)
                    throw new FormatException("bulkCandidateLimit must be from 16 to 4096.");
            }
            else
            {
                bulkCatalogueUrl = string.Empty;
                bulkCatalogueSha256 = string.Empty;
                bulkEmbeddingIndexUrl = string.Empty;
                bulkEmbeddingIndexSha256 = string.Empty;
                bulkEmbeddingModelId = string.Empty;
            }

            if (modelInputWidth < 32 || modelInputWidth > 4096 || modelInputHeight < 32 || modelInputHeight > 4096)
                throw new FormatException("Model dimensions must be from 32 to 4096 pixels.");
            if (!Enum.TryParse(modelInputLayout, true, out ModelInputLayout parsedInputLayout))
                throw new FormatException("modelInputLayout must be Nchw or Nhwc.");
            modelInputLayout = parsedInputLayout.ToString();
            if (outputTensorIndex < 0 || outputTensorIndex > 32)
                throw new FormatException("outputTensorIndex must be from 0 to 32.");
            if (!Enum.TryParse(outputTensorLayout, true, out DetectionTensorLayout parsedOutputLayout))
                throw new FormatException("outputTensorLayout must be ChannelsFirst or RowsFirst.");
            outputTensorLayout = parsedOutputLayout.ToString();
            if (maxDetections < 1 || maxDetections > 500)
                throw new FormatException("maxDetections must be from 1 to 500.");
            RequireRange(inferenceIntervalSeconds, 0.05f, 5f, "inferenceIntervalSeconds");
            RequireRange(minimumConfidence, 0f, 1f, "minimumConfidence");
            RequireRange(duplicateIouThreshold, 0f, 1f, "duplicateIouThreshold");
            RequireRange(nonMaxSuppressionIouThreshold, 0f, 1f, "nonMaxSuppressionIouThreshold");
            RequireRange(trackTtlSeconds, 0.1f, 30f, "trackTtlSeconds");
            if (retentionDays < 1 || retentionDays > 365)
                throw new FormatException("retentionDays must be from 1 to 365.");

            syncEndpoint = string.IsNullOrWhiteSpace(syncEndpoint) ? string.Empty : syncEndpoint.Trim();
            if (networkSyncEnabled)
                syncEndpoint = RequireHttpsUrl(syncEndpoint, "syncEndpoint");
        }

        public RuntimeCustomerConfiguration CreateConfiguration(string modelFilePath)
        {
            Validate();
            if (string.IsNullOrWhiteSpace(modelFilePath))
                throw new ArgumentException("A runtime model path is required.", nameof(modelFilePath));
            return new RuntimeCustomerConfiguration(this, modelFilePath);
        }

        public static string NormalizeSha256(string value, string fieldName)
        {
            value = RequireText(value, fieldName).ToLowerInvariant();
            if (value.Length != 64)
                throw new FormatException($"{fieldName} must contain a 64-character SHA-256 digest.");
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var valid = character >= '0' && character <= '9' || character >= 'a' && character <= 'f';
                if (!valid)
                    throw new FormatException($"{fieldName} contains a non-hexadecimal character.");
            }
            return value;
        }

        public static string RequireHttpsUrl(string value, string fieldName)
        {
            value = RequireText(value, fieldName);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"{fieldName} must be an absolute HTTPS URL.");
            }
            return uri.AbsoluteUri;
        }

        private static string RequireText(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException($"The customer manifest field {fieldName} is required.");
            return value.Trim();
        }

        private static void RequireRange(float value, float minimum, float maximum, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new FormatException($"{fieldName} must be from {minimum} to {maximum}.");
        }
    }

    public sealed class RuntimeCustomerConfiguration : IVisionCountConfiguration
    {
        private readonly CustomerPackageManifest _manifest;

        internal RuntimeCustomerConfiguration(CustomerPackageManifest manifest, string modelFilePath)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            ModelFilePath = modelFilePath;
        }

        public string ApplicationDisplayName => _manifest.customerName;
        public string CustomerCode => _manifest.customerCode;
        public string DefaultLanguage => _manifest.defaultLanguage;
        public ModelAsset ModelAsset => null;
        public string ModelFilePath { get; }
        public string ModelVersion => _manifest.modelVersion;
        public string CatalogueVersion => _manifest.catalogueVersion;
        public int ModelInputWidth => _manifest.modelInputWidth;
        public int ModelInputHeight => _manifest.modelInputHeight;
        public ModelInputLayout InputLayout => Enum.TryParse(_manifest.modelInputLayout, true, out ModelInputLayout value) ? value : ModelInputLayout.Nchw;
        public int OutputTensorIndex => _manifest.outputTensorIndex;
        public DetectionTensorLayout OutputTensorLayout => Enum.TryParse(_manifest.outputTensorLayout, true, out DetectionTensorLayout value) ? value : DetectionTensorLayout.ChannelsFirst;
        public bool OutputHasObjectness => _manifest.outputHasObjectness;
        public bool OutputCoordinatesNormalized => _manifest.outputCoordinatesNormalized;
        public int MaxDetections => _manifest.maxDetections;
        public bool PreferGpu => _manifest.preferGpu;
        public float InferenceIntervalSeconds => _manifest.inferenceIntervalSeconds;
        public float MinimumConfidence => _manifest.minimumConfidence;
        public float DuplicateIouThreshold => _manifest.duplicateIouThreshold;
        public float NonMaxSuppressionIouThreshold => _manifest.nonMaxSuppressionIouThreshold;
        public float TrackTtlSeconds => _manifest.trackTtlSeconds;
        public bool RequireHumanConfirmation => true;
        public bool StoreCapturedImages => false;
        public int RetentionDays => _manifest.retentionDays;
        public string PrivacyNoticeVersion => _manifest.privacyNoticeVersion;
        public bool NetworkSyncEnabled => _manifest.networkSyncEnabled;
        public string SyncEndpoint => _manifest.syncEndpoint;
    }
}
