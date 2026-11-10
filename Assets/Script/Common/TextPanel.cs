// Copyright (c) MR-ExitReady contributors.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MRExitReady.Common
{
    /// <summary>
    /// A world-space canvas with a background and one or two columns of rich text, built entirely
    /// from code. Sizes are in millimetres (the canvas is scaled 0.001). Placement is up to the
    /// owner (<see cref="HeadFollowPanel"/>, a wrist panel, ...): move <see cref="Root"/>.
    /// </summary>
    public class TextPanel
    {
        private readonly RectTransform _canvas;
        private readonly Image _background;

        public Transform Root { get; }

        /// <summary>The (left) text column.</summary>
        public TextMeshProUGUI Text { get; }

        /// <summary>The right text column, or null for a single-column panel.</summary>
        public TextMeshProUGUI Text2 { get; }

        public bool Visible
        {
            get => _canvas.gameObject.activeSelf;
            set => _canvas.gameObject.SetActive(value);
        }

        /// <param name="root">Transform the canvas is created under; move it to place the panel.</param>
        /// <param name="sizeMm">Panel size in millimetres.</param>
        /// <param name="textSize">Font size in millimetres.</param>
        /// <param name="columns">1 or 2 text columns.</param>
        /// <param name="wrap">Wrap long lines instead of letting them run off the panel.</param>
        public TextPanel(Transform root, Vector2 sizeMm, float textSize, Color background, int columns = 1, bool wrap = false)
        {
            Root = root;

            var canvasGo = new GameObject(root.name + "Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            _canvas = canvasGo.GetComponent<RectTransform>();
            _canvas.SetParent(root, false);
            _canvas.sizeDelta = sizeMm;
            _canvas.localScale = Vector3.one * 0.001f;

            var bgGo = new GameObject("Background", typeof(Image));
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.SetParent(_canvas, false);
            Stretch(bgRect, Vector2.zero, Vector2.one, 0f);
            _background = bgGo.GetComponent<Image>();
            _background.color = background;

            var inset = Mathf.Clamp(Mathf.Min(sizeMm.x, sizeMm.y) * 0.04f, 6f, 18f);
            if (columns >= 2)
            {
                Text = MakeText("TextLeft", new Vector2(0f, 0f), new Vector2(0.5f, 1f), inset, textSize, wrap);
                Text2 = MakeText("TextRight", new Vector2(0.5f, 0f), new Vector2(1f, 1f), inset, textSize, wrap);
            }
            else
            {
                Text = MakeText("Text", Vector2.zero, Vector2.one, inset, textSize, wrap);
            }
        }

        public void SetBackground(Color color) => _background.color = color;

        private TextMeshProUGUI MakeText(string name, Vector2 anchorMin, Vector2 anchorMax, float inset, float size, bool wrap)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_canvas, false);
            Stretch(rect, anchorMin, anchorMax, inset);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.richText = true;
            text.color = Color.white;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, float inset)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
