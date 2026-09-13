using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class BarcodeEvidenceTests
    {
        [Test]
        public void CacheNormalizesBarcodeAndRequiresSpatialOverlap()
        {
            var cache = new BarcodeEvidenceCache();
            var original = new NormalizedRect(0.2f, 0.2f, 0.4f, 0.4f);
            cache.Record("6281-000 001", original, 10d);

            Assert.That(cache.TryGet(new NormalizedRect(0.22f, 0.21f, 0.4f, 0.4f), 10.5d, out var value), Is.True);
            Assert.That(value, Is.EqualTo("6281000001"));
            Assert.That(cache.TryGet(new NormalizedRect(0.75f, 0.75f, 0.2f, 0.2f), 10.5d, out _), Is.False);
        }

        [Test]
        public void CacheExpiresAndEmptyEvidenceFailsClosed()
        {
            var cache = new BarcodeEvidenceCache(ttlSeconds: 1d);
            var bounds = new NormalizedRect(0.1f, 0.1f, 0.5f, 0.5f);
            cache.Record("123456", bounds, 5d);
            Assert.That(cache.TryGet(bounds, 6.1d, out _), Is.False);

            cache.Record(" -- ", bounds, 7d);
            Assert.That(cache.TryGet(bounds, 7.1d, out _), Is.False);
        }

        [Test]
        public void SidecarThrottlesScanAndKeepsEvidenceSpatiallyBound()
        {
            var scanner = new FakeBarcodeScanner("6281000001");
            using var sidecar = new BarcodeEvidenceSidecar(scanner, scanIntervalSeconds: 1d);
            var crop = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                var bounds = new NormalizedRect(0.2f, 0.2f, 0.4f, 0.4f);
                sidecar.ObserveSingleProductCrop(crop, bounds, 10d);
                Assert.That(scanner.BeginScanCount, Is.EqualTo(1));

                Assert.That(sidecar.TryGetBarcode(bounds, 10.1d, out var value), Is.True);
                Assert.That(value, Is.EqualTo("6281000001"));

                sidecar.ObserveSingleProductCrop(crop, bounds, 10.2d);
                Assert.That(scanner.BeginScanCount, Is.EqualTo(1), "Low-cadence sidecar must not scan every visual frame.");

                var differentProduct = new NormalizedRect(0.75f, 0.75f, 0.2f, 0.2f);
                Assert.That(sidecar.TryGetBarcode(differentProduct, 10.2d, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crop);
            }
        }

        private sealed class FakeBarcodeScanner : IBarcodeEvidenceScanner
        {
            private readonly string _value;
            private readonly Queue<BarcodeScanResult> _results = new Queue<BarcodeScanResult>();

            public FakeBarcodeScanner(string value)
            {
                _value = value;
            }

            public int BeginScanCount { get; private set; }
            public bool IsAvailable => true;
            public bool IsBusy => false;

            public void BeginScan(byte[] jpegBytes)
            {
                if (jpegBytes == null || jpegBytes.Length == 0)
                    throw new ArgumentException("JPEG evidence is required.", nameof(jpegBytes));
                BeginScanCount++;
                _results.Enqueue(new BarcodeScanResult(_value, string.Empty));
            }

            public bool TryTakeResult(out BarcodeScanResult result)
            {
                if (_results.Count == 0)
                {
                    result = null;
                    return false;
                }
                result = _results.Dequeue();
                return true;
            }

            public void Dispose()
            {
                _results.Clear();
            }
        }
    }
}
