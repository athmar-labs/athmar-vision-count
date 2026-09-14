using System;
using System.Collections.Generic;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class ProductEnrollmentCoordinator : MonoBehaviour
    {
        private readonly List<float[]> _pendingReferences = new List<float[]>();
        private CustomerAdminView _adminView;
        private ProductEnrollmentView _view;
        private VisionInferenceRunner _inference;
        private ProductEnrollmentStore _store;
        private IProductEmbeddingExtractor _embeddingExtractor;
        private RobustProductRecognitionMatcher _manualMatcher;
        private ProductEvidenceResolver _bulkResolver;
        private BulkProductCatalogue _bulkCatalogue;
        private bool _catalogueCompatible = true;
        private bool _subscribed;

        private void Awake()
        {
            _adminView = GetComponent<CustomerAdminView>();
            _inference = GetComponent<VisionInferenceRunner>();
            _view = GetComponent<ProductEnrollmentView>();
            if (_view == null)
                _view = gameObject.AddComponent<ProductEnrollmentView>();

            // No fallback descriptor. The shared neural embedding model is the only visual feature space.
            _manualMatcher = new RobustProductRecognitionMatcher();
            Subscribe();
        }

        public void Open(string customerCode)
        {
            if (_adminView == null || !_adminView.IsUnlocked)
                throw new InvalidOperationException("Administrator access must be unlocked before product enrollment.");

            var normalizedCustomerCode = CustomerStorageScope.Require(customerCode);
            _store = new ProductEnrollmentStore(normalizedCustomerCode);
            _pendingReferences.Clear();
            var manualCatalogue = _store.Load();
            _catalogueCompatible = CatalogueUsesCurrentEmbeddingDimension(manualCatalogue);

            LoadBulkRecognition(normalizedCustomerCode, out var bulkWarning);
            var bulkCount = _bulkCatalogue == null ? 0 : _bulkCatalogue.Count;
            _view.Show(normalizedCustomerCode, manualCatalogue.products.Count, bulkCount);

            if (!_catalogueCompatible)
            {
                _view.SetStatus(
                    "بيانات التصحيح اليدوي الحالية أُنشئت ببصمة قديمة وغير متوافقة. " +
                    "احذف بيانات التصحيح المحلية ثم أعد فقط المنتجات الصعبة. / " +
                    "Existing manual hard-case data uses an incompatible embedding version; clear it and re-enroll only hard products.",
                    true);
                return;
            }

            if (!EnsureEmbeddingExtractor(out var error))
            {
                _view.SetStatus(error, true);
                return;
            }

            if (bulkCount > 0)
            {
                _view.SetStatus(
                    $"كتالوج Bulk جاهز: {bulkCount} منتج. لا تُصوّر الكتالوج كاملًا هنا؛ " +
                    "استخدم هذه الشاشة فقط لإضافة منتج جديد أو تصحيح منتج صعب. / " +
                    $"Bulk catalogue ready: {bulkCount} products. Manual capture is only for new or hard products.");
            }
            else if (!string.IsNullOrWhiteSpace(bulkWarning))
            {
                _view.SetStatus(
                    "حزمة Bulk غير متاحة؛ التصحيح اليدوي ما زال متاحًا. / Bulk data unavailable: " + bulkWarning,
                    true);
            }
            else
            {
                _view.SetStatus(
                    "لا توجد حزمة Bulk v2 بعد. هذه الشاشة مخصصة لإضافة/تصحيح الحالات الصعبة فقط. / " +
                    "No bulk v2 package is active yet; this screen is for manual hard cases only.");
            }
        }

        private void Update()
        {
            if (_view == null || !_view.IsVisible)
                return;

            if (_adminView == null || !_adminView.IsUnlocked)
            {
                _pendingReferences.Clear();
                _view.Hide();
                return;
            }

            if (_inference != null)
            {
                _view.SetCamera(
                    _inference.CameraTexture,
                    _inference.VideoRotationAngle,
                    _inference.VideoVerticallyMirrored);
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _view == null)
                return;

            _view.CaptureReferenceRequested += CaptureReference;
            _view.SaveRequested += SaveProduct;
            _view.TestRecognitionRequested += TestRecognition;
            _view.ClearDraftRequested += ClearDraft;
            _view.CloseRequested += Close;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _view == null)
                return;

            _view.CaptureReferenceRequested -= CaptureReference;
            _view.SaveRequested -= SaveProduct;
            _view.TestRecognitionRequested -= TestRecognition;
            _view.ClearDraftRequested -= ClearDraft;
            _view.CloseRequested -= Close;
            _subscribed = false;
        }

        private void CaptureReference()
        {
            if (!EnsureSession())
                return;
            if (_pendingReferences.Count >= ProductEnrollmentStore.MaximumReferenceCount)
            {
                _view.SetStatus(
                    $"تم الوصول إلى الحد الأقصى ({ProductEnrollmentStore.MaximumReferenceCount}) من المراجع.",
                    true);
                return;
            }

            if (!TryCaptureEmbedding(out var embedding, out var error))
            {
                _view.SetStatus(error, true);
                return;
            }

            _pendingReferences.Add(embedding);
            _view.SetReferenceCount(_pendingReferences.Count);
            _view.SetStatus(
                $"تم التقاط بصمة AI رقم {_pendingReferences.Count}. " +
                "غيّر زاوية المنتج أو المسافة قليلًا ثم التقط التالية.");
        }

        private void SaveProduct(ProductEnrollmentDraft draft)
        {
            if (!EnsureSession())
                return;

            try
            {
                _view.SetBusy(true);
                var saved = _store.UpsertProduct(draft, _pendingReferences);
                _pendingReferences.Clear();
                var count = _store.Load().products.Count;
                _view.ResetDraft();
                _view.SetProductCount(count);
                _view.SetStatus(
                    $"تم حفظ {saved.sku} كتصحيح يدوي محلي لهذا العميل. " +
                    "هذا الـoverlay سيُستخدم عندما لا يحسم Bulk/Barcode/OCR المنتج.");
            }
            catch (Exception exception)
            {
                _view.SetStatus(exception.Message, true);
            }
            finally
            {
                _view.SetBusy(false);
            }
        }

        private void TestRecognition()
        {
            if (!EnsureSession())
                return;

            try
            {
                _view.SetBusy(true);
                var manualCatalogue = _store.Load();
                if (manualCatalogue.products.Count == 0 && _bulkResolver == null)
                {
                    _view.SetStatus("لا توجد بيانات Bulk أو تصحيحات يدوية للاختبار / No recognition data available.", true);
                    return;
                }

                if (!TryCaptureEmbedding(out var query, out var error))
                {
                    _view.SetStatus(error, true);
                    return;
                }

                if (_bulkResolver != null)
                {
                    var resolution = _bulkResolver.Resolve(
                        new ProductRecognitionEvidence { VisualEmbedding = query },
                        manualCatalogue);
                    if (!resolution.IsMatch)
                    {
                        var reason = resolution.IsAmbiguous
                            ? "تعارض أو تقارب بين أكثر من مرشح"
                            : "لم يصل الدليل إلى حد القبول";
                        _view.SetStatus(
                            $"غير معروف / Unknown — {reason}. " +
                            $"score={resolution.Score:0.000}, runner-up={resolution.RunnerUpScore:0.000}",
                            true);
                        return;
                    }

                    var displayName = ResolveDisplayName(manualCatalogue, resolution.Sku);
                    _view.SetStatus(
                        $"تم التعرف: {displayName} [{resolution.Sku}] — " +
                        $"source={resolution.Source}, score={resolution.Score:0.000}");
                    return;
                }

                var manual = _manualMatcher.Match(query, manualCatalogue);
                if (!manual.IsMatch)
                {
                    var reason = manual.IsAmbiguous
                        ? "النتيجة متقاربة بين أكثر من منتج يدوي"
                        : "لم تتفق عدة مراجع يدوية أو كان التشابه أقل من الحد";
                    _view.SetStatus(
                        $"غير معروف / Unknown — {reason}. " +
                        $"consensus={manual.Similarity:0.000}, runner-up={manual.RunnerUpSimilarity:0.000}",
                        true);
                    return;
                }

                _view.SetStatus(
                    $"تم التعرف: {ResolveDisplayName(manualCatalogue, manual.Sku)} [{manual.Sku}] — " +
                    $"source=ManualHardCase, consensus={manual.Similarity:0.000}");
            }
            catch (Exception exception)
            {
                _view.SetStatus(exception.Message, true);
            }
            finally
            {
                _view.SetBusy(false);
            }
        }

        private void LoadBulkRecognition(string customerCode, out string warning)
        {
            warning = string.Empty;
            _bulkResolver = null;
            _bulkCatalogue = null;

            try
            {
                var packageStore = new CustomerPackageStore();
                if (!packageStore.TryLoadActive(out var snapshot, out var error))
                {
                    warning = error;
                    return;
                }
                if (!string.Equals(snapshot.Configuration.CustomerCode, customerCode, StringComparison.Ordinal))
                {
                    warning = "Active package belongs to another customer.";
                    return;
                }
                if (!snapshot.HasBulkRecognitionData)
                    return;

                _bulkCatalogue = snapshot.BulkCatalogue;
                _bulkResolver = new ProductEvidenceResolver(
                    snapshot.BulkCatalogue,
                    snapshot.BulkEmbeddingIndex,
                    snapshot.Manifest.bulkMinimumSimilarity,
                    snapshot.Manifest.bulkMinimumMargin,
                    snapshot.Manifest.bulkCandidateLimit);
            }
            catch (Exception exception)
            {
                warning = exception.Message;
                _bulkResolver = null;
                _bulkCatalogue = null;
            }
        }

        private void ClearDraft()
        {
            _pendingReferences.Clear();
            _view.ResetDraft();
            _view.SetStatus("تم مسح بيانات التصحيح المؤقتة / Hard-case draft cleared");
        }

        private void Close()
        {
            _pendingReferences.Clear();
            _view.Hide();
        }

        private bool EnsureSession()
        {
            if (_adminView == null || !_adminView.IsUnlocked || _store == null)
            {
                _pendingReferences.Clear();
                _view.SetStatus("انتهت جلسة المدير. افتح لوحة الإدارة مجددًا.", true);
                return false;
            }

            if (!_catalogueCompatible)
            {
                _view.SetStatus(
                    "بيانات التصحيح اليدوي القديمة غير متوافقة مع نموذج Sentis الحالي. احذفها وأعد الحالات الصعبة فقط.",
                    true);
                return false;
            }

            if (!EnsureEmbeddingExtractor(out var error))
            {
                _view.SetStatus(error, true);
                return false;
            }

            return true;
        }

        private bool EnsureEmbeddingExtractor(out string error)
        {
            error = string.Empty;
            if (_embeddingExtractor != null)
                return true;

            try
            {
                _embeddingExtractor = new SentisProductEmbeddingExtractor();
                return true;
            }
            catch (Exception exception)
            {
                error = "تعذر تحميل نموذج AI العام / Generic Sentis embedding unavailable: " + exception.Message;
                return false;
            }
        }

        private bool TryCaptureEmbedding(out float[] embedding, out string error)
        {
            embedding = null;
            error = string.Empty;

            if (!EnsureEmbeddingExtractor(out error))
                return false;

            if (_inference == null || _inference.CameraTexture == null)
            {
                error = "الكاميرا غير جاهزة. ارجع لشاشة العد وتأكد من تشغيل الكاميرا.";
                return false;
            }

            if (!CameraFrameCapture.TryCapture(_inference.CameraTexture, out var frame, out error))
                return false;

            try
            {
                embedding = _embeddingExtractor.Extract(frame);
                if (embedding == null || embedding.Length != _embeddingExtractor.Dimension)
                {
                    error = "نموذج AI أعاد بصمة غير صالحة.";
                    embedding = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "تعذر إنشاء بصمة AI: " + exception.Message;
                embedding = null;
                return false;
            }
            finally
            {
                if (frame != null)
                    Destroy(frame);
            }
        }

        private static bool CatalogueUsesCurrentEmbeddingDimension(ProductEnrollmentCatalogueData catalogue)
        {
            if (catalogue?.products == null)
                return true;

            for (var productIndex = 0; productIndex < catalogue.products.Count; productIndex++)
            {
                var product = catalogue.products[productIndex];
                if (product?.references == null)
                    continue;
                for (var referenceIndex = 0; referenceIndex < product.references.Count; referenceIndex++)
                {
                    var values = product.references[referenceIndex]?.values;
                    if (values != null && values.Length != SentisProductEmbeddingExtractor.FeatureDimension)
                        return false;
                }
            }
            return true;
        }

        private string ResolveDisplayName(ProductEnrollmentCatalogueData manualCatalogue, string sku)
        {
            if (_bulkCatalogue != null && _bulkCatalogue.TryGetBySku(sku, out var bulkProduct))
                return bulkProduct.GetDisplayName("ar");

            if (manualCatalogue?.products != null)
            {
                for (var index = 0; index < manualCatalogue.products.Count; index++)
                {
                    var product = manualCatalogue.products[index];
                    if (product == null || !string.Equals(product.sku, sku, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(product.nameArabic))
                        return product.nameArabic;
                    if (!string.IsNullOrWhiteSpace(product.nameEnglish))
                        return product.nameEnglish;
                    return product.sku;
                }
            }
            return sku;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_embeddingExtractor is IDisposable disposable)
                disposable.Dispose();
            _embeddingExtractor = null;
            _bulkResolver = null;
            _bulkCatalogue = null;
        }
    }

    public static class CameraFrameCapture
    {
        public static bool TryCapture(Texture source, out Texture2D frame, out string error)
        {
            frame = null;
            error = string.Empty;

            if (!(source is WebCamTexture camera))
            {
                error = "مصدر الكاميرا الحالي غير مدعوم لالتقاط صور التسجيل.";
                return false;
            }

            if (!camera.isPlaying || camera.width <= 16 || camera.height <= 16)
            {
                error = "الكاميرا لم توفر إطارًا صالحًا بعد.";
                return false;
            }

            try
            {
                var pixels = camera.GetPixels32();
                if (pixels == null || pixels.Length != camera.width * camera.height)
                {
                    error = "تعذر قراءة إطار الكاميرا.";
                    return false;
                }

                frame = new Texture2D(camera.width, camera.height, TextureFormat.RGBA32, false);
                frame.SetPixels32(pixels);
                frame.Apply(false, false);
                return true;
            }
            catch (Exception exception)
            {
                if (frame != null)
                {
                    UnityEngine.Object.Destroy(frame);
                    frame = null;
                }
                error = "تعذر التقاط إطار الكاميرا: " + exception.Message;
                return false;
            }
        }
    }
}
