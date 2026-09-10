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
        private ProductRecognitionMatcher _matcher;
        private bool _subscribed;

        private void Awake()
        {
            _adminView = GetComponent<CustomerAdminView>();
            _inference = GetComponent<VisionInferenceRunner>();
            _view = GetComponent<ProductEnrollmentView>();
            if (_view == null)
                _view = gameObject.AddComponent<ProductEnrollmentView>();

            _embeddingExtractor = new MvpVisualEmbeddingExtractor();
            _matcher = new ProductRecognitionMatcher();
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
            _view.Show(normalizedCustomerCode, catalogue.products.Count);
        }

        private void Update()
        {
            if (_view == null || !_view.IsVisible)
                return;

            if (_adminView == null || !_adminView.IsUnlocked)
            {
                _pendingReferences.Clear();
                _view.Hide();
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
                $"تم التقاط البصمة البصرية {_pendingReferences.Count}. " +
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
                    $"تم حفظ {saved.sku} محليًا لهذا العميل فقط. " +
                    "يمكنك الآن وضع المنتج أمام الكاميرا واختيار Test Recognition.");
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
                        : "الثقة أقل من حد MVP";
                    _view.SetStatus(
                        $"غير معروف / Unknown — {reason}. " +
                        $"best={result.Similarity:0.000}, runner-up={result.RunnerUpSimilarity:0.000}",
                        true);
                    return;
                }

                var displayName = ResolveDisplayName(catalogue, result.Sku);
                _view.SetStatus(
                    $"تم التعرف: {displayName} [{result.Sku}] — similarity={result.Similarity:0.000}");
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
            if (_adminView != null && _adminView.IsUnlocked && _store != null)
                return true;

            _pendingReferences.Clear();
            _view.SetStatus("انتهت جلسة المدير. افتح لوحة الإدارة مجددًا.", true);
            return false;
        }

        private bool TryCaptureEmbedding(out float[] embedding, out string error)
        {
            embedding = null;
            error = string.Empty;

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
                    error = "مولد البصمة البصرية أعاد بيانات غير صالحة.";
                    embedding = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "تعذر إنشاء البصمة البصرية: " + exception.Message;
                embedding = null;
                return false;
            }
            finally
            {
                if (frame != null)
                    Destroy(frame);
            }
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
