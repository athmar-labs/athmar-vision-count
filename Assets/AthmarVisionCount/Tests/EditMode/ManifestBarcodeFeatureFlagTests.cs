using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class ManifestBarcodeFeatureFlagTests
    {
        [Test]
        public void SchemaV1CannotEnableBarcodeEvidence()
        {
            var manifest = BaseManifest();
            manifest.schemaVersion = 1;
            manifest.barcodeEvidenceEnabled = true;

            manifest.Validate();

            Assert.That(manifest.BarcodeEvidenceEnabled, Is.False);
            Assert.That(manifest.barcodeEvidenceEnabled, Is.False);
        }

        [Test]
        public void SchemaV2PreservesExplicitBarcodeOptIn()
        {
            var manifest = BaseManifest();
            manifest.schemaVersion = 2;
            manifest.bulkCatalogueUrl = "https://example.com/bulk_products.csv";
            manifest.bulkCatalogueSha256 = new string('a', 64);
            manifest.bulkEmbeddingIndexUrl = "https://example.com/bulk_embeddings.bin";
            manifest.bulkEmbeddingIndexSha256 = new string('b', 64);
            manifest.bulkEmbeddingModelId = SentisProductEmbeddingExtractor.ModelId;
            manifest.barcodeEvidenceEnabled = true;

            manifest.Validate();

            Assert.That(manifest.BarcodeEvidenceEnabled, Is.True);
        }

        private static CustomerPackageManifest BaseManifest()
        {
            return new CustomerPackageManifest
            {
                customerCode = "TEST",
                customerName = "Test Customer",
                defaultLanguage = "ar",
                modelVersion = "1",
                catalogueVersion = "1",
                privacyNoticeVersion = "1",
                modelUrl = "https://example.com/production.sentis",
                modelSha256 = new string('1', 64),
                catalogueUrl = "https://example.com/sku_catalogue.csv",
                catalogueSha256 = new string('2', 64),
                modelInputWidth = 640,
                modelInputHeight = 640,
                modelInputLayout = "Nchw",
                outputTensorLayout = "ChannelsFirst",
                maxDetections = 100,
                inferenceIntervalSeconds = 0.25f,
                minimumConfidence = 0.65f,
                duplicateIouThreshold = 0.45f,
                nonMaxSuppressionIouThreshold = 0.45f,
                trackTtlSeconds = 1.25f,
                retentionDays = 30
            };
        }
    }
}
