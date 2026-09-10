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
        private bool _busy;

        public bool IsVisible => _panel != null && _panel.activeSelf;

        private void Awake()
        {
            EnsureInterfaceBuilt();
        }

        public void Show(string customerCode, int productCount)
        {
            EnsureInterfaceBuilt();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _customerText.text = "العميل / Customer: " + (customerCode ?? string.Empty);
            _productCount = Math.Max(0, productCount);
            ResetDraft();
            SetProductCount(_productCount);
            SetStatus(
                "أدخل بيانات المنتج، وجّه الكاميرا إلى منتج واحد يملأ الإطار، ثم التقط 8–15 صورة من زوايا مختلفة. " +
                "الصور الخام لا تُحفظ.");
            SetBusy(false);
        }

        public void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
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
                $"الصور المرجعية / References: {_referenceCount}/{ProductEnrollmentStore.MinimumReferenceCount} min " +
                $"({_referenceCount}/{ProductEnrollmentStore.MaximumReferenceCount} max)";
            _productCountText.text = $"المنتجات المسجلة / Enrolled products: {_productCount}";
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
            _testButton.interactable = !_busy && _productCount > 0;
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

            var title = CreateText("Title", panelRect, 37, FontStyle.Bold, TextAnchor.MiddleCenter);
            title.text = "إضافة المنتجات ذاتيًا / Self-Service Product Enrollment";
            Place(title.rectTransform, 0.05f, 0.91f, 0.95f, 0.98f);

            _customerText = CreateText("Customer", panelRect, 23, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_customerText.rectTransform, 0.05f, 0.865f, 0.95f, 0.91f);

            _productCountText = CreateText("ProductCount", panelRect, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(_productCountText.rectTransform, 0.05f, 0.82f, 0.95f, 0.865f);

            var formRect = CreateRect("ProductForm", panelRect);
            Place(formRect, 0.055f, 0.42f, 0.945f, 0.81f);
            var formBackground = formRect.gameObject.AddComponent<Image>();
            formBackground.color = new Color(0.07f, 0.10f, 0.14f, 1f);

            _skuInput = CreateInputField("Sku", formRect, "SKU / رمز الصنف");
            Place(_skuInput.GetComponent<RectTransform>(), 0.05f, 0.76f, 0.95f, 0.94f);
            _skuInput.characterLimit = 64;

            _nameArabicInput = CreateInputField("NameArabic", formRect, "اسم المنتج بالعربية");
            Place(_nameArabicInput.GetComponent<RectTransform>(), 0.05f, 0.55f, 0.95f, 0.72f);
            _nameArabicInput.characterLimit = 160;

            _nameEnglishInput = CreateInputField("NameEnglish", formRect, "Product name in English");
            Place(_nameEnglishInput.GetComponent<RectTransform>(), 0.05f, 0.34f, 0.95f, 0.51f);
            _nameEnglishInput.characterLimit = 160;

            _barcodeInput = CreateInputField("Barcode", formRect, "Barcode اختياري / optional");
            Place(_barcodeInput.GetComponent<RectTransform>(), 0.05f, 0.13f, 0.95f, 0.30f);
            _barcodeInput.characterLimit = 128;
            _barcodeInput.keyboardType = TouchScreenKeyboardType.NumberPad;

            _referenceText = CreateText("ReferenceCount", panelRect, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(_referenceText.rectTransform, 0.05f, 0.365f, 0.95f, 0.415f);

            _statusText = CreateText("Status", panelRect, 21, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_statusText.rectTransform, 0.065f, 0.275f, 0.935f, 0.365f);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Truncate;

            _captureButton = CreateButton(
                "CaptureReference",
                panelRect,
                "التقاط صورة مرجعية / Capture Reference");
            Place(_captureButton.GetComponent<RectTransform>(), 0.07f, 0.205f, 0.49f, 0.27f);
            _captureButton.onClick.AddListener(() => CaptureReferenceRequested?.Invoke());

            _saveButton = CreateButton(
                "SaveProduct",
                panelRect,
                "حفظ المنتج / Save Product");
            Place(_saveButton.GetComponent<RectTransform>(), 0.51f, 0.205f, 0.93f, 0.27f);
            _saveButton.onClick.AddListener(HandleSave);

            _testButton = CreateButton(
                "TestRecognition",
                panelRect,
                "اختبر المنتج أمام الكاميرا / Test Recognition");
            Place(_testButton.GetComponent<RectTransform>(), 0.07f, 0.13f, 0.93f, 0.195f);
            _testButton.onClick.AddListener(() => TestRecognitionRequested?.Invoke());

            _clearButton = CreateButton(
                "ClearDraft",
                panelRect,
                "مسح النموذج / Clear");
            Place(_clearButton.GetComponent<RectTransform>(), 0.07f, 0.055f, 0.46f, 0.12f);
            _clearButton.onClick.AddListener(() => ClearDraftRequested?.Invoke());

            _closeButton = CreateButton(
                "Close",
                panelRect,
                "رجوع للإدارة / Back");
            Place(_closeButton.GetComponent<RectTransform>(), 0.54f, 0.055f, 0.93f, 0.12f);
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
            var text = CreateText("Text", rect, 23, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(18f, 6f);
            text.rectTransform.offsetMax = new Vector2(-18f, -6f);

            var placeholder = CreateText("Placeholder", rect, 21, FontStyle.Italic, TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(18f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-18f, -6f);
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
            var label = CreateText("Text", rect, 21, FontStyle.Bold, TextAnchor.MiddleCenter);
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
