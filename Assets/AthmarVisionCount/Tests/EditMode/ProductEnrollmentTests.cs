using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class ProductEnrollmentTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "athmar-product-enrollment-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [TestCase(5)]
        [TestCase(20)]
        [TestCase(100)]
        public void StoreScalesThroughPlannedMvpProductCounts(int productCount)
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);

            for (var index = 0; index < productCount; index++)
            {
                store.UpsertProduct(
                    new ProductEnrollmentDraft(
                        $"SKU-{index:000}",
                        $"Product {index}",
                        $"منتج {index}",
                        $"6281000{index:0000}"),
                    BuildReferences(index));
            }

            var loaded = new ProductEnrollmentStore("CUSTOMER-A", _root).Load();
            Assert.That(loaded.customerCode, Is.EqualTo("CUSTOMER-A"));
            Assert.That(loaded.products.Count, Is.EqualTo(productCount));
            Assert.That(loaded.products[productCount - 1].references.Count,
                Is.EqualTo(ProductEnrollmentStore.MinimumReferenceCount));
        }

        [Test]
        public void CustomerScopesDoNotLeakProductsAcrossTenants()
        {
            var first = new ProductEnrollmentStore("CUSTOMER-A", _root);
            var second = new ProductEnrollmentStore("CUSTOMER-B", _root);

            first.UpsertProduct(
                new ProductEnrollmentDraft("A-001", "Red Bottle", "زجاجة حمراء", "111"),
                BuildReferences(1));
            second.UpsertProduct(
                new ProductEnrollmentDraft("B-001", "Blue Bottle", "زجاجة زرقاء", "222"),
                BuildReferences(2));

            var firstLoaded = first.Load();
            var secondLoaded = second.Load();

            Assert.That(firstLoaded.products.Count, Is.EqualTo(1));
            Assert.That(firstLoaded.products[0].sku, Is.EqualTo("A-001"));
            Assert.That(secondLoaded.products.Count, Is.EqualTo(1));
            Assert.That(secondLoaded.products[0].sku, Is.EqualTo("B-001"));
            Assert.That(first.CustomerDirectory, Is.Not.EqualTo(second.CustomerDirectory));
        }

        [Test]
        public void EnrollmentPersistsEmbeddingsWithoutPersistingRawImages()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("SKU-001", "Bottle", "زجاجة", "628100001"),
                BuildReferences(4));

            var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0));
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                Assert.That(extension, Is.Not.EqualTo(".png"));
                Assert.That(extension, Is.Not.EqualTo(".jpg"));
                Assert.That(extension, Is.Not.EqualTo(".jpeg"));
                Assert.That(extension, Is.Not.EqualTo(".webp"));
            }

            var reloaded = store.Load();
            Assert.That(reloaded.products[0].references.Count,
                Is.EqualTo(ProductEnrollmentStore.MinimumReferenceCount));
            Assert.That(reloaded.products[0].references[0].values.Length, Is.EqualTo(16));
        }

        [Test]
        public void DeleteAllLocalDataPurgesEveryCustomerEnrollmentScope()
        {
            var first = new ProductEnrollmentStore("CUSTOMER-A", _root);
            var second = new ProductEnrollmentStore("CUSTOMER-B", _root);
            first.UpsertProduct(
                new ProductEnrollmentDraft("A-001", "Bottle", "زجاجة", "111"),
                BuildReferences(1));
            second.UpsertProduct(
                new ProductEnrollmentDraft("B-001", "Cup", "كوب", "222"),
                BuildReferences(2));

            Assert.That(Directory.Exists(first.CustomerDirectory), Is.True);
            Assert.That(Directory.Exists(second.CustomerDirectory), Is.True);

            ProductEnrollmentStore.DeleteAllLocalData(_root);

            Assert.That(Directory.Exists(_root), Is.False);
        }

        [Test]
        public void StoreRejectsTooFewReferenceViews()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            var tooFew = BuildReferences(1, ProductEnrollmentStore.MinimumReferenceCount - 1);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                store.UpsertProduct(
                    new ProductEnrollmentDraft("SKU-001", "Bottle", "زجاجة", ""),
                    tooFew));

            StringAssert.Contains("Capture", exception.Message);
        }

        [Test]
        public void StoreRejectsDuplicateBarcodeAcrossProducts()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("SKU-001", "Bottle", "زجاجة", "628100001"),
                BuildReferences(1));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                store.UpsertProduct(
                    new ProductEnrollmentDraft("SKU-002", "Cup", "كوب", "628100001"),
                    BuildReferences(2)));

            StringAssert.Contains("already assigned", exception.Message);
        }

        [Test]
        public void UpdatingSameSkuReplacesReferencesWithoutGrowingProductCount()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("SKU-001", "Bottle", "زجاجة", "111"),
                BuildReferences(1));
            store.UpsertProduct(
                new ProductEnrollmentDraft("sku-001", "Bottle Updated", "زجاجة محدثة", "111"),
                BuildReferences(7));

            var loaded = store.Load();
            Assert.That(loaded.products.Count, Is.EqualTo(1));
            Assert.That(loaded.products[0].nameEnglish, Is.EqualTo("Bottle Updated"));
        }

        [Test]
        public void MatcherReturnsBestSkuWhenSimilarityAndMarginPass()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("RED", "Red", "أحمر", ""),
                RepeatReference(new[] { 1f, 0f, 0f, 0f }));
            store.UpsertProduct(
                new ProductEnrollmentDraft("BLUE", "Blue", "أزرق", ""),
                RepeatReference(new[] { 0f, 1f, 0f, 0f }));

            var matcher = new ProductRecognitionMatcher(0.80f, 0.05f);
            var result = matcher.Match(new[] { 0.98f, 0.02f, 0f, 0f }, store.Load());

            Assert.That(result.IsMatch, Is.True);
            Assert.That(result.IsAmbiguous, Is.False);
            Assert.That(result.Sku, Is.EqualTo("RED"));
            Assert.That(result.Similarity, Is.GreaterThan(0.99f));
        }

        [Test]
        public void MatcherFailsClosedWhenBestProductsAreTooSimilar()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("A", "A", "أ", ""),
                RepeatReference(new[] { 1f, 0f, 0f, 0f }));
            store.UpsertProduct(
                new ProductEnrollmentDraft("B", "B", "ب", ""),
                RepeatReference(new[] { 0.995f, 0.1f, 0f, 0f }));

            var matcher = new ProductRecognitionMatcher(0.80f, 0.03f);
            var result = matcher.Match(new[] { 1f, 0.02f, 0f, 0f }, store.Load());

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.IsAmbiguous, Is.True);
        }

        [Test]
        public void MatcherRejectsUnknownBelowSimilarityThreshold()
        {
            var store = new ProductEnrollmentStore("CUSTOMER-A", _root);
            store.UpsertProduct(
                new ProductEnrollmentDraft("RED", "Red", "أحمر", ""),
                RepeatReference(new[] { 1f, 0f, 0f, 0f }));

            var matcher = new ProductRecognitionMatcher(0.90f, 0.03f);
            var result = matcher.Match(new[] { 0f, 1f, 0f, 0f }, store.Load());

            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.IsAmbiguous, Is.False);
        }

        private static IReadOnlyList<float[]> BuildReferences(
            int seed,
            int count = ProductEnrollmentStore.MinimumReferenceCount)
        {
            var references = new List<float[]>(count);
            for (var referenceIndex = 0; referenceIndex < count; referenceIndex++)
            {
                var values = new float[16];
                values[seed % values.Length] = 1f;
                values[(seed + referenceIndex + 1) % values.Length] = 0.02f + referenceIndex * 0.001f;
                references.Add(values);
            }
            return references;
        }

        private static IReadOnlyList<float[]> RepeatReference(float[] values)
        {
            var references = new List<float[]>(ProductEnrollmentStore.MinimumReferenceCount);
            for (var index = 0; index < ProductEnrollmentStore.MinimumReferenceCount; index++)
            {
                var copy = new float[values.Length];
                Array.Copy(values, copy, values.Length);
                references.Add(copy);
            }
            return references;
        }
    }
}
