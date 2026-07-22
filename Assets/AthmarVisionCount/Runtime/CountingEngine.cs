using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Maintains a conservative proposed count. A detection is counted once while an overlapping
    /// track remains active. Final inventory values must still be confirmed by a human operator.
    /// </summary>
    public sealed class CountingEngine
    {
        private sealed class Track
        {
            public NormalizedRect Bounds;
            public double LastSeenSeconds;
        }

        private readonly float _minimumConfidence;
        private readonly float _duplicateIouThreshold;
        private readonly double _trackTtlSeconds;
        private readonly Dictionary<string, List<Track>> _tracks = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _proposedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _manualCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

            ExpireTracks(timestampSeconds);

            foreach (var detection in detections)
            {
                if (detection == null || string.IsNullOrWhiteSpace(detection.Sku) || detection.Confidence < _minimumConfidence)
                    continue;

                var sku = detection.Sku.Trim();
                if (!_tracks.TryGetValue(sku, out var skuTracks))
                {
                    skuTracks = new List<Track>();
                    _tracks[sku] = skuTracks;
                }

                Track matchingTrack = null;
                var bestIou = 0f;
                foreach (var track in skuTracks)
                {
                    var iou = track.Bounds.IntersectionOverUnion(detection.Bounds);
                    if (iou >= _duplicateIouThreshold && iou > bestIou)
                    {
                        bestIou = iou;
                        matchingTrack = track;
                    }
                }

                if (matchingTrack != null)
                {
                    matchingTrack.Bounds = detection.Bounds;
                    matchingTrack.LastSeenSeconds = timestampSeconds;
                    continue;
                }

                skuTracks.Add(new Track
                {
                    Bounds = detection.Bounds,
                    LastSeenSeconds = timestampSeconds
                });

                _proposedCounts[sku] = _proposedCounts.TryGetValue(sku, out var current) ? current + 1 : 1;
            }

            return GetEffectiveCounts();
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
            _tracks.Clear();
            _proposedCounts.Clear();
            _manualCounts.Clear();
        }

        private void ExpireTracks(double timestampSeconds)
        {
            foreach (var pair in _tracks)
                pair.Value.RemoveAll(track => timestampSeconds - track.LastSeenSeconds > _trackTtlSeconds);
        }
    }
}
