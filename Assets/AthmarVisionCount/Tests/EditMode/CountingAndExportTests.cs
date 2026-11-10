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
        public void ProcessFrame_ReappearingProductIsNotCountedAgain()
        {
            // Field bug: two bottles, phone moving, count climbed to 8.
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 0.5d);
            var left = new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));
            var right = new Detection("SKU-1", 0.9f, new NormalizedRect(0.6f, 0.1f, 0.2f, 0.2f));
            var moved = new Detection("SKU-1", 0.9f, new NormalizedRect(0.3f, 0.5f, 0.2f, 0.2f));

            engine.ProcessFrame(new[] { left, right }, 0d);
            engine.ProcessFrame(new[] { left, right }, 0.25d);
            engine.ProcessFrame(Array.Empty<Detection>(), 0.5d);
            engine.ProcessFrame(new[] { moved }, 2d);
            engine.ProcessFrame(new[] { moved }, 2.25d);
            var counts = engine.ProcessFrame(new[] { left, right }, 4d);

            Assert.That(counts["SKU-1"], Is.EqualTo(2));
        }

        [Test]
        public void ProcessFrame_SingleFrameSpikeDoesNotRaiseCount()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 1d);
            var a = new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));
            var b = new Detection("SKU-1", 0.9f, new NormalizedRect(0.6f, 0.1f, 0.2f, 0.2f));
            var spurious = new Detection("SKU-1", 0.9f, new NormalizedRect(0.4f, 0.6f, 0.2f, 0.2f));

            engine.ProcessFrame(new[] { a, b }, 0d);
            engine.ProcessFrame(new[] { a, b, spurious }, 0.25d);
            var counts = engine.ProcessFrame(new[] { a, b }, 0.5d);

            Assert.That(counts["SKU-1"], Is.EqualTo(2));
        }

        [Test]
        public void ProcessFrame_SameSkuOverlappingBoxesCountOnce()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 1d);
            var box = new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f));
            var overlap = new Detection("SKU-1", 0.8f, new NormalizedRect(0.11f, 0.11f, 0.2f, 0.2f));

            engine.ProcessFrame(new[] { box, overlap }, 0d);
            var counts = engine.ProcessFrame(new[] { box, overlap }, 0.25d);

            Assert.That(counts["SKU-1"], Is.EqualTo(1));
        }

        [Test]
        public void ProcessFrame_SlowPhoneStillConfirmsConsecutiveFrames()
        {
            // Field bug: with the heavier cafe model one inference took longer than the track
            // lifetime, so two bottles were boxed on screen but the count stayed empty.
            var engine = new CountingEngine(minimumConfidence: 0.5f, duplicateIouThreshold: 0.4f, trackTtlSeconds: 1.25d);
            var left = new Detection("SKU-1", 0.63f, new NormalizedRect(0.2f, 0.4f, 0.1f, 0.3f));
            var right = new Detection("SKU-1", 0.74f, new NormalizedRect(0.5f, 0.4f, 0.1f, 0.3f));

            engine.ProcessFrame(new[] { left, right }, 0d);
            var counts = engine.ProcessFrame(new[] { left, right }, 2.5d);

            Assert.That(counts["SKU-1"], Is.EqualTo(2));
        }

        [Test]
        public void ProcessFrame_FirstFrameAloneDoesNotCount()
        {
            var engine = new CountingEngine(minimumConfidence: 0.5f);
            var counts = engine.ProcessFrame(
                new[] { new Detection("SKU-1", 0.9f, new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f)) },
                0d);

            Assert.That(counts.ContainsKey("SKU-1"), Is.False);
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
            var seen = new[] { new Detection("SKU-1", 0.9f, new NormalizedRect(0f, 0f, 0.2f, 0.2f)) };
            engine.ProcessFrame(seen, 0d);
            engine.ProcessFrame(seen, 0.25d);
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

        [Test]
        public void CsvExport_NeutralizesEveryTextColumnAndUnsafePrefix()
        {
            var session = new ScanSessionRecord
            {
                SessionId = "=SESSION",
                StartedAtUtc = "+START",
                CompletedAtUtc = "-END",
                Confirmed = true,
                OperatorReference = "@OPERATOR",
                LocationReference = "  =LOCATION",
                Lines = new List<CountLine>
                {
                    new CountLine
                    {
                        Sku = "\0=SKU",
                        DisplayName = "\U000E0001=NAME,\"quoted\"",
                        ProposedCount = 1,
                        ConfirmedCount = 1
                    }
                }
            };

            var csv = CsvExportService.BuildConfirmedSessionCsv(session);
            var expected =
                "session_id,started_at_utc,completed_at_utc,operator_reference,location_reference,sku,display_name,proposed_count,confirmed_count,manually_adjusted" + Environment.NewLine +
                "'=SESSION,'+START,'-END,'@OPERATOR,'  =LOCATION,'\0=SKU,\"'\U000E0001=NAME,\"\"quoted\"\"\",1,1,false" + Environment.NewLine;

            Assert.That(csv, Is.EqualTo(expected));
        }

    }
}
