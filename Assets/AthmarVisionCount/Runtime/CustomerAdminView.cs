using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class CustomerAdminView : MonoBehaviour
    {
        public event Action<string> PinSetupRequested;
        public event Action<string> UnlockRequested;
        public event Action<string, string> InstallRequested;
        public event Action RollbackRequested;
        public event Action CloseRequested;

        private Font _font;
        private GameObject _panel;
        private GameObject _authenticationSection;
        private GameObject _packageSection;
        private Text _title;
        private Text _customerText;
        private Text _statusText;
        private Text _authButtonText;
        private InputField _pinInput;
        private InputField _manifestUrlInput;
        private InputField _manifestHashInput;
        private Button _authButton;
        private Button _installButton;
        private Button _rollbackButton;
        private Button _manageProductsButton;
        private Button _closeButton;
        private bool _pinConfigured;
        private bool _configurationRequired;
        private bool _hasPreviousPackage;
        private bool _hasActiveCustomer;
        private string _activeCustomerCode = string.Empty;

        public bool IsVisible => _panel != null && _panel.activeSelf;
        public bool IsUnlocked => IsVisible && _packageSection != null && _packageSection.activeSelf;

        private void Awake()
        {
            EnsureInterfaceBuilt();
        }

        public void ShowLocked(bool pinConfigured, bool configurationRequired)
        {
            EnsureInterfaceBuilt();

            _pinConfigured = pinConfigured;
            _configurationRequired = configurationRequired;
            _hasActiveCustomer = false;
            _activeCustomerCode = string.Empty;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _authenticationSection.SetActive(true);
            _packageSection.SetActive(false);
            _pinInput.text = string.Empty;
            _authButtonText.text = pinConfigured
                ? "فتح لوحة الإدارة / Unlock"
                : "إنشاء رمز المدير / Create PIN";
            _title.text = pinConfigured
                ? "دخول المدير / Administrator Access"
                : "إعداد حماية الإدارة / Secure Administration";
            _customerText.text = string.Empty;
            _statusText.color = Color.white;
            _statusText.text = pinConfigured
                ? "أدخل رمز المدير المكوّن من 6 إلى 12 رقمًا."
                : "أنشئ رمزًا من 6 إلى 12 رقمًا قبل إعداد العميل.";
            _closeButton.gameObject.SetActive(!configurationRequired);
            SetBusy(false);
        }

        public void ShowUnlocked(string customerCode, bool hasPreviousPackage)
        {
            EnsureInterfaceBuilt();

            _hasPreviousPackage = hasPreviousPackage;
            _activeCustomerCode = string.IsNullOrWhiteSpace(customerCode) ? string.Empty : customerCode.Trim();
            _hasActiveCustomer = _activeCustomerCode.Length > 0;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            _authenticationSection.SetActive(false);
            _packageSection.SetActive(true);
            _pinInput.text = string.Empty;
            _title.text = "إدارة العميل والنموذج / Customer & Model Administration";
            _customerText.text = !_hasActiveCustomer
                ? "لا توجد حزمة عميل مفعلة / No active customer package"
                : "العميل الحالي / Active customer: " + _activeCustomerCode;
            _statusText.color = Color.white;
            _statusText.text = _hasActiveCustomer
                ? "يمكنك إضافة منتجات هذا العميل من داخل التطبيق أو تحديث حزمة الرؤية."
                : "ثبّت حزمة العميل أولًا، ثم أضف منتجاته من داخل التطبيق.";
            _closeButton.gameObject.SetActive(!_configurationRequired || _hasActiveCustomer);
            SetBusy(false);
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
            _authButton.interactable = !busy;
            _installButton.interactable = !busy;
            _rollbackButton.interactable = !busy && _hasPreviousPackage;
            _manageProductsButton.interactable = !busy && _hasActiveCustomer;
            _closeButton.interactable = !busy;
        }

        public void Hide()
        {
            EnsureInterfaceBuilt();
            _panel.SetActive(false);
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

            var panelRect = CreateRect("AdministrationPanel", transform);
            Stretch(panelRect);
            var background = panelRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.025f, 0.04f, 0.065f, 0.995f);
            _panel = panelRect.gameObject;

            _title = CreateText("Title", panelRect, 38, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(_title.rectTransform, 0.05f, 0.89f, 0.95f, 0.97f);

            _customerText = CreateText("Customer", panelRect, 25, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_customerText.rectTransform, 0.05f, 0.82f, 0.95f, 0.88f);

            _statusText = CreateText("Status", panelRect, 23, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(_statusText.rectTransform, 0.07f, 0.72f, 0.93f, 0.82f);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Truncate;

            var authenticationRect = CreateRect("AuthenticationSection", panelRect);
            Place(authenticationRect, 0.08f, 0.43f, 0.92f, 0.71f);
            var authenticationBackground = authenticationRect.gameObject.AddComponent<Image>();
            authenticationBackground.color = new Color(0.07f, 0.10f, 0.14f, 1f);
            _authenticationSection = authenticationRect.gameObject;

            var pinLabel = CreateText("AdminPinLabel", authenticationRect, 27, FontStyle.Bold, TextAnchor.MiddleCenter);
            pinLabel.text = "رمز المدير\nAdministrator PIN";
            Place(pinLabel.rectTransform, 0.05f, 0.73f, 0.95f, 0.96f);

            _pinInput = CreateInputField("AdminPin", authenticationRect, "أدخل 6–12 رقمًا / Enter 6–12 digits");
            Place(_pinInput.GetComponent<RectTransform>(), 0.08f, 0.40f, 0.92f, 0.69f);
            _pinInput.contentType = InputField.ContentType.Pin;
            _pinInput.keyboardType = TouchScreenKeyboardType.NumberPad;
            _pinInput.characterLimit = 12;

            _authButton = CreateButton("Authenticate", authenticationRect, string.Empty, out _authButtonText);
            Place(_authButton.GetComponent<RectTransform>(), 0.08f, 0.07f, 0.92f, 0.33f);
            _authButton.onClick.AddListener(HandleAuthentication);

            var packageRect = CreateRect("PackageSection", panelRect);
            Place(packageRect, 0.05f, 0.15f, 0.95f, 0.70f);
            var packageBackground = packageRect.gameObject.AddComponent<Image>();
            packageBackground.color = new Color(0.07f, 0.10f, 0.14f, 1f);
            _packageSection = packageRect.gameObject;

            var packageTitle = CreateText("PackageTitle", packageRect, 27, FontStyle.Bold, TextAnchor.MiddleCenter);
            packageTitle.text = "حزمة الرؤية ومنتجات العميل / Vision Package & Customer Products";
            Place(packageTitle.rectTransform, 0.04f, 0.84f, 0.96f, 0.98f);

            _manifestUrlInput = CreateInputField("ManifestUrl", packageRect, "رابط Manifest HTTPS");
            Place(_manifestUrlInput.GetComponent<RectTransform>(), 0.05f, 0.65f, 0.95f, 0.80f);
            _manifestUrlInput.contentType = InputField.ContentType.Standard;
            _manifestUrlInput.keyboardType = TouchScreenKeyboardType.URL;

            _manifestHashInput = CreateInputField("ManifestHash", packageRect, "بصمة Manifest SHA-256");
            Place(_manifestHashInput.GetComponent<RectTransform>(), 0.05f, 0.46f, 0.95f, 0.61f);
            _manifestHashInput.contentType = InputField.ContentType.Alphanumeric;
            _manifestHashInput.characterLimit = 64;

            _installButton = CreateButton("Install", packageRect, "تحقق وثبّت / Verify & Install", out _);
            Place(_installButton.GetComponent<RectTransform>(), 0.05f, 0.27f, 0.57f, 0.41f);
            _installButton.onClick.AddListener(() =>
                InstallRequested?.Invoke(_manifestUrlInput.text.Trim(), _manifestHashInput.text.Trim()));

            _rollbackButton = CreateButton("Rollback", packageRect, "رجوع للسابق / Rollback", out _);
            Place(_rollbackButton.GetComponent<RectTransform>(), 0.60f, 0.27f, 0.95f, 0.41f);
            _rollbackButton.onClick.AddListener(() => RollbackRequested?.Invoke());

            _manageProductsButton = CreateButton(
                "ManageProducts",
                packageRect,
                "إضافة المنتجات ذاتيًا / Self-Service Products",
                out _);
            Place(_manageProductsButton.GetComponent<RectTransform>(), 0.05f, 0.06f, 0.95f, 0.21f);
            _manageProductsButton.onClick.AddListener(OpenProductEnrollment);

            _closeButton = CreateButton("Close", panelRect, "إغلاق / Close", out _);
            Place(_closeButton.GetComponent<RectTransform>(), 0.30f, 0.045f, 0.70f, 0.115f);
            _closeButton.onClick.AddListener(() => CloseRequested?.Invoke());

            _packageSection.SetActive(false);
            _panel.SetActive(false);
        }

        private void OpenProductEnrollment()
        {
            if (!_hasActiveCustomer)
                return;

            try
            {
                var coordinator = GetComponent<ProductEnrollmentCoordinator>();
                if (coordinator == null)
                    coordinator = gameObject.AddComponent<ProductEnrollmentCoordinator>();
                coordinator.Open(_activeCustomerCode);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, true);
            }
        }

        private void HandleAuthentication()
        {
            var pin = _pinInput.text;
            if (_pinConfigured)
                UnlockRequested?.Invoke(pin);
            else
                PinSetupRequested?.Invoke(pin);
        }

        private InputField CreateInputField(string name, Transform parent, string placeholderValue)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.21f, 1f);
            var input = rect.gameObject.AddComponent<InputField>();
            var text = CreateText("Text", rect, 24, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(18f, 6f);
            text.rectTransform.offsetMax = new Vector2(-18f, -6f);
            var placeholder = CreateText("Placeholder", rect, 22, FontStyle.Italic, TextAnchor.MiddleLeft);
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

        private Button CreateButton(string name, Transform parent, string value, out Text label)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.48f, 0.72f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = CreateText("Text", rect, 23, FontStyle.Bold, TextAnchor.MiddleCenter);
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
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = gameObject.GetComponent<RectTransform>();
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
