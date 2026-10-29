using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class BulkCatalogueBootstrapTests
    {
        [Test]
        public void CatalogueResolvesExactBarcodeAndUniqueOcrAlias()
        {
            var catalogue = BulkProductCatalogue.Parse(
                "sku,name_en,name_ar,barcode,ocr_aliases,active\n" +
                "KENT-001,Kent Gold,كنت جولد,628100001,KENT GOLD|KENTGOLD,true\n" +
                "ROCO-001,Roco,روكو,628100002,ROCO PACK,true\n");

            Assert.That(catalogue.Count, Is.EqualTo(2));
            Assert.That(catalogue.TryGetByBarcode("6281-00001", out var barcode), Is.True);
            Assert.That(barcode.Sku, Is.EqualTo("KENT-001"));

            Assert.That(catalogue.TryResolveOcrText("price KENT GOLD 20", out var ocr, out var ambiguous), Is.True);
            Assert.That(ambiguous, Is.False);
            Assert.That(ocr.Sku, Is.EqualTo("KENT-001"));
        }

        [Test]
        public void DuplicateOcrAliasFailsClosedAsAmbiguous()
        {
            var catalogue = BulkProductCatalogue.Parse(
                "sku,name_en,name_ar,barcode,ocr_aliases,active\n" +
                "A,Alpha,ألفا,,SAME PACK,true\n" +
                "B,Beta,بيتا,,SAME PACK,true\n");

            Assert.That(catalogue.TryResolveOcrText("SAME PACK", out _, out var ambiguous), Is.False);
            Assert.That(ambiguous, Is.True);
        }

        [Test]
        public void CompactEmbeddingIndexFindsNearestPrototype()
        {
            using var stream = BuildIndex(
                "test-model",
                new Dictionary<string, float[]>
                {
                    ["A"] = new[] { 1f, 0f, 0f, 0f },
                    ["B"] = new[] { 0f, 1f, 0f, 0f },
                    ["C"] = new[] { 0f, 0f, 1f, 0f }
                });
            var index = BulkEmbeddingIndex.Load(stream);

            var result = index.Search(new[] { 0.99f, 0.01f, 0f, 0f }, 3);

            Assert.That(result.Sku, Is.EqualTo("A"));
            Assert.That(result.Similarity, Is.GreaterThan(0.98f));
            Assert.That(result.RunnerUpSimilarity, Is.LessThan(0.1f));
        }

        [Test]
        public void BarcodeEvidenceWinsWithoutVisualGuessing()
        {
            var catalogue = BulkProductCatalogue.Parse(
                "sku,name_en,name_ar,barcode,ocr_aliases,active\n" +
                "A,Alpha,ألفا,111111,ALPHA PACK,true\n" +
                "B,Beta,بيتا,222222,BETA PACK,true\n");
            using var stream = BuildIndex(
                "test-model",
                new Dictionary<string, float[]>
                {
                    ["A"] = new[] { 1f, 0f, 0f, 0f },
                    ["B"] = new[] { 0f, 1f, 0f, 0f }
                });
            var resolver = new ProductEvidenceResolver(catalogue, BulkEmbeddingIndex.Load(stream));

            var result = resolver.Resolve(new ProductRecognitionEvidence
            {
                Barcode = "222222",
                VisualEmbedding = new[] { 1f, 0f, 0f, 0f }
            });

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.Sku, Is.EqualTo("B"));
            Assert.That(result.Source, Is.EqualTo(ProductRecognitionSource.Barcode));
        }

        [Test]
        public void ManualHardCaseOverlayCanResolveProductMissingFromBulkVectors()
        {
            var catalogue = BulkProductCatalogue.Parse(
                "sku,name_en,name_ar,barcode,ocr_aliases,active\n" +
                "BULK,Bulk,جماعي,,,true\n");
            using var stream = BuildIndex(
                "test-model",
                new Dictionary<string, float[]>
                {
                    ["BULK"] = new[] { 0f, 1f, 0f, 0f }
                });
            var resolver = new ProductEvidenceResolver(catalogue, BulkEmbeddingIndex.Load(stream));
            var manual = ManualCatalogue("HARD", new[] { 1f, 0f, 0f, 0f });

            var result = resolver.Resolve(
                new ProductRecognitionEvidence { VisualEmbedding = new[] { 1f, 0f, 0f, 0f } },
                manual);

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.Sku, Is.EqualTo("HARD"));
            Assert.That(result.Source, Is.EqualTo(ProductRecognitionSource.ManualHardCase));
        }

        [Test]
        public void ManifestV2RequiresBulkAssetsAndCompatibleModelIdentity()
        {
            var manifest = BaseManifest();
            manifest.schemaVersion = 2;
            Assert.Throws<FormatException>(() => manifest.Validate());

            manifest.bulkCatalogueUrl = "https://example.com/bulk_products.csv";
            manifest.bulkCatalogueSha256 = new string('a', 64);
            manifest.bulkEmbeddingIndexUrl = "https://example.com/bulk_embeddings.bin";
            manifest.bulkEmbeddingIndexSha256 = new string('b', 64);
            manifest.bulkEmbeddingModelId = SentisProductEmbeddingExtractor.ModelId;
            Assert.DoesNotThrow(() => manifest.Validate());
        }

        [Test]
        public void EmbeddingIndexRejectsDifferentModelOrUnknownSku()
        {
            var catalogue = BulkProductCatalogue.Parse(
                "sku,name_en,name_ar,barcode,ocr_aliases,active\n" +
                "A,Alpha,ألفا,,,true\n");
            using var wrongModel = BuildIndex(
                "wrong-model",
                new Dictionary<string, float[]> { ["A"] = UnitVector(SentisProductEmbeddingExtractor.FeatureDimension, 0) });
            var wrong = BulkEmbeddingIndex.Load(wrongModel);
            Assert.Throws<InvalidDataException>(() =>
                wrong.ValidateAgainst(catalogue, SentisProductEmbeddingExtractor.ModelId, SentisProductEmbeddingExtractor.FeatureDimension));

            using var unknownSku = BuildIndex(
                SentisProductEmbeddingExtractor.ModelId,
                new Dictionary<string, float[]> { ["B"] = UnitVector(SentisProductEmbeddingExtractor.FeatureDimension, 0) });
            var unknown = BulkEmbeddingIndex.Load(unknownSku);
            Assert.Throws<InvalidDataException>(() =>
                unknown.ValidateAgainst(catalogue, SentisProductEmbeddingExtractor.ModelId, SentisProductEmbeddingExtractor.FeatureDimension));
        }

        private static CustomerPackageManifest BaseManifest()
        {
            return new CustomerPackageManifest
            {
                schemaVersion = 1,
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

        private static ProductEnrollmentCatalogueData ManualCatalogue(string sku, float[] values)
        {
            var references = new List<ProductReferenceEmbedding>();
            for (var index = 0; index < ProductEnrollmentStore.MinimumReferenceCount; index++)
            {
                var copy = new float[values.Length];
                Array.Copy(values, copy, values.Length);
                references.Add(new ProductReferenceEmbedding { values = copy });
            }
            return new ProductEnrollmentCatalogueData
            {
                customerCode = "TEST",
                products = new List<ProductEnrollmentRecord>
                {
                    new ProductEnrollmentRecord
                    {
                        sku = sku,
                        nameEnglish = sku,
                        active = true,
                        references = references
                    }
                }
            };
        }

        private static float[] UnitVector(int dimension, int index)
        {
            var values = new float[dimension];
            values[index] = 1f;
            return values;
        }

        private static MemoryStream BuildIndex(string modelId, IReadOnlyDictionary<string, float[]> products)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("ATHVEC01"));
                writer.Write(BulkEmbeddingIndex.FormatVersion);
                var dimension = -1;
                foreach (var pair in products)
                {
                    dimension = pair.Value.Length;
                    break;
                }
                writer.Write(dimension);
                writer.Write(products.Count);
                var modelBytes = Encoding.UTF8.GetBytes(modelId);
                writer.Write(modelBytes.Length);
                writer.Write(modelBytes);

                foreach (var pair in products)
                {
                    if (pair.Value.Length != dimension)
                        throw new InvalidOperationException("Test vectors must share one dimension.");
                    var normalized = new float[dimension];
                    Array.Copy(pair.Value, normalized, dimension);
                    ProductEmbeddingMath.NormalizeInPlace(normalized);
                    var skuBytes = Encoding.UTF8.GetBytes(pair.Key);
                    writer.Write((ushort)skuBytes.Length);
                    writer.Write(skuBytes);
                    writer.Write(BulkEmbeddingIndex.ComputeProjectionSignature(normalized));
                    for (var index = 0; index < normalized.Length; index++)
                    {
                        var quantized = (sbyte)Math.Round(Math.Max(-1f, Math.Min(1f, normalized[index])) * 127f);
                        writer.Write(quantized);
                    }
                }
            }
            stream.Position = 0;
            return stream;
        }
    }
}
