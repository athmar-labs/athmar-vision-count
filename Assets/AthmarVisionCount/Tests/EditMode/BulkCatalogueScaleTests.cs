using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class BulkCatalogueScaleTests
    {
        private const int GateProductCount = 100;
        private const int CandidateLimit = 32;

        [Test]
        public void HundredSkuGateLoadsValidatesSearchesAndRejectsUnknown()
        {
            var csv = new StringBuilder("sku,name_en,name_ar,barcode,ocr_aliases,active\n");
            var vectors = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < GateProductCount; index++)
            {
                var sku = $"SKU-{index:0000}";
                var barcode = $"628100{index:000000}";
                csv.Append(sku)
                    .Append(",Product ").Append(index.ToString("0000"))
                    .Append(",منتج ").Append(index.ToString("0000"))
                    .Append(',').Append(barcode)
                    .Append(",ALIAS").Append(index.ToString("0000"))
                    .Append(",true\n");
                vectors.Add(sku, DeterministicVector(index));
            }

            var catalogue = BulkProductCatalogue.Parse(csv.ToString());
            Assert.That(catalogue.Count, Is.EqualTo(GateProductCount));

            using var stream = BuildIndex(
                SentisProductEmbeddingExtractor.ModelId,
                vectors);

            // One int8 1024-D prototype per SKU should stay close to ~1 KB/SKU plus metadata.
            Assert.That(stream.Length, Is.LessThan(120_000L));

            var embeddingIndex = BulkEmbeddingIndex.Load(stream);
            Assert.That(embeddingIndex.Count, Is.EqualTo(GateProductCount));
            Assert.That(embeddingIndex.Dimension, Is.EqualTo(SentisProductEmbeddingExtractor.FeatureDimension));
            Assert.DoesNotThrow(() => embeddingIndex.ValidateAgainst(
                catalogue,
                SentisProductEmbeddingExtractor.ModelId,
                SentisProductEmbeddingExtractor.FeatureDimension));

            foreach (var pair in vectors)
            {
                var result = embeddingIndex.Search(pair.Value, CandidateLimit);
                Assert.That(result.Sku, Is.EqualTo(pair.Key),
                    $"Exact prototype query must retrieve its own SKU inside the {CandidateLimit}-candidate shortlist.");
                Assert.That(result.Similarity, Is.GreaterThan(0.98f));
            }

            var resolver = new ProductEvidenceResolver(
                catalogue,
                embeddingIndex,
                ProductEvidenceResolver.DefaultBulkMinimumSimilarity,
                ProductEvidenceResolver.DefaultBulkMinimumMargin,
                CandidateLimit);

            var unknown = resolver.Resolve(new ProductRecognitionEvidence
            {
                VisualEmbedding = new float[SentisProductEmbeddingExtractor.FeatureDimension]
            });

            Assert.That(unknown.IsMatch, Is.False);
            Assert.That(unknown.IsAmbiguous, Is.False);
            Assert.That(unknown.Sku, Is.Empty);
        }

        private static float[] DeterministicVector(int productIndex)
        {
            var values = new float[SentisProductEmbeddingExtractor.FeatureDimension];
            var random = new Random(17_071 + productIndex * 7_919);
            for (var index = 0; index < values.Length; index++)
                values[index] = (float)(random.NextDouble() * 2d - 1d);

            ProductEmbeddingMath.NormalizeInPlace(values);
            return values;
        }

        private static MemoryStream BuildIndex(
            string modelId,
            IReadOnlyDictionary<string, float[]> products)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("ATHVEC01"));
                writer.Write(BulkEmbeddingIndex.FormatVersion);
                writer.Write(SentisProductEmbeddingExtractor.FeatureDimension);
                writer.Write(products.Count);

                var modelBytes = Encoding.UTF8.GetBytes(modelId);
                writer.Write(modelBytes.Length);
                writer.Write(modelBytes);

                foreach (var pair in products)
                {
                    var normalized = new float[pair.Value.Length];
                    Array.Copy(pair.Value, normalized, normalized.Length);
                    ProductEmbeddingMath.NormalizeInPlace(normalized);

                    var skuBytes = Encoding.UTF8.GetBytes(pair.Key);
                    writer.Write((ushort)skuBytes.Length);
                    writer.Write(skuBytes);
                    writer.Write(BulkEmbeddingIndex.ComputeProjectionSignature(normalized));

                    for (var index = 0; index < normalized.Length; index++)
                    {
                        var quantized = (sbyte)Math.Round(
                            Math.Max(-1f, Math.Min(1f, normalized[index])) * 127f);
                        writer.Write(quantized);
                    }
                }
            }

            stream.Position = 0;
            return stream;
        }
    }
}
