using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Applies ArabicUiTextFormatter to legacy Unity UI Text components before they are displayed.
    /// This keeps the existing dynamically-created interface while fixing Arabic shaping and RTL order.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class ArabicUiRuntimeFix : MonoBehaviour
    {
        private sealed class TextState
        {
            public Text Target;
            public string VisualText = string.Empty;
            public bool AlignmentAdjusted;
            public TextAnchor AlignmentBeforeAdjustment;
        }

        private readonly Dictionary<int, TextState> _states = new Dictionary<int, TextState>();
        private readonly HashSet<int> _editableTextIds = new HashSet<int>();
        private float _nextRefreshAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeFixExists()
        {
            if (FindFirstObjectByType<ArabicUiRuntimeFix>() != null)
                return;

            var root = new GameObject("Arabic UI Runtime Fix");
            DontDestroyOnLoad(root);
            root.AddComponent<ArabicUiRuntimeFix>();
        }

        private void Start()
        {
            RefreshAllText();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextRefreshAt)
                return;

            _nextRefreshAt = Time.unscaledTime + 0.05f;
            RefreshAllText();
        }

        private void RefreshAllText()
        {
            _editableTextIds.Clear();
            var inputFields = FindObjectsByType<InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var input in inputFields)
            {
                if (input != null && input.textComponent != null)
                    _editableTextIds.Add(input.textComponent.GetInstanceID());
            }

            var texts = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var text in texts)
            {
                if (text == null || _editableTextIds.Contains(text.GetInstanceID()))
                    continue;

                ApplyToText(text);
            }
        }

        private void ApplyToText(Text text)
        {
            var id = text.GetInstanceID();
            if (!_states.TryGetValue(id, out var state) || state.Target != text)
            {
                state = new TextState { Target = text };
                _states[id] = state;
            }

            var current = text.text ?? string.Empty;
            if (current == state.VisualText)
                return;

            var containsArabic = ArabicUiTextFormatter.ContainsArabic(current);
            var visual = containsArabic ? ArabicUiTextFormatter.Format(current) : current;

            if (containsArabic)
            {
                if (!state.AlignmentAdjusted && TryGetRightAligned(text.alignment, out var rightAligned))
                {
                    state.AlignmentBeforeAdjustment = text.alignment;
                    state.AlignmentAdjusted = true;
                    text.alignment = rightAligned;
                }
            }
            else if (state.AlignmentAdjusted)
            {
                text.alignment = state.AlignmentBeforeAdjustment;
                state.AlignmentAdjusted = false;
            }

            if (visual != current)
                text.text = visual;
            state.VisualText = visual;
        }

        private static bool TryGetRightAligned(TextAnchor current, out TextAnchor adjusted)
        {
            switch (current)
            {
                case TextAnchor.UpperLeft:
                    adjusted = TextAnchor.UpperRight;
                    return true;
                case TextAnchor.MiddleLeft:
                    adjusted = TextAnchor.MiddleRight;
                    return true;
                case TextAnchor.LowerLeft:
                    adjusted = TextAnchor.LowerRight;
                    return true;
                default:
                    adjusted = current;
                    return false;
            }
        }
    }
}
