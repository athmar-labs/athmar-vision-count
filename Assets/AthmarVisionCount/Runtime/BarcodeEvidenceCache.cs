using System;

namespace AthmarLabs.VisionCount
{
    public sealed class BarcodeEvidenceCache
    {
        public const double DefaultTtlSeconds = 1.5d;
        public const float DefaultMinimumOverlap = 0.50f;

        private readonly double _ttlSeconds;
        private readonly float _minimumOverlap;
        private string _barcode = string.Empty;
        private NormalizedRect _bounds;
        private double _expiresAt;

        public BarcodeEvidenceCache(double ttlSeconds = DefaultTtlSeconds, float minimumOverlap = DefaultMinimumOverlap)
        {
            if (ttlSeconds <= 0d || ttlSeconds > 10d)
                throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
            if (minimumOverlap < 0f || minimumOverlap > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumOverlap));
            _ttlSeconds = ttlSeconds;
            _minimumOverlap = minimumOverlap;
        }

        public void Record(string rawValue, NormalizedRect bounds, double nowSeconds)
        {
            Clear();
            var normalized = BulkProductCatalogue.NormalizeBarcode(rawValue);
            if (normalized.Length == 0)
                return;
            _barcode = normalized;
            _bounds = bounds;
            _expiresAt = nowSeconds + _ttlSeconds;
        }

        public bool TryGet(NormalizedRect currentBounds, double nowSeconds, out string barcode)
        {
            barcode = string.Empty;
            if (_barcode.Length == 0 || nowSeconds > _expiresAt)
            {
                Clear();
                return false;
            }
            if (_bounds.IntersectionOverUnion(currentBounds) < _minimumOverlap)
                return false;
            barcode = _barcode;
            return true;
        }

        public void Clear()
        {
            _barcode = string.Empty;
            _bounds = default;
            _expiresAt = 0d;
        }
    }
}
