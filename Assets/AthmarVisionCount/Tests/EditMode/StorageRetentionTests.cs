using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class StorageRetentionTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "athmar-vision-count-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void LocalRepositoryRejectsUnconfirmedSession()
        {
            var repository = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var session = BuildSession(confirmed: false, completedAtUtc: "");

            Assert.Throws<InvalidOperationException>(() => repository.Save(session));
        }

        [Test]
        public void RetentionDeletesOnlyExpiredSessionsForRequestedCustomer()
        {
            var repository = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            repository.Save(BuildSession(true, now.AddDays(-40).ToString("O"), "expired-a", "customer-a"));
            repository.Save(BuildSession(true, now.AddDays(-5).ToString("O"), "recent-a", "customer-a"));
            repository.Save(BuildSession(true, now.AddDays(-90).ToString("O"), "expired-b", "customer-b"));

            var deleted = repository.DeleteExpired("customer-a", 30, now);
            var remaining = repository.LoadAll();

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(remaining, Has.Count.EqualTo(2));
            Assert.That(remaining[0].SessionId == "recent-a" || remaining[1].SessionId == "recent-a", Is.True);
            Assert.That(remaining[0].SessionId == "expired-b" || remaining[1].SessionId == "expired-b", Is.True);
        }

        [Test]
        public void ExportRetentionDeletesOnlyRequestedCustomerFiles()
        {
            var exportRoot = Path.Combine(_root, "exports");
            var exports = new ExportFileService(exportRoot);
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            var pathA = exports.SaveConfirmedSession(BuildSession(true, now.AddDays(-31).ToString("O"), "export-a", "customer-a"));
            var pathB = exports.SaveConfirmedSession(BuildSession(true, now.AddDays(-60).ToString("O"), "export-b", "customer-b"));
            File.SetLastWriteTimeUtc(pathA, now.AddDays(-31));
            File.SetLastWriteTimeUtc(pathB, now.AddDays(-60));

            var deleted = exports.DeleteExpired("customer-a", 30, now);

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(File.Exists(pathA), Is.False);
            Assert.That(File.Exists(pathB), Is.True);
        }

        private static ScanSessionRecord BuildSession(
            bool confirmed,
            string completedAtUtc,
            string sessionId = "session",
            string customerCode = "customer-a")
        {
            return new ScanSessionRecord
            {
                SessionId = sessionId,
                CustomerCode = customerCode,
                StartedAtUtc = "2026-07-01T00:00:00.0000000Z",
                CompletedAtUtc = completedAtUtc,
                Confirmed = confirmed,
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
    }
}
