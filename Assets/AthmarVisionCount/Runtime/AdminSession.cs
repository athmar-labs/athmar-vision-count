using UnityEngine;
namespace AthmarLabs.VisionCount {
    public sealed class AdminSession {
        private double _startTime;
        private bool _active;
        public bool IsActive => _active && (Time.realtimeSinceStartupAsDouble - _startTime) < 300.0;
        public void Start(double realtime) { _startTime = realtime; _active = true; }
        public void End() { _active = false; }
    }
}
