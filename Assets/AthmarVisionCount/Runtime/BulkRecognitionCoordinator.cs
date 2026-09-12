using System;
using System.Collections.Generic;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class BulkRecognitionCoordinator : MonoBehaviour, IDisposable
    {
        private VisionInferenceRunner _inference;
        private BulkDetectionRecognizer _recognizer;
        private ProductEnrollmentStore _manualStore;
        private BulkProductCatalogue _bulkCatalogue;
        private string _customerCode = string.Empty;
        private bool _disposed;

        private void Awake()
        {
            _inference = GetComponent<VisionInferenceRunner>();
            if (_inference == null)
                throw new InvalidOperationException("Bulk recognition requires VisionInferenceRunner on the same object.");
        }

        public bool IsActive => _recognizer != null;
        public int BulkProductCount => _bulkCatalogue == null ? 0 : _bulkCatalogue.Count;

        public void Configure(string customerCode, SkuCatalogue detectorCatalogue)
        {
            ThrowIfDisposed();
            Disable();

            _customerCode = CustomerStorageScope.Require(customerCode);
            var store = new CustomerPackageStore();
            if (!store.TryLoadActive(out var snapshot, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.LogWarning("Bulk recognition package unavailable: " + error);
                return;
            }

            if (!string.Equals(snapshot.Configuration.CustomerCode, _customerCode, StringComparison.Ordinal))
                return;
            if (!snapshot.HasBulkRecognitionData)
                return;

            try
            {
                _bulkCatalogue = snapshot.BulkCatalogue;
                var resolver = new ProductEvidenceResolver(
                    snapshot.BulkCatalogue,
                    snapshot.BulkEmbeddingIndex,
                    snapshot.Manifest.bulkMinimumSimilarity,
                    snapshot.Manifest.bulkMinimumMargin,
                    snapshot.Manifest.bulkCandidateLimit);

                _manualStore = new ProductEnrollmentStore(_customerCode);
                var manualHardCases = SafeLoadManualHardCases();
                _recognizer = new BulkDetectionRecognizer(resolver, manualHardCases);
                _inference.SetDetectionPostProcessor(PostProcessDetections);

                detectorCatalogue?.SetFallbackDisplayNameResolver(ResolveBulkDisplayName);
                Debug.Log($"Bulk recognition activated for {_customerCode} with {_bulkCatalogue.Count} products.");
            }
            catch
            {
                Disable();
                throw;
            }
        }

        public void RefreshManualHardCases()
        {
            ThrowIfDisposed();
            if (_recognizer == null || _manualStore == null)
                return;
            _recognizer.SetManualHardCases(SafeLoadManualHardCases());
        }

        public void Disable()
        {
            if (_inference != null)
                _inference.SetDetectionPostProcessor(null);
            if (_recognizer != null)
                _recognizer.Dispose();
            _recognizer = null;
            _manualStore = null;
            _bulkCatalogue = null;
            _customerCode = string.Empty;
        }

        private IReadOnlyList<Detection> PostProcessDetections(IReadOnlyList<Detection> detections)
        {
            if (_recognizer == null || _inference == null)
                return detections ?? Array.Empty<Detection>();

            return _recognizer.Recognize(
                _inference.CameraTexture,
                detections,
                _inference.VideoRotationAngle,
                _inference.VideoVerticallyMirrored);
        }

        private ProductEnrollmentCatalogueData SafeLoadManualHardCases()
        {
            try
            {
                return _manualStore?.Load() ?? new ProductEnrollmentCatalogueData
                {
                    customerCode = _customerCode,
                    products = new List<ProductEnrollmentRecord>()
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Manual hard-case catalogue could not be loaded; bulk recognition continues without it: " + exception.Message);
                return new ProductEnrollmentCatalogueData
                {
                    customerCode = _customerCode,
                    products = new List<ProductEnrollmentRecord>()
                };
            }
        }

        private string ResolveBulkDisplayName(string sku, string language)
        {
            if (_bulkCatalogue != null && _bulkCatalogue.TryGetBySku(sku, out var product))
                return product.GetDisplayName(language);
            return sku;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Disable();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BulkRecognitionCoordinator));
        }
    }
}
