using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CustomerPackageTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "athmar-customer-package-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void ManifestRejectsNonHttpsAssets()
        {
            var manifest = CreateManifest("customer-a", new byte[] { 1, 2, 3 }, CatalogueBytes());
            manifest.modelUrl = "http://example.com/model.sentis";

            Assert.Throws<FormatException>(() => manifest.Validate());
        }

        [Test]
        public void HashVerificationRejectsModifiedContent()
        {
            var expected = CustomerPackageStore.ComputeSha256(new byte[] { 1, 2, 3 });

            Assert.Throws<InvalidDataException>(() =>
                CustomerPackageStore.VerifyBytesSha256(new byte[] { 1, 2, 4 }, expected, "test payload"));
        }

        [Test]
        public void StoreActivatesAndRollsBackValidatedPackages()
        {
            var store = new CustomerPackageStore(_root);
            WriteStagingPackage(store, "customer-a", new byte[] { 1, 2, 3 });
            var first = store.ActivateStaging();
            Assert.AreEqual("customer-a", first.Configuration.CustomerCode);

            WriteStagingPackage(store, "customer-b", new byte[] { 4, 5, 6 });
            var second = store.ActivateStaging();
            Assert.AreEqual("customer-b", second.Configuration.CustomerCode);
            Assert.IsTrue(store.HasPreviousPackage);

            var restored = store.Rollback();
            Assert.AreEqual("customer-a", restored.Configuration.CustomerCode);
        }

        [TestCase("123456")]
        [TestCase("123456789012")]
        public void AdministratorPinAcceptsSixToTwelveDigits(string pin)
        {
            Assert.DoesNotThrow(() => AdminPinStore.ValidatePin(pin));
        }

        [TestCase("12345")]
        [TestCase("1234567890123")]
        [TestCase("12345a")]
        public void AdministratorPinRejectsUnsafeValues(string pin)
        {
            Assert.Throws<FormatException>(() => AdminPinStore.ValidatePin(pin));
        }

        private static void WriteStagingPackage(CustomerPackageStore store, string customerCode, byte[] modelBytes)
        {
            var catalogueBytes = CatalogueBytes();
            var manifest = CreateManifest(customerCode, modelBytes, catalogueBytes);
            var staging = store.PrepareStagingDirectory();
            File.WriteAllText(
                Path.Combine(staging, CustomerPackageStore.ManifestFileName),
                JsonUtility.ToJson(manifest),
                Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(staging, CustomerPackageStore.ModelFileName), modelBytes);
            File.WriteAllBytes(Path.Combine(staging, CustomerPackageStore.CatalogueFileName), catalogueBytes);
        }

        private static CustomerPackageManifest CreateManifest(string customerCode, byte[] modelBytes, byte[] catalogueBytes)
        {
            return new CustomerPackageManifest
            {
                schemaVersion = 1,
                customerCode = customerCode,
                customerName = "Customer " + customerCode,
                defaultLanguage = "ar",
                modelVersion = "1.0.0",
                catalogueVersion = "1.0.0",
                privacyNoticeVersion = "1.0",
                modelUrl = "https://example.com/production.sentis",
                modelSha256 = CustomerPackageStore.ComputeSha256(modelBytes),
                catalogueUrl = "https://example.com/sku_catalogue.csv",
                catalogueSha256 = CustomerPackageStore.ComputeSha256(catalogueBytes),
                modelInputWidth = 640,
                modelInputHeight = 640,
                modelInputLayout = "Nchw",
                outputTensorIndex = 0,
                outputTensorLayout = "ChannelsFirst",
                outputHasObjectness = false,
                outputCoordinatesNormalized = false,
                maxDetections = 100,
                inferenceIntervalSeconds = 0.25f,
                minimumConfidence = 0.65f,
                duplicateIouThreshold = 0.45f,
                nonMaxSuppressionIouThreshold = 0.45f,
                trackTtlSeconds = 1.25f,
                retentionDays = 30
            };
        }

        private static byte[] CatalogueBytes()
        {
            return Encoding.UTF8.GetBytes(
                "label_index,sku,name_en,name_ar,active\n" +
                "0,SKU-001,Demo Product,منتج تجريبي,true\n");
        }
    }
}
