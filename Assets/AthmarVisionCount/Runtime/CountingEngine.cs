using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Proposes, per SKU, the largest number of products visible in the camera frame at the same
    /// time ("what is in the picture now"). A count only becomes proposed after it is seen in two
    /// consecutive frames no more than the track lifetime apart, so a single spurious box cannot
    /// raise it. Moving the phone, or a product briefly dropping out of view, never adds to the
    /// count. Final inventory values must still be confirmed by a human operator.
    /// </summary>
    public sealed class CountingEngine
    {
        private readonly float _minimumConfidence;
        private readonly float _duplicateIouThreshold;
        private readonly double _trackTtlSeconds;
        private readonly Dictionary<string, int> _proposedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _manualCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _previousFrameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private double _previousFrameSeconds = double.NegativeInfinity;

        public CountingEngine(float minimumConfidence = 0.65f, float duplicateIouThreshold = 0.45f, double trackTtlSeconds = 1.25d)
        {
            if (minimumConfidence < 0f || minimumConfidence > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
            if (duplicateIouThreshold < 0f || duplicateIouThreshold > 1f)
                throw new ArgumentOutOfRangeException(nameof(duplicateIouThreshold));
            if (trackTtlSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(trackTtlSeconds));

            _minimumConfidence = minimumConfidence;
            _duplicateIouThreshold = duplicateIouThreshold;
            _trackTtlSeconds = trackTtlSeconds;
        }

        public IReadOnlyDictionary<string, int> ProcessFrame(IEnumerable<Detection> detections, double timestampSeconds)
        {
            if (detections == null)
                throw new ArgumentNullException(nameof(detections));
            if (timestampSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));

            var frameCounts = CountVisible(detections);
            var previousIsRecent = timestampSeconds - _previousFrameSeconds <= _trackTtlSeconds;
            if (previousIsRecent)
            {
                foreach (var pair in frameCounts)
                {
                    if (!_previousFrameCounts.TryGetValue(pair.Key, out var previous))
                        continue;

                    var stable = Math.Min(pair.Value, previous);
                    if (!_proposedCounts.TryGetValue(pair.Key, out var proposed) || stable > proposed)
                        _proposedCounts[pair.Key] = stable;
                }
            }

            _previousFrameCounts = frameCounts;
            _previousFrameSeconds = timestampSeconds;
            return GetEffectiveCounts();
        }

        private Dictionary<string, int> CountVisible(IEnumerable<Detection> detections)
        {
            // Keep one box per physical product: drop same-SKU boxes overlapping a stronger one.
            var accepted = new Dictionary<string, List<NormalizedRect>>(StringComparer.OrdinalIgnoreCase);
            foreach (var detection in detections
                         .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Sku) && value.Confidence >= _minimumConfidence)
                         .OrderByDescending(value => value.Confidence))
            {
                var sku = detection.Sku.Trim();
                if (!accepted.TryGetValue(sku, out var boxes))
                {
                    boxes = new List<NormalizedRect>();
                    accepted[sku] = boxes;
                }

                if (boxes.Any(box => box.IntersectionOverUnion(detection.Bounds) >= _duplicateIouThreshold))
                    continue;
                boxes.Add(detection.Bounds);
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in accepted)
                counts[pair.Key] = pair.Value.Count;
            return counts;
        }

        public void SetManualCount(string sku, int count)
        {
            if (string.IsNullOrWhiteSpace(sku))
                throw new ArgumentException("SKU is required.", nameof(sku));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            _manualCounts[sku.Trim()] = count;
        }

        public void ClearManualCount(string sku)
        {
            if (!string.IsNullOrWhiteSpace(sku))
                _manualCounts.Remove(sku.Trim());
        }

        public IReadOnlyDictionary<string, int> GetEffectiveCounts()
        {
            var snapshot = new Dictionary<string, int>(_proposedCounts, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _manualCounts)
                snapshot[pair.Key] = pair.Value;
            return snapshot;
        }

        public ScanSessionRecord BuildSession(string operatorReference, string locationReference, Func<string, string> displayNameResolver = null)
        {
            var session = ScanSessionRecord.CreateNew(operatorReference, locationReference);
            foreach (var sku in _proposedCounts.Keys.Union(_manualCounts.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var proposed = _proposedCounts.TryGetValue(sku, out var proposedValue) ? proposedValue : 0;
                var confirmed = _manualCounts.TryGetValue(sku, out var manualValue) ? manualValue : proposed;
                var displayName = displayNameResolver == null ? sku : displayNameResolver(sku);
                session.Lines.Add(new CountLine
                {
                    Sku = sku,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? sku : displayName.Trim(),
                    ProposedCount = proposed,
                    ConfirmedCount = confirmed,
                    ManuallyAdjusted = _manualCounts.ContainsKey(sku)
                });
            }
            return session;
        }

        public void Confirm(ScanSessionRecord session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (session.Lines == null || session.Lines.Count == 0)
                throw new InvalidOperationException("A scan session cannot be confirmed without count lines.");
            if (session.Lines.Any(line => line == null || string.IsNullOrWhiteSpace(line.Sku) || line.ConfirmedCount < 0))
                throw new InvalidOperationException("The session contains an invalid count line.");

            session.Confirmed = true;
            session.CompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        public void Reset()
        {
            _previousFrameCounts.Clear();
            _previousFrameSeconds = double.NegativeInfinity;
            _proposedCounts.Clear();
            _manualCounts.Clear();
        }
    }
}
