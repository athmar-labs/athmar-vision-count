using System;
using System.Collections.Generic;
using System.Globalization;

namespace AthmarLabs.VisionCount
{
    [Serializable]
    public struct NormalizedRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public NormalizedRect(float x, float y, float width, float height)
        {
            X = Clamp01(x);
            Y = Clamp01(y);
            Width = Clamp01(width);
            Height = Clamp01(height);
        }

        public float IntersectionOverUnion(NormalizedRect other)
        {
            var left = Math.Max(X, other.X);
            var top = Math.Max(Y, other.Y);
            var right = Math.Min(X + Width, other.X + other.Width);
            var bottom = Math.Min(Y + Height, other.Y + other.Height);
            var intersectionWidth = Math.Max(0f, right - left);
            var intersectionHeight = Math.Max(0f, bottom - top);
            var intersection = intersectionWidth * intersectionHeight;
            var union = (Width * Height) + (other.Width * other.Height) - intersection;
            return union <= 0f ? 0f : intersection / union;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    [Serializable]
    public sealed class Detection
    {
        public string Sku;
        public float Confidence;
        public NormalizedRect Bounds;

        public Detection(string sku, float confidence, NormalizedRect bounds)
        {
            Sku = sku == null ? string.Empty : sku.Trim();
            Confidence = confidence;
            Bounds = bounds;
        }
    }

    [Serializable]
    public sealed class CountLine
    {
        public string Sku;
        public string DisplayName;
        public int ProposedCount;
        public int ConfirmedCount;
        public bool ManuallyAdjusted;
    }

    [Serializable]
    public sealed class ScanSessionRecord
    {
        public string SessionId;
        public string CustomerCode;
        public string StartedAtUtc;
        public string CompletedAtUtc;
        public bool Confirmed;
        public string OperatorReference;
        public string LocationReference;
        public List<CountLine> Lines = new List<CountLine>();

        public static ScanSessionRecord CreateNew(string operatorReference, string locationReference)
        {
            return new ScanSessionRecord
            {
                SessionId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                OperatorReference = operatorReference == null ? string.Empty : operatorReference.Trim(),
                LocationReference = locationReference == null ? string.Empty : locationReference.Trim()
            };
        }
    }
}
