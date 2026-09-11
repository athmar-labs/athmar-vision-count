using System;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class SentisProductEmbeddingExtractorTests
    {
        [Test]
        public void PreparedGenericModelRunsThroughSentisAndReturnsNormalized1024DFeatures()
        {
            var modelAsset = Resources.Load<ModelAsset>(SentisProductEmbeddingExtractor.ResourceName);
            var required = string.Equals(
                Environment.GetEnvironmentVariable("ATHMAR_REQUIRE_GENERIC_EMBEDDING"),
                "1",
                StringComparison.Ordinal);

            if (modelAsset == null && !required)
                Assert.Ignore("Generated generic embedding model is not present in this developer checkout.");

            Assert.That(modelAsset, Is.Not.Null,
                "CI requires the prepared generic embedding model to be imported as a Unity ModelAsset.");

            var first = BuildPatternTexture(false);
            var second = BuildPatternTexture(true);
            try
            {
                using var extractor = new SentisProductEmbeddingExtractor(modelAsset, BackendType.CPU);
                var firstEmbedding = extractor.Extract(first);
                var secondEmbedding = extractor.Extract(second);

                Assert.That(firstEmbedding.Length, Is.EqualTo(SentisProductEmbeddingExtractor.FeatureDimension));
                Assert.That(secondEmbedding.Length, Is.EqualTo(SentisProductEmbeddingExtractor.FeatureDimension));
                Assert.That(VectorNorm(firstEmbedding), Is.EqualTo(1f).Within(0.001f));
                Assert.That(VectorNorm(secondEmbedding), Is.EqualTo(1f).Within(0.001f));

                var similarity = ProductEmbeddingMath.CosineSimilarity(firstEmbedding, secondEmbedding);
                Assert.That(similarity, Is.LessThan(0.9999f),
                    "Two deliberately different image patterns must not collapse to an identical embedding.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        private static Texture2D BuildPatternTexture(bool inverted)
        {
            var texture = new Texture2D(224, 224, TextureFormat.RGBA32, false);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var left = x < texture.width / 2;
                    var top = y >= texture.height / 2;
                    var bright = inverted ? left == top : left != top;
                    texture.SetPixel(x, y, bright
                        ? new Color(0.95f, 0.20f, 0.10f, 1f)
                        : new Color(0.05f, 0.25f, 0.90f, 1f));
                }
            }
            texture.Apply(false, false);
            return texture;
        }

        private static float VectorNorm(float[] values)
        {
            double sum = 0d;
            for (var index = 0; index < values.Length; index++)
                sum += values[index] * values[index];
            return (float)Math.Sqrt(sum);
        }
    }
}
