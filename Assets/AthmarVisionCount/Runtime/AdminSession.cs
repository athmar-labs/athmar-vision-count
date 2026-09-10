using System;

namespace AthmarLabs.VisionCount
{
    public sealed class AdminSession
    {
        public static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(5);

        private double _expiresAt;
        private bool _active;

        public void Start(double monotonicSeconds)
        {
            RequireValidTime(monotonicSeconds);
            _expiresAt = monotonicSeconds + MaximumDuration.TotalSeconds;
            _active = true;
        }

        public bool IsActive(double monotonicSeconds)
        {
            RequireValidTime(monotonicSeconds);
            if (!_active || monotonicSeconds >= _expiresAt)
            {
                End();
                return false;
            }

            return true;
        }

        public void End()
        {
            _active = false;
            _expiresAt = 0d;
        }

        private static void RequireValidTime(double monotonicSeconds)
        {
            if (double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds) || monotonicSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        }
    }
}
