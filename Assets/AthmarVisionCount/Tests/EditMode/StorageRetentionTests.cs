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
        public void RetentionDeletesExpiredSessionButKeepsRecentSession()
        {
            var repository = new LocalScanRepository(Path.Combine(_root, "sessions"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            repository.Save(BuildSession(true, now.AddDays(-40).ToString("O"), "expired"));
            repository.Save(BuildSession(true, now.AddDays(-5).ToString("O"), "recent"));

            var deleted = repository.DeleteExpired(30, now);
            var remaining = repository.LoadAll();

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].SessionId, Is.EqualTo("recent"));
        }

        [Test]
        public void ExportRetentionDeletesOldCsvFiles()
        {
            var exportRoot = Path.Combine(_root, "exports");
            var exports = new ExportFileService(exportRoot);
            var path = exports.SaveConfirmedSession(BuildSession(true, DateTime.UtcNow.ToString("O"), "export-old"));
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, now.AddDays(-31));

            var deleted = exports.DeleteExpired(30, now);

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(File.Exists(path), Is.False);
        }

        private static ScanSessionRecord BuildSession(bool confirmed, string completedAtUtc, string sessionId = "session")
        {
            return new ScanSessionRecord
            {
                SessionId = sessionId,
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
