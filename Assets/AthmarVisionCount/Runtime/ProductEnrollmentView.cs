using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class ProductEnrollmentView : MonoBehaviour
    {
        public event Action CaptureReferenceRequested;
        public event Action TestRecognitionRequested;
        public event Action<ProductEnrollmentDraft> SaveRequested;
        public event Action ClearDraftRequested;
        public event Action CloseRequested;

        private Font _font;
        private GameObject _panel;
        private RawImage _cameraImage;
        private AspectRatioFitter _cameraAspect;
        private Text _customerText;
        private Text _statusText;
        private Text _referenceText;
        private Text _productCountText;
        private InputField _skuInput;
        private InputField _nameEnglishInput;
        private InputField _nameArabicInput;
        private InputField _barcodeInput;
        private Button _captureButton;
        private Button _saveButton;
        private Button _testButton;
        private Button _clearButton;
        private Button _closeButton;
        private int _referenceCount;
        private int _productCount;
        private int _bulkProductCount;
        private bool _busy;

        public bool IsVisible => _panel != null && _panel.activeSelf;

        private void Awake()
        {
            EnsureInterfaceBuilt();
        }

        public void Show(string customerCode, int productCount, int bulkProductCount = 0)
        {
            EnsureInterfaceBuilt();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _customerText.text = "العميل / Customer: " + (customerCode ?? string.Empty);
            _productCount = Math.Max(0, productCount);
            _bulkProductCount = Math.Max(0, bulkProductCount);
            ResetDraft();
            UpdateProgress();
            SetStatus(
                "هذه الشاشة ليست لإدخال الكتالوج كاملًا. استخدمها فقط لمنتج جديد أو حالة صعبة بعد Bulk Bootstrap. " +
                "ضع منتجًا واحدًا داخل الإطار والتقط 8–15 مرجعًا. الصور الخام لا تُحفظ.");
            SetBusy(false);
        }

        public void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }

        public void SetCamera(Texture texture, int rotationAngle, bool verticallyMirrored)
        {
            EnsureInterfaceBuilt();
            if (_cameraImage.texture != texture)
                _cameraImage.texture = texture;
            if (texture == null)
                return;

            var rotated = Math.Abs(rotationAngle) == 90 || Math.Abs(rotationAngle) == 270;
            _cameraAspect.aspectRatio = rotated
                ? texture.height / (float)Math.Max(1, texture.width)
                : texture.width / (float)Math.Max(1, texture.height);
            _cameraImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rotationAngle);
            _cameraImage.uvRect = verticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
        }

        public void SetReferenceCount(int referenceCount)
        {
            _referenceCount = Mathf.Clamp(referenceCount, 0, ProductEnrollmentStore.MaximumReferenceCount);
            UpdateProgress();
        }

        public void SetProductCount(int productCount)
        {
            _productCount = Math.Max(0, productCount);
            UpdateProgress();
        }

        public void SetBulkProductCount(int bulkProductCount)
        {
            _bulkProductCount = Math.Max(0, bulkProductCount);
            UpdateProgress();
        }

        public void ResetDraft()
        {
            EnsureInterfaceBuilt();
            _skuInput.text = string.Empty;
            _nameEnglishInput.text = string.Empty;
            _nameArabicInput.text = string.Empty;
            _barcodeInput.text = string.Empty;
            _referenceCount = 0;
            UpdateProgress();
        }

        public void SetStatus(string message, bool isError = false)
        {
            EnsureInterfaceBuilt();
            _statusText.color = isError ? new Color(1f, 0.45f, 0.45f) : Color.white;
            _statusText.text = message ?? string.Empty;
        }

        public void SetBusy(bool busy)
        {
            EnsureInterfaceBuilt();
            _busy = busy;
            UpdateButtonState();
        }

        private void UpdateProgress()
        {
            if (_referenceText == null)
                return;

            _referenceText.text =
                $"المراجع اليدوية / References: {_referenceCount} " +
                $"(min {ProductEnrollmentStore.MinimumReferenceCount}, max {ProductEnrollmentStore.MaximumReferenceCount})";
            _productCountText.text =
                $"Bulk catalogue: {_bulkProductCount}  |  Manual hard cases: {_productCount}";
            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            if (_captureButton == null)
                return;

            _captureButton.interactable = !_busy && _referenceCount < ProductEnrollmentStore.MaximumReferenceCount;
            _saveButton.interactable =
                !_busy &&
                _referenceCount >= ProductEnrollmentStore.MinimumReferenceCount &&
                _referenceCount <= ProductEnrollmentStore.MaximumReferenceCount;
            _testButton.interactable = !_busy && (_productCount > 0 || _bulkProductCount > 0);
            _clearButton.interactable = !_busy;
            _closeButton.interactable = !_busy;
        }

        private void HandleSave()
        {
            SaveRequested?.Invoke(new ProductEnrollmentDraft(
                _skuInput.text,
                _nameEnglishInput.text,
                _nameArabicInput.text,
                _barcodeInput.text));
        }

        private void EnsureInterfaceBuilt()
        {
            if (_panel != null)
                return;
            BuildInterface();
        }

        private void BuildInterface()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var panelRect = CreateRect("ProductEnrollmentPanel", transform);
            Stretch(panelRect);
            var background = panelRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.025f, 0.04f, 0.065f, 0.995f);
            _panel = panelRect.gameObject;

            var title = CreateText("Title", panelRect, 34, FontStyle.Bold, TextAnchor.MiddleCenter);
            title.text = "إضافة/تصحيح منتج صعب / Manual Hard-Case Product";
            Place(title.rectTransform, 0.04f, 0.945f, 0.96f, 0.99f);

            _customerText = CreateText("Customer", panelRect, 21, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_customerText.rectTransform, 0.04f, 0.905f, 0.96f, 0.945f);

            _productCountText = CreateText("ProductCount", panelRect, 21, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(_productCountText.rectTransform, 0.04f, 0.865f, 0.96f, 0.905f);

            var cameraRect = CreateRect("EnrollmentCamera", panelRect);
            Place(cameraRect, 0.055f, 0.545f, 0.945f, 0.855f);
            var cameraBackground = cameraRect.gameObject.AddComponent<Image>();
            cameraBackground.color = Color.black;

            var cameraImageRect = CreateRect("CameraImage", cameraRect);
            Stretch(cameraImageRect);
            _cameraImage = cameraImageRect.gameObject.AddComponent<RawImage>();
            _cameraImage.color = Color.white;
            _cameraImage.raycastTarget = false;
            _cameraAspect = cameraImageRect.gameObject.AddComponent<AspectRatioFitter>();
            _cameraAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _cameraAspect.aspectRatio = 16f / 9f;

            var cameraHint = CreateText("CameraHint", cameraRect, 20, FontStyle.Bold, TextAnchor.LowerCenter);
            Stretch(cameraHint.rectTransform);
            cameraHint.rectTransform.offsetMin = new Vector2(14f, 10f);
            cameraHint.rectTransform.offsetMax = new Vector2(-14f, -10f);
            cameraHint.text = "منتج جديد/صعب واحد داخل الإطار / One new or hard product";
            cameraHint.raycastTarget = false;

            var formRect = CreateRect("ProductForm", panelRect);
            Place(formRect, 0.055f, 0.325f, 0.945f, 0.535f);
            var formBackground = formRect.gameObject.AddComponent<Image>();
            formBackground.color = new Color(0.07f, 0.10f, 0.14f, 1f);

            _skuInput = CreateInputField("Sku", formRect, "SKU / رمز الصنف");
            Place(_skuInput.GetComponent<RectTransform>(), 0.04f, 0.77f, 0.96f, 0.96f);
            _skuInput.characterLimit = 64;

            _nameArabicInput = CreateInputField("NameArabic", formRect, "اسم المنتج بالعربية");
            Place(_nameArabicInput.GetComponent<RectTransform>(), 0.04f, 0.53f, 0.96f, 0.72f);
            _nameArabicInput.characterLimit = 160;

            _nameEnglishInput = CreateInputField("NameEnglish", formRect, "Product name in English");
            Place(_nameEnglishInput.GetComponent<RectTransform>(), 0.04f, 0.29f, 0.96f, 0.48f);
            _nameEnglishInput.characterLimit = 160;

            _barcodeInput = CreateInputField("Barcode", formRect, "Barcode اختياري / optional");
            Place(_barcodeInput.GetComponent<RectTransform>(), 0.04f, 0.05f, 0.96f, 0.24f);
            _barcodeInput.characterLimit = 128;
            _barcodeInput.keyboardType = TouchScreenKeyboardType.NumberPad;

            _referenceText = CreateText("ReferenceCount", panelRect, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(_referenceText.rectTransform, 0.04f, 0.285f, 0.96f, 0.325f);

            _statusText = CreateText("Status", panelRect, 19, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_statusText.rectTransform, 0.055f, 0.215f, 0.945f, 0.285f);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Truncate;

            _captureButton = CreateButton("CaptureReference", panelRect, "التقاط مرجع / Capture");
            Place(_captureButton.GetComponent<RectTransform>(), 0.06f, 0.15f, 0.49f, 0.21f);
            _captureButton.onClick.AddListener(() => CaptureReferenceRequested?.Invoke());

            _saveButton = CreateButton("SaveProduct", panelRect, "حفظ التصحيح / Save Override");
            Place(_saveButton.GetComponent<RectTransform>(), 0.51f, 0.15f, 0.94f, 0.21f);
            _saveButton.onClick.AddListener(HandleSave);

            _testButton = CreateButton("TestRecognition", panelRect, "اختبر التعرف الآن / Test Recognition");
            Place(_testButton.GetComponent<RectTransform>(), 0.06f, 0.085f, 0.94f, 0.145f);
            _testButton.onClick.AddListener(() => TestRecognitionRequested?.Invoke());

            _clearButton = CreateButton("ClearDraft", panelRect, "مسح / Clear");
            Place(_clearButton.GetComponent<RectTransform>(), 0.06f, 0.02f, 0.47f, 0.075f);
            _clearButton.onClick.AddListener(() => ClearDraftRequested?.Invoke());

            _closeButton = CreateButton("Close", panelRect, "رجوع / Back");
            Place(_closeButton.GetComponent<RectTransform>(), 0.53f, 0.02f, 0.94f, 0.075f);
            _closeButton.onClick.AddListener(() => CloseRequested?.Invoke());

            _panel.SetActive(false);
            UpdateProgress();
        }

        private InputField CreateInputField(string name, Transform parent, string placeholderValue)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.21f, 1f);

            var input = rect.gameObject.AddComponent<InputField>();
            var text = CreateText("Text", rect, 21, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(14f, 4f);
            text.rectTransform.offsetMax = new Vector2(-14f, -4f);

            var placeholder = CreateText("Placeholder", rect, 19, FontStyle.Italic, TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(14f, 4f);
            placeholder.rectTransform.offsetMax = new Vector2(-14f, -4f);
            placeholder.text = placeholderValue;
            placeholder.color = new Color(0.62f, 0.67f, 0.72f, 1f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = image;
            return input;
        }

        private Button CreateButton(string name, Transform parent, string value)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.48f, 0.72f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var label = CreateText("Text", rect, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = value;
            return button;
        }

        private Text CreateText(string name, Transform parent, int size, FontStyle style, TextAnchor alignment)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var objectName = new GameObject(name, typeof(RectTransform));
            var rect = objectName.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
