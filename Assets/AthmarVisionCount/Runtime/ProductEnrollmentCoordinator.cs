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
        private RobustProductRecognitionMatcher _matcher;
        private bool _catalogueCompatible = true;
        private bool _subscribed;

        private void Awake()
        {
            _adminView = GetComponent<CustomerAdminView>();
            _inference = GetComponent<VisionInferenceRunner>();
            _view = GetComponent<ProductEnrollmentView>();
            if (_view == null)
                _view = gameObject.AddComponent<ProductEnrollmentView>();

            // Do not construct a fallback descriptor here. The neural model is loaded lazily and
            // enrollment fails closed if the shared Sentis resource is missing or invalid.
            _matcher = new RobustProductRecognitionMatcher();
            Subscribe();
        }

        public void Open(string customerCode)
        {
            if (_adminView == null || !_adminView.IsUnlocked)
                throw new InvalidOperationException("Administrator access must be unlocked before product enrollment.");

            var normalizedCustomerCode = CustomerStorageScope.Require(customerCode);
            _store = new ProductEnrollmentStore(normalizedCustomerCode);
            _pendingReferences.Clear();
            var catalogue = _store.Load();
            _catalogueCompatible = CatalogueUsesCurrentEmbeddingDimension(catalogue);
            _view.Show(normalizedCustomerCode, catalogue.products.Count);

            if (!_catalogueCompatible)
            {
                _view.SetStatus(
                    "بيانات تسجيل المنتجات الحالية أُنشئت ببصمة بصرية قديمة وغير متوافقة. " +
                    "استخدم Delete Local Data ثم أعد تسجيل المنتجات بنموذج Sentis الجديد. / " +
                    "Existing enrollment data uses an incompatible embedding version; delete local data and re-enroll.",
                    true);
                return;
            }

            if (!EnsureEmbeddingExtractor(out var error))
                _view.SetStatus(error, true);
            else
                _view.SetStatus(
                    "نموذج AI العام جاهز. ضع منتجًا واحدًا داخل الإطار والتقط 8–15 مرجعًا. / " +
                    "Generic Sentis AI embedding ready; capture 8–15 views of one product.");
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
                    $"تم الوصول إلى الحد الأقصى ({ProductEnrollmentStore.MaximumReferenceCount}) من الصور المرجعية.",
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
                    $"تم حفظ {saved.sku} محليًا لهذا العميل باستخدام بصمة Sentis AI. " +
                    "ضع المنتج أمام الكاميرا واختر Test Recognition.");
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
                var catalogue = _store.Load();
                if (catalogue.products.Count == 0)
                {
                    _view.SetStatus("لا توجد منتجات مسجلة للاختبار / No enrolled products.", true);
                    return;
                }

                if (!TryCaptureEmbedding(out var query, out var error))
                {
                    _view.SetStatus(error, true);
                    return;
                }

                var result = _matcher.Match(query, catalogue);
                if (!result.IsMatch)
                {
                    var reason = result.IsAmbiguous
                        ? "النتيجة متقاربة بين أكثر من منتج"
                        : "لم تتفق عدة صور مرجعية على المنتج أو كان التشابه أقل من الحد المطلوب";
                    _view.SetStatus(
                        $"غير معروف / Unknown — {reason}. " +
                        $"consensus={result.Similarity:0.000}, runner-up={result.RunnerUpSimilarity:0.000}",
                        true);
                    return;
                }

                var displayName = ResolveDisplayName(catalogue, result.Sku);
                _view.SetStatus(
                    $"تم التعرف: {displayName} [{result.Sku}] — consensus={result.Similarity:0.000}");
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

        private void ClearDraft()
        {
            _pendingReferences.Clear();
            _view.ResetDraft();
            _view.SetStatus("تم مسح بيانات المنتج والبصمات المؤقتة / Draft cleared");
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
                    "بيانات التسجيل القديمة غير متوافقة مع نموذج Sentis الحالي. احذف البيانات المحلية وأعد التسجيل.",
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
                error = "تعذر تحميل نموذج AI العام للتسجيل / Generic Sentis embedding unavailable: " + exception.Message;
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

        private static string ResolveDisplayName(ProductEnrollmentCatalogueData catalogue, string sku)
        {
            for (var index = 0; index < catalogue.products.Count; index++)
            {
                var product = catalogue.products[index];
                if (product == null || !string.Equals(product.sku, sku, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(product.nameArabic))
                    return product.nameArabic;
                if (!string.IsNullOrWhiteSpace(product.nameEnglish))
                    return product.nameEnglish;
                return product.sku;
            }
            return sku;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_embeddingExtractor is IDisposable disposable)
                disposable.Dispose();
            _embeddingExtractor = null;
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
