using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [Test]
        public void ActivePackageRetentionOverridesBundledFallbackWithoutDeletingOtherCustomer()
        {
            var store = new CustomerPackageStore(Path.Combine(_root, "packages"));
            WriteStagingPackage(store, "customer-a", new byte[] { 1, 2, 3 }, retentionDays: 7);
            store.ActivateStaging();
            var sessions = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var exports = new ExportFileService(Path.Combine(_root, "exports"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

            var expiredA = BuildSession("active-expired", now.AddDays(-10), "customer-a");
            sessions.Save(expiredA);
            var exportA = exports.SaveConfirmedSession(expiredA);
            File.SetLastWriteTimeUtc(exportA, now.AddDays(-10));

            var oldB = BuildSession("other-customer-old", now.AddDays(-60), "customer-b");
            sessions.Save(oldB);
            var exportB = exports.SaveConfirmedSession(oldB);
            File.SetLastWriteTimeUtc(exportB, now.AddDays(-60));

            LocalRetentionEnforcer.ApplyRetentionPolicy(
                store,
                "bundled-customer",
                30,
                sessions,
                exports,
                now);

            var remaining = sessions.LoadAll();
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].CustomerCode, Is.EqualTo("customer-b"));
            Assert.That(File.Exists(exportA), Is.False);
            Assert.That(File.Exists(exportB), Is.True);
        }

        [Test]
        public void MalformedActivePackageUsesBundledRetentionFallbackWithoutTouchingOtherCustomer()
        {
            var store = new CustomerPackageStore(Path.Combine(_root, "packages"));
            WriteStagingPackage(store, "customer-a", new byte[] { 1, 2, 3 }, retentionDays: 7);
            store.ActivateStaging();
            File.WriteAllText(Path.Combine(store.ActiveDirectory, CustomerPackageStore.ManifestFileName), "{invalid", Encoding.UTF8);
            var sessions = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var exports = new ExportFileService(Path.Combine(_root, "exports"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

            sessions.Save(BuildSession("fallback-recent", now.AddDays(-10), "bundled-customer"));
            sessions.Save(BuildSession("fallback-expired", now.AddDays(-31), "bundled-customer"));
            sessions.Save(BuildSession("other-customer-expired", now.AddDays(-100), "customer-a"));

            LocalRetentionEnforcer.ApplyRetentionPolicy(
                store,
                "bundled-customer",
                30,
                sessions,
                exports,
                now);

            var remaining = sessions.LoadAll();
            Assert.That(remaining, Has.Count.EqualTo(2));
            Assert.That(remaining.Any(session => session.SessionId == "fallback-recent"), Is.True);
            Assert.That(remaining.Any(session => session.SessionId == "other-customer-expired"), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(366)]
        public void InvalidBundledRetentionNeverDeletesLocalData(int retentionDays)
        {
            var store = new CustomerPackageStore(Path.Combine(_root, "packages"));
            var sessions = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var exports = new ExportFileService(Path.Combine(_root, "exports"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            var oldSession = BuildSession("must-remain", now.AddDays(-400), "bundled-customer");
            sessions.Save(oldSession);
            var exportPath = exports.SaveConfirmedSession(oldSession);
            File.SetLastWriteTimeUtc(exportPath, now.AddDays(-400));

            LocalRetentionEnforcer.ApplyRetentionPolicy(
                store,
                "bundled-customer",
                retentionDays,
                sessions,
                exports,
                now);

            Assert.That(sessions.LoadAll(), Has.Count.EqualTo(1));
            Assert.That(File.Exists(exportPath), Is.True);
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

        private static ScanSessionRecord BuildSession(string sessionId, DateTime completedAtUtc, string customerCode)
        {
            return new ScanSessionRecord
            {
                SessionId = sessionId,
                CustomerCode = customerCode,
                StartedAtUtc = completedAtUtc.AddMinutes(-1).ToString("O"),
                CompletedAtUtc = completedAtUtc.ToString("O"),
                Confirmed = true,
                OperatorReference = "operator",
                LocationReference = "location",
                Lines = new List<CountLine>
                {
                    new CountLine
                    {
                        Sku = "SKU-001",
                        DisplayName = "Product",
                        ProposedCount = 1,
                        ConfirmedCount = 1
                    }
                }
            };
        }

        private static void WriteStagingPackage(CustomerPackageStore store, string customerCode, byte[] modelBytes, int retentionDays = 30)
        {
            var catalogueBytes = CatalogueBytes();
            var manifest = CreateManifest(customerCode, modelBytes, catalogueBytes, retentionDays);
            var staging = store.PrepareStagingDirectory();
            File.WriteAllText(
                Path.Combine(staging, CustomerPackageStore.ManifestFileName),
                JsonUtility.ToJson(manifest),
                Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(staging, CustomerPackageStore.ModelFileName), modelBytes);
            File.WriteAllBytes(Path.Combine(staging, CustomerPackageStore.CatalogueFileName), catalogueBytes);
        }

        private static CustomerPackageManifest CreateManifest(string customerCode, byte[] modelBytes, byte[] catalogueBytes, int retentionDays = 30)
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
                retentionDays = retentionDays
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
