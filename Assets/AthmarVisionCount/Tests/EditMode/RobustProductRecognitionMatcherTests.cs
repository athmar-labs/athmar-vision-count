using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class RobustProductRecognitionMatcherTests
    {
        [Test]
        public void AcceptsKnownProductWhenSeveralReferencesAgree()
        {
            var catalogue = Catalogue(
                Product("CUP", 0.96f, 0.95f, 0.94f, 0.90f, 0.88f, 0.87f, 0.86f, 0.85f),
                Product("BOTTLE", 0.45f, 0.40f, 0.35f, 0.30f, 0.25f, 0.20f, 0.15f, 0.10f));

            var matcher = new RobustProductRecognitionMatcher();
            var result = matcher.Match(Query(), catalogue);

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.IsAmbiguous, Is.False);
            Assert.That(result.Sku, Is.EqualTo("CUP"));
            Assert.That(result.Similarity, Is.GreaterThan(0.94f));
        }

        [Test]
        public void RejectsSingleSpuriousHighReferenceAsUnknown()
        {
            var catalogue = Catalogue(
                Product("BOTTLE", 0.88f, 0.42f, 0.38f, 0.30f, 0.25f, 0.20f, 0.15f, 0.10f));

            var matcher = new RobustProductRecognitionMatcher();
            var result = matcher.Match(Query(), catalogue);

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.IsAmbiguous, Is.False);
            Assert.That(result.Similarity, Is.LessThan(RobustProductRecognitionMatcher.DefaultMinimumSimilarity));
        }

        [Test]
        public void FailsClosedWhenTwoProductsHaveConsensusButScoresAreTooClose()
        {
            var catalogue = Catalogue(
                Product("A", 0.95f, 0.94f, 0.93f, 0.91f, 0.90f, 0.89f, 0.88f, 0.87f),
                Product("B", 0.94f, 0.93f, 0.92f, 0.90f, 0.89f, 0.88f, 0.87f, 0.86f));

            var matcher = new RobustProductRecognitionMatcher();
            var result = matcher.Match(Query(), catalogue);

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.IsAmbiguous, Is.True);
        }

        private static float[] Query()
        {
            return new[] { 1f, 0f, 0f, 0f };
        }

        private static ProductEnrollmentCatalogueData Catalogue(params ProductEnrollmentRecord[] products)
        {
            return new ProductEnrollmentCatalogueData
            {
                customerCode = "TEST",
                products = new List<ProductEnrollmentRecord>(products)
            };
        }

        private static ProductEnrollmentRecord Product(string sku, params float[] similarities)
        {
            var references = new List<ProductReferenceEmbedding>(similarities.Length);
            for (var index = 0; index < similarities.Length; index++)
            {
                var cosine = Math.Max(-1d, Math.Min(1d, similarities[index]));
                references.Add(new ProductReferenceEmbedding
                {
                    values = new[]
                    {
                        (float)cosine,
                        (float)Math.Sqrt(Math.Max(0d, 1d - cosine * cosine)),
                        0f,
                        0f
                    }
                });
            }

            return new ProductEnrollmentRecord
            {
                sku = sku,
                nameEnglish = sku,
                active = true,
                references = references
            };
        }
    }
}
