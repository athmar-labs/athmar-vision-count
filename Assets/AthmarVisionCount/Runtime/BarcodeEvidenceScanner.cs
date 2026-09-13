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
            while (_results.TryDequeue(out _)) { }
        }

        private void Complete(BarcodeScanResult result)
        {
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
