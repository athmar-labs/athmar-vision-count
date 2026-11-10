using System;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Places the rotated camera preview inside its panel without distortion and maps detection
    /// boxes (raw texture coordinates, top-origin) onto that preview. All sizes are panel units.
    /// The preview RawImage is laid out unrotated and then rotated by the camera angle, so its
    /// pre-rotation size is the visual size with width and height swapped for 90/270 degrees.
    /// </summary>
    public readonly struct CameraViewportLayout
    {
        public CameraViewportLayout(
            int textureWidth,
            int textureHeight,
            CameraFrameOrientation orientation,
            float panelWidth,
            float panelHeight)
        {
            if (textureWidth <= 0 || textureHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(textureWidth), "Texture dimensions must be positive.");
            if (panelWidth <= 0f || panelHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(panelWidth), "Panel dimensions must be positive.");

            Orientation = orientation;
            PanelWidth = panelWidth;
            PanelHeight = panelHeight;

            var uprightAspect = orientation.SwapsAxes
                ? textureHeight / (float)textureWidth
                : textureWidth / (float)textureHeight;
            var visualWidth = Math.Min(panelWidth, panelHeight * uprightAspect);
            VisualWidth = visualWidth;
            VisualHeight = visualWidth / uprightAspect;
            VisualLeft = (panelWidth - VisualWidth) * 0.5f;
            VisualTop = (panelHeight - VisualHeight) * 0.5f;
            UnrotatedWidth = orientation.SwapsAxes ? VisualHeight : VisualWidth;
            UnrotatedHeight = orientation.SwapsAxes ? VisualWidth : VisualHeight;
        }

        public CameraFrameOrientation Orientation { get; }
        public float PanelWidth { get; }
        public float PanelHeight { get; }

        /// <summary>The preview as the operator sees it, top-left origin inside the panel.</summary>
        public float VisualLeft { get; }
        public float VisualTop { get; }
        public float VisualWidth { get; }
        public float VisualHeight { get; }

        /// <summary>Size to give the preview RectTransform before its rotation is applied.</summary>
        public float UnrotatedWidth { get; }
        public float UnrotatedHeight { get; }

        /// <summary>Maps a texture-space detection box to the panel, normalized with a top-left origin.</summary>
        public NormalizedRect TextureBoxToPanel(NormalizedRect textureBox)
        {
            var upright = Orientation.TextureToUpright(textureBox);
            return new NormalizedRect(
                (VisualLeft + (upright.X * VisualWidth)) / PanelWidth,
                (VisualTop + (upright.Y * VisualHeight)) / PanelHeight,
                upright.Width * VisualWidth / PanelWidth,
                upright.Height * VisualHeight / PanelHeight);
        }
    }
}
