using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AthmarLabs.VisionCount
{
    public sealed class VisionCountView : MonoBehaviour
    {
        private sealed class DetectionWidget
        {
            public GameObject Root;
            public Text Label;
        }

        public event Action PauseResumeRequested;
        public event Action ReviewRequested;
        public event Action ConfirmRequested;
        public event Action CancelReviewRequested;
        public event Action DeleteDataRequested;
        public event Action LanguageToggleRequested;
        public event Action<string, int> ManualCountRequested;
        public event Action AdministrationRequested;

        private Font _font;
        private string _language = "en";
        private RawImage _cameraImage;
        private AspectRatioFitter _cameraAspect;
        private RectTransform _detectionLayer;
        private Text _titleText;
        private Text _statusText;
        private Text _countsText;
        private Text _privacyText;
        private Text _pauseButtonText;
        private Text _reviewButtonText;
        private Text _deleteButtonText;
        private Text _languageButtonText;
        private Text _adminButtonText;
        private GameObject _reviewPanel;
        private Text _reviewTitleText;
        private InputField _operatorInput;
        private InputField _locationInput;
        private RectTransform _reviewContent;
        private Text _confirmButtonText;
        private Text _cancelButtonText;
        private readonly List<DetectionWidget> _detectionWidgets = new List<DetectionWidget>();

        public string OperatorReference => _operatorInput == null ? string.Empty : _operatorInput.text;
        public string LocationReference => _locationInput == null ? string.Empty : _locationInput.text;
        public string Language => _language;

        private void Awake()
        {
            BuildInterface();
        }

        public void SetLanguage(string language)
        {
            _language = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
            var rtl = VisionCountLocalization.IsArabic(_language);
            _titleText.text = VisionCountLocalization.Text("app_title", _language);
            _privacyText.text = VisionCountLocalization.Text("privacy_note", _language);
            _pauseButtonText.text = VisionCountLocalization.Text("pause", _language);
            _reviewButtonText.text = VisionCountLocalization.Text("review", _language);
            _deleteButtonText.text = VisionCountLocalization.Text("delete_data", _language);
            _languageButtonText.text = VisionCountLocalization.Text("language", _language);
            _adminButtonText.text = VisionCountLocalization.Text("administration", _language);
            _reviewTitleText.text = VisionCountLocalization.Text("review_title", _language);
            _confirmButtonText.text = VisionCountLocalization.Text("confirm_export", _language);
            _cancelButtonText.text = VisionCountLocalization.Text("cancel", _language);
            SetInputPlaceholder(_operatorInput, VisionCountLocalization.Text("operator", _language), rtl);
            SetInputPlaceholder(_locationInput, VisionCountLocalization.Text("location", _language), rtl);
            _countsText.alignment = rtl ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            _statusText.alignment = rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        }

        public void SetStatus(string statusKey)
        {
            _statusText.color = Color.white;
            _statusText.text = VisionCountLocalization.Text(statusKey, _language);
        }

        public void SetStatusMessage(string message, bool isError = false)
        {
            _statusText.color = isError ? new Color(1f, 0.55f, 0.55f) : Color.white;
            _statusText.text = message ?? string.Empty;
        }

        public void SetPaused(bool paused)
        {
            _pauseButtonText.text = VisionCountLocalization.Text(paused ? "resume" : "pause", _language);
        }

        public void SetCamera(Texture texture, int rotationAngle, bool verticallyMirrored)
        {
            if (_cameraImage.texture != texture)
                _cameraImage.texture = texture;
            if (texture == null)
                return;

            var rotated = Math.Abs(rotationAngle) == 90 || Math.Abs(rotationAngle) == 270;
            _cameraAspect.aspectRatio = rotated
                ? texture.height / (float)Math.Max(1, texture.width)
                : texture.width / (float)Math.Max(1, texture.height);
            _cameraImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rotationAngle);
            _cameraImage.uvRect = verticallyMirrored ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        }

        public void ShowDetections(IReadOnlyList<Detection> detections, SkuCatalogue catalogue)
        {
            detections = detections ?? Array.Empty<Detection>();
            EnsureDetectionWidgets(detections.Count);

            for (var index = 0; index < _detectionWidgets.Count; index++)
            {
                var widget = _detectionWidgets[index];
                var active = index < detections.Count;
                widget.Root.SetActive(active);
                if (!active)
                    continue;

                var detection = detections[index];
                var rect = widget.Root.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(detection.Bounds.X, 1f - detection.Bounds.Y - detection.Bounds.Height);
                rect.anchorMax = new Vector2(detection.Bounds.X + detection.Bounds.Width, 1f - detection.Bounds.Y);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                widget.Label.text = catalogue.ResolveDisplayName(detection.Sku, _language) + " " + detection.Confidence.ToString("P0");
                widget.Label.alignment = VisionCountLocalization.IsArabic(_language) ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            }
        }

        public void ShowCounts(IReadOnlyDictionary<string, int> counts, SkuCatalogue catalogue)
        {
            if (counts == null || counts.Count == 0)
            {
                _countsText.text = VisionCountLocalization.Text("empty_counts", _language);
                return;
            }

            var keys = counts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            var builder = new StringBuilder();
            foreach (var sku in keys)
                builder.Append(catalogue.ResolveDisplayName(sku, _language)).Append("  ×  ").Append(counts[sku]).AppendLine();
            _countsText.text = builder.ToString().TrimEnd();
        }

        public void ShowReview(ScanSessionRecord session, SkuCatalogue catalogue)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            _reviewPanel.SetActive(true);
            ClearChildren(_reviewContent);
            foreach (var line in session.Lines.OrderBy(value => value.Sku, StringComparer.OrdinalIgnoreCase))
                CreateReviewRow(line, catalogue);
        }

        public void HideReview()
        {
            _reviewPanel.SetActive(false);
        }

        private void BuildInterface()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var root = CreateRect("Root", transform);
            Stretch(root);
            var background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.055f, 0.075f, 0.10f, 1f);

            var header = CreateRect("Header", root);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 112f);
            header.anchoredPosition = Vector2.zero;
            var headerImage = header.gameObject.AddComponent<Image>();
            headerImage.color = new Color(0.08f, 0.12f, 0.17f, 1f);

            _titleText = CreateText("Title", header, 34, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.rectTransform.anchorMin = new Vector2(0.24f, 0f);
            _titleText.rectTransform.anchorMax = new Vector2(0.76f, 1f);
            _titleText.rectTransform.offsetMin = Vector2.zero;
            _titleText.rectTransform.offsetMax = Vector2.zero;

            var languageButton = CreateButton("Language", header, out _languageButtonText);
            var languageRect = languageButton.GetComponent<RectTransform>();
            languageRect.anchorMin = new Vector2(0.78f, 0.14f);
            languageRect.anchorMax = new Vector2(0.97f, 0.86f);
            languageRect.offsetMin = Vector2.zero;
            languageRect.offsetMax = Vector2.zero;
            languageButton.onClick.AddListener(() => LanguageToggleRequested?.Invoke());

            var adminButton = CreateButton("Admin", header, out _adminButtonText);
            var adminRect = adminButton.GetComponent<RectTransform>();
            adminRect.anchorMin = new Vector2(0.03f, 0.14f);
            adminRect.anchorMax = new Vector2(0.22f, 0.86f);
            adminRect.offsetMin = Vector2.zero;
            adminRect.offsetMax = Vector2.zero;
            adminButton.onClick.AddListener(() => AdministrationRequested?.Invoke());

            var cameraPanel = CreateRect("CameraPanel", root);
            cameraPanel.anchorMin = new Vector2(0.025f, 0.42f);
            cameraPanel.anchorMax = new Vector2(0.975f, 0.92f);
            cameraPanel.offsetMin = Vector2.zero;
            cameraPanel.offsetMax = Vector2.zero;
            var cameraBackground = cameraPanel.gameObject.AddComponent<Image>();
            cameraBackground.color = Color.black;
            var cameraMask = cameraPanel.gameObject.AddComponent<Mask>();
            cameraMask.showMaskGraphic = true;

            var cameraRect = CreateRect("Camera", cameraPanel);
            Stretch(cameraRect);
            _cameraImage = cameraRect.gameObject.AddComponent<RawImage>();
            _cameraImage.color = Color.white;
            _cameraAspect = cameraRect.gameObject.AddComponent<AspectRatioFitter>();
            _cameraAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _cameraAspect.aspectRatio = 16f / 9f;

            _detectionLayer = CreateRect("DetectionLayer", cameraPanel);
            Stretch(_detectionLayer);

            _statusText = CreateText("Status", cameraPanel, 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            _statusText.rectTransform.anchorMin = new Vector2(0.025f, 0.89f);
            _statusText.rectTransform.anchorMax = new Vector2(0.975f, 0.985f);
            _statusText.rectTransform.offsetMin = new Vector2(18f, 0f);
            _statusText.rectTransform.offsetMax = new Vector2(-18f, 0f);
            var statusBackground = _statusText.gameObject.AddComponent<Shadow>();
            statusBackground.effectColor = Color.black;
            statusBackground.effectDistance = new Vector2(2f, -2f);

            var bottom = CreateRect("Bottom", root);
            bottom.anchorMin = new Vector2(0.025f, 0.025f);
            bottom.anchorMax = new Vector2(0.975f, 0.40f);
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = Vector2.zero;
            var bottomImage = bottom.gameObject.AddComponent<Image>();
            bottomImage.color = new Color(0.08f, 0.12f, 0.17f, 1f);

            _countsText = CreateText("Counts", bottom, 31, FontStyle.Normal, TextAnchor.UpperLeft);
            _countsText.rectTransform.anchorMin = new Vector2(0.035f, 0.28f);
            _countsText.rectTransform.anchorMax = new Vector2(0.965f, 0.95f);
            _countsText.rectTransform.offsetMin = Vector2.zero;
            _countsText.rectTransform.offsetMax = Vector2.zero;
            _countsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _countsText.verticalOverflow = VerticalWrapMode.Truncate;

            _privacyText = CreateText("Privacy", bottom, 21, FontStyle.Italic, TextAnchor.MiddleCenter);
            _privacyText.color = new Color(0.72f, 0.78f, 0.84f, 1f);
            _privacyText.rectTransform.anchorMin = new Vector2(0.035f, 0.16f);
            _privacyText.rectTransform.anchorMax = new Vector2(0.965f, 0.28f);
            _privacyText.rectTransform.offsetMin = Vector2.zero;
            _privacyText.rectTransform.offsetMax = Vector2.zero;

            var controls = CreateRect("Controls", bottom);
            controls.anchorMin = new Vector2(0.025f, 0.02f);
            controls.anchorMax = new Vector2(0.975f, 0.18f);
            controls.offsetMin = Vector2.zero;
            controls.offsetMax = Vector2.zero;
            var controlsLayout = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            controlsLayout.spacing = 12f;
            controlsLayout.childAlignment = TextAnchor.MiddleCenter;
            controlsLayout.childControlWidth = true;
            controlsLayout.childForceExpandWidth = true;

            var pauseButton = CreateButton("Pause", controls, out _pauseButtonText);
            pauseButton.onClick.AddListener(() => PauseResumeRequested?.Invoke());
            var reviewButton = CreateButton("Review", controls, out _reviewButtonText);
            reviewButton.onClick.AddListener(() => ReviewRequested?.Invoke());
            var deleteButton = CreateButton("Delete", controls, out _deleteButtonText);
            deleteButton.onClick.AddListener(() => DeleteDataRequested?.Invoke());

            BuildReviewPanel(root);
            SetLanguage("en");
            SetStatus("configuration_required");
        }

        private void BuildReviewPanel(RectTransform root)
        {
            var panel = CreateRect("ReviewPanel", root);
            Stretch(panel);
            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.035f, 0.05f, 0.075f, 0.99f);
            _reviewPanel = panel.gameObject;

            _reviewTitleText = CreateText("ReviewTitle", panel, 40, FontStyle.Bold, TextAnchor.MiddleCenter);
            _reviewTitleText.rectTransform.anchorMin = new Vector2(0.04f, 0.89f);
            _reviewTitleText.rectTransform.anchorMax = new Vector2(0.96f, 0.98f);
            _reviewTitleText.rectTransform.offsetMin = Vector2.zero;
            _reviewTitleText.rectTransform.offsetMax = Vector2.zero;

            _operatorInput = CreateInputField("Operator", panel);
            _operatorInput.GetComponent<RectTransform>().anchorMin = new Vector2(0.05f, 0.80f);
            _operatorInput.GetComponent<RectTransform>().anchorMax = new Vector2(0.48f, 0.875f);
            _operatorInput.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            _operatorInput.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            _locationInput = CreateInputField("Location", panel);
            _locationInput.GetComponent<RectTransform>().anchorMin = new Vector2(0.52f, 0.80f);
            _locationInput.GetComponent<RectTransform>().anchorMax = new Vector2(0.95f, 0.875f);
            _locationInput.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            _locationInput.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            var scrollRoot = CreateRect("ReviewScroll", panel);
            scrollRoot.anchorMin = new Vector2(0.05f, 0.17f);
            scrollRoot.anchorMax = new Vector2(0.95f, 0.77f);
            scrollRoot.offsetMin = Vector2.zero;
            scrollRoot.offsetMax = Vector2.zero;
            var scrollImage = scrollRoot.gameObject.AddComponent<Image>();
            scrollImage.color = new Color(0.08f, 0.11f, 0.15f, 1f);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();

            var viewport = CreateRect("Viewport", scrollRoot);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, -12f);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _reviewContent = CreateRect("Content", viewport);
            _reviewContent.anchorMin = new Vector2(0f, 1f);
            _reviewContent.anchorMax = new Vector2(1f, 1f);
            _reviewContent.pivot = new Vector2(0.5f, 1f);
            _reviewContent.anchoredPosition = Vector2.zero;
            _reviewContent.sizeDelta = Vector2.zero;
            var contentLayout = _reviewContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            var fitter = _reviewContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = _reviewContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var controls = CreateRect("ReviewControls", panel);
            controls.anchorMin = new Vector2(0.05f, 0.055f);
            controls.anchorMax = new Vector2(0.95f, 0.135f);
            controls.offsetMin = Vector2.zero;
            controls.offsetMax = Vector2.zero;
            var layout = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 22f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            var confirm = CreateButton("Confirm", controls, out _confirmButtonText);
            confirm.onClick.AddListener(() => ConfirmRequested?.Invoke());
            var cancel = CreateButton("Cancel", controls, out _cancelButtonText);
            cancel.onClick.AddListener(() => CancelReviewRequested?.Invoke());

            _reviewPanel.SetActive(false);
        }

        private void CreateReviewRow(CountLine line, SkuCatalogue catalogue)
        {
            var row = CreateRect("Row_" + line.Sku, _reviewContent);
            var layoutElement = row.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 92f;
            var image = row.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.21f, 1f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 10, 10);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;

            var label = CreateText("Label", row, 27, FontStyle.Normal, VisionCountLocalization.IsArabic(_language) ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            label.text = catalogue.ResolveDisplayName(line.Sku, _language) + "\n" + line.Sku;
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.preferredHeight = 72f;

            var proposed = CreateText("Proposed", row, 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            proposed.text = VisionCountLocalization.Text("proposed", _language) + "\n" + line.ProposedCount;
            proposed.gameObject.AddComponent<LayoutElement>().preferredWidth = 135f;

            var sku = line.Sku;
            var currentCount = line.ConfirmedCount;
            var minus = CreateButton("Minus", row, out var minusText);
            minusText.text = "−";
            minus.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;
            minus.onClick.AddListener(() => ManualCountRequested?.Invoke(sku, Math.Max(0, currentCount - 1)));

            var confirmed = CreateText("Confirmed", row, 31, FontStyle.Bold, TextAnchor.MiddleCenter);
            confirmed.text = currentCount.ToString();
            confirmed.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            var plus = CreateButton("Plus", row, out var plusText);
            plusText.text = "+";
            plus.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;
            plus.onClick.AddListener(() => ManualCountRequested?.Invoke(sku, currentCount + 1));
        }

        private void EnsureDetectionWidgets(int count)
        {
            while (_detectionWidgets.Count < count)
            {
                var root = CreateRect("Detection", _detectionLayer);
                var image = root.gameObject.AddComponent<Image>();
                image.color = new Color(0.1f, 0.9f, 0.45f, 0.16f);
                var outline = root.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.1f, 1f, 0.45f, 1f);
                outline.effectDistance = new Vector2(3f, -3f);
                var label = CreateText("Label", root, 20, FontStyle.Bold, TextAnchor.UpperLeft);
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(6f, 4f);
                label.rectTransform.offsetMax = new Vector2(-6f, -4f);
                label.color = Color.white;
                label.gameObject.AddComponent<Shadow>().effectColor = Color.black;
                _detectionWidgets.Add(new DetectionWidget { Root = root.gameObject, Label = label });
            }
        }

        private Button CreateButton(string name, Transform parent, out Text label)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.10f, 0.48f, 0.72f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = CreateText("Text", rect, 24, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return button;
        }

        private InputField CreateInputField(string name, Transform parent)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.21f, 1f);
            var input = rect.gameObject.AddComponent<InputField>();
            var text = CreateText("Text", rect, 25, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(18f, 6f);
            text.rectTransform.offsetMax = new Vector2(-18f, -6f);
            var placeholder = CreateText("Placeholder", rect, 23, FontStyle.Italic, TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(18f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-18f, -6f);
            placeholder.color = new Color(0.65f, 0.68f, 0.72f, 1f);
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = image;
            return input;
        }

        private void SetInputPlaceholder(InputField input, string value, bool rtl)
        {
            if (input == null)
                return;
            if (input.placeholder is Text placeholder)
            {
                placeholder.text = value;
                placeholder.alignment = rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            }
            input.textComponent.alignment = rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(Transform parent)
        {
            for (var index = parent.childCount - 1; index >= 0; index--)
                Destroy(parent.GetChild(index).gameObject);
        }
    }
}
