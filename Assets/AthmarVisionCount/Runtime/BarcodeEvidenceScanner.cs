using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    public sealed class BarcodeScanResult
    {
        public BarcodeScanResult(string value, string error)
        {
            Value = value ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public string Value { get; }
        public string Error { get; }
        public bool Succeeded => string.IsNullOrWhiteSpace(Error);
    }

    public interface IBarcodeEvidenceScanner : IDisposable
    {
        bool IsAvailable { get; }
        bool IsBusy { get; }
        void BeginScan(byte[] jpegBytes);
        bool TryTakeResult(out BarcodeScanResult result);
    }

    public static class BarcodeEvidenceScannerFactory
    {
        public static IBarcodeEvidenceScanner Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidMlKitBarcodeEvidenceScanner();
#else
            return new UnavailableBarcodeEvidenceScanner();
#endif
        }
    }

    internal sealed class UnavailableBarcodeEvidenceScanner : IBarcodeEvidenceScanner
    {
        public bool IsAvailable => false;
        public bool IsBusy => false;
        public void BeginScan(byte[] jpegBytes) { }
        public bool TryTakeResult(out BarcodeScanResult result) { result = null; return false; }
        public void Dispose() { }
    }

    /// <summary>
    /// Optional low-cadence barcode sidecar. It consumes an already-created product crop only after
    /// visual embedding has run, never schedules work inside the Sentis worker, and fails open back
    /// to the unchanged visual path. Evidence is short-lived and spatially bound to the same box.
    /// </summary>
    public sealed class BarcodeEvidenceSidecar : IDisposable
    {
        public const double DefaultScanIntervalSeconds = 1.0d;
        public const int DefaultJpegQuality = 70;

        private readonly IBarcodeEvidenceScanner _scanner;
        private readonly BarcodeEvidenceCache _cache;
        private readonly double _scanIntervalSeconds;
        private double _nextScanAtSeconds;
        private NormalizedRect _pendingBounds;
        private bool _hasPendingBounds;
        private bool _disposed;

        public BarcodeEvidenceSidecar(
            IBarcodeEvidenceScanner scanner,
            double scanIntervalSeconds = DefaultScanIntervalSeconds)
        {
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            if (scanIntervalSeconds < 0.25d || scanIntervalSeconds > 10d)
                throw new ArgumentOutOfRangeException(nameof(scanIntervalSeconds));
            _scanIntervalSeconds = scanIntervalSeconds;
            _cache = new BarcodeEvidenceCache();
        }

        public bool IsAvailable => !_disposed && _scanner.IsAvailable;

        public bool TryGetBarcode(NormalizedRect bounds, double nowSeconds, out string barcode)
        {
            barcode = string.Empty;
            if (_disposed)
                return false;
            Pump(nowSeconds);
            return _cache.TryGet(bounds, nowSeconds, out barcode);
        }

        public void ObserveSingleProductCrop(Texture2D crop, NormalizedRect bounds, double nowSeconds)
        {
            if (_disposed || crop == null)
                return;

            Pump(nowSeconds);
            if (!_scanner.IsAvailable || _scanner.IsBusy || nowSeconds < _nextScanAtSeconds)
                return;

            try
            {
                var jpeg = ImageConversion.EncodeToJPG(crop, DefaultJpegQuality);
                if (jpeg == null || jpeg.Length == 0)
                    return;

                _pendingBounds = bounds;
                _hasPendingBounds = true;
                _nextScanAtSeconds = nowSeconds + _scanIntervalSeconds;
                _scanner.BeginScan(jpeg);
            }
            catch (Exception exception)
            {
                _hasPendingBounds = false;
                _nextScanAtSeconds = nowSeconds + _scanIntervalSeconds;
                Debug.LogWarning("Barcode sidecar skipped a scan; visual recognition continues unchanged: " + exception.Message);
            }
        }

        private void Pump(double nowSeconds)
        {
            while (_scanner.TryTakeResult(out var result))
            {
                if (!_hasPendingBounds)
                    continue;

                var bounds = _pendingBounds;
                _hasPendingBounds = false;
                _cache.Clear();

                if (result == null)
                    continue;
                if (!result.Succeeded)
                {
                    Debug.LogWarning("Barcode sidecar result ignored; visual recognition continues unchanged: " + result.Error);
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(result.Value))
                    _cache.Record(result.Value, bounds, nowSeconds);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cache.Clear();
            _scanner.Dispose();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidMlKitBarcodeEvidenceScanner : IBarcodeEvidenceScanner
    {
        private const string BridgeClassName = "com.athmarlabs.visioncount.barcode.MlKitBarcodeBridge";
        private const string CallbackInterfaceName = BridgeClassName + "$Callback";
        private readonly ConcurrentQueue<BarcodeScanResult> _results = new ConcurrentQueue<BarcodeScanResult>();
        private readonly AndroidJavaClass _bridge;
        private int _busy;
        private bool _disposed;

        public AndroidMlKitBarcodeEvidenceScanner()
        {
            try
            {
                _bridge = new AndroidJavaClass(BridgeClassName);
                IsAvailable = _bridge.CallStatic<bool>("isAvailable");
            }
            catch (Exception exception)
            {
                IsAvailable = false;
                _results.Enqueue(new BarcodeScanResult(string.Empty, exception.Message));
            }
        }

        public bool IsAvailable { get; }
        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        public void BeginScan(byte[] jpegBytes)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AndroidMlKitBarcodeEvidenceScanner));
            if (!IsAvailable || jpegBytes == null || jpegBytes.Length == 0)
                return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;
            try
            {
                _bridge.CallStatic("scanJpeg", jpegBytes, new CallbackProxy(this));
            }
            catch (Exception exception)
            {
                Complete(new BarcodeScanResult(string.Empty, exception.Message));
            }
        }

        public bool TryTakeResult(out BarcodeScanResult result) => _results.TryDequeue(out result);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bridge?.Dispose();
            Interlocked.Exchange(ref _busy, 0);
            while (_results.TryDequeue(out _)) { }
        }

        private void Complete(BarcodeScanResult result)
        {
            if (!_disposed)
                _results.Enqueue(result ?? new BarcodeScanResult(string.Empty, "Unknown barcode scanner result."));
            Interlocked.Exchange(ref _busy, 0);
        }

        private sealed class CallbackProxy : AndroidJavaProxy
        {
            private readonly AndroidMlKitBarcodeEvidenceScanner _owner;
            public CallbackProxy(AndroidMlKitBarcodeEvidenceScanner owner) : base(CallbackInterfaceName) { _owner = owner; }
            public void onSuccess(string value) { _owner.Complete(new BarcodeScanResult(value, string.Empty)); }
            public void onNoResult() { _owner.Complete(new BarcodeScanResult(string.Empty, string.Empty)); }
            public void onError(string message) { _owner.Complete(new BarcodeScanResult(string.Empty, message)); }
        }
    }
#endif
}
