using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CountingAndExportTests
    {
        [Test]
        public void ProcessFrame_DeduplicatesOverlappingDetectionWithinTrackLifetime()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 1d);
            var first = new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));
            var second = new Detection("SKU-1", 0.92f, new NormalizedRect(0.11f, 0.11f, 0.2f, 0.2f));

            engine.ProcessFrame(new[] { first }, 0d);
            var counts = engine.ProcessFrame(new[] { second }, 0.25d);

            Assert.That(counts["SKU-1"], Is.EqualTo(1));
        }

        [Test]
        public void ProcessFrame_CountsAgainAfterTrackExpires()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 0.5d);
            var detection = new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));

            engine.ProcessFrame(new[] { detection }, 0d);
            var counts = engine.ProcessFrame(new[] { detection }, 1d);

            Assert.That(counts["SKU-1"], Is.EqualTo(2));
        }

        [Test]
        public void ProcessFrame_IgnoresLowConfidenceDetections()
        {
            var engine = new CountingEngine(minimumConfidence: 0.8f);
            var counts = engine.ProcessFrame(
                new[] { new Detection("SKU-1", 0.5f, new NormalizedRect(0f, 0f, 0.2f, 0.2f)) },
                0d);

            Assert.That(counts.ContainsKey("SKU-1"), Is.False);
        }

        [Test]
        public void ManualReview_OverridesProposedCountAndCanBeConfirmed()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f);
            engine.ProcessFrame(
                new[] { new Detection("SKU-1", 0.9f, new NormalizedRect(0f, 0f, 0.2f, 0.2f)) },
                0d);
            engine.SetManualCount("SKU-1", 3);

            var session = engine.BuildSession("operator-1", "aisle-a");
            engine.Confirm(session);

            Assert.That(session.Confirmed, Is.True);
            Assert.That(session.Lines[0].ProposedCount, Is.EqualTo(1));
            Assert.That(session.Lines[0].ConfirmedCount, Is.EqualTo(3));
            Assert.That(session.Lines[0].ManuallyAdjusted, Is.True);
            Assert.That(session.CompletedAtUtc, Is.Not.Empty);
        }

        [Test]
        public void CsvExport_RejectsUnconfirmedSession()
        {
            var session = ScanSessionRecord.CreateNew("operator", "location");
            session.Lines.Add(new CountLine { Sku = "SKU-1", DisplayName = "Item", ProposedCount = 1, ConfirmedCount = 1 });

            Assert.Throws<InvalidOperationException>(() => CsvExportService.BuildConfirmedSessionCsv(session));
        }

        [Test]
        public void CsvExport_EscapesCustomerTextAndUsesConfirmedCount()
        {
            var session = new ScanSessionRecord
            {
                SessionId = "session-1",
                StartedAtUtc = "2026-07-22T00:00:00Z",
                CompletedAtUtc = "2026-07-22T00:01:00Z",
                Confirmed = true,
                OperatorReference = "Operator, One",
                LocationReference = "Aisle \"A\"",
                Lines = new List<CountLine>
                {
                    new CountLine
                    {
                        Sku = "SKU-1",
                        DisplayName = "Bottle, 1L",
                        ProposedCount = 2,
                        ConfirmedCount = 3,
                        ManuallyAdjusted = true
                    }
                }
            };

            var csv = CsvExportService.BuildConfirmedSessionCsv(session);

            StringAssert.Contains("\"Operator, One\"", csv);
            StringAssert.Contains("\"Aisle \"\"A\"\"\"", csv);
            StringAssert.Contains(",2,3,true", csv);
        }
    }
}
