using System;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Aspect-preserving fit of the upright camera frame into the model's square input, padded
    /// with grey like Ultralytics training/inference. Stretching a 9:16 portrait frame into
    /// 640x640 distorts products enough to change the predicted class (bottle read as vase).
    /// </summary>
    public readonly struct TensorLetterbox
    {
        public const float PadValue = 114f / 255f;

        public TensorLetterbox(int uprightWidth, int uprightHeight, int tensorWidth, int tensorHeight)
        {
            if (uprightWidth <= 0 || uprightHeight <= 0 || tensorWidth <= 0 || tensorHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(uprightWidth), "Dimensions must be positive.");

            var scale = Math.Min(tensorWidth / (float)uprightWidth, tensorHeight / (float)uprightHeight);
            ContentWidth = uprightWidth * scale;
            ContentHeight = uprightHeight * scale;
            PadX = (tensorWidth - ContentWidth) * 0.5f;
            PadY = (tensorHeight - ContentHeight) * 0.5f;
            TensorWidth = tensorWidth;
            TensorHeight = tensorHeight;
        }

        public static TensorLetterbox ForCamera(
            int textureWidth,
            int textureHeight,
            CameraFrameOrientation orientation,
            int tensorWidth,
            int tensorHeight)
        {
            return orientation.SwapsAxes
                ? new TensorLetterbox(textureHeight, textureWidth, tensorWidth, tensorHeight)
                : new TensorLetterbox(textureWidth, textureHeight, tensorWidth, tensorHeight);
        }

        public float ContentWidth { get; }
        public float ContentHeight { get; }
        public float PadX { get; }
        public float PadY { get; }
        public int TensorWidth { get; }
        public int TensorHeight { get; }

        /// <summary>Maps a tensor pixel centre to upright normalized coordinates; false inside the padding.</summary>
        public bool TryTensorPixelToUpright(int x, int y, out float uprightX, out float uprightY)
        {
            uprightX = ((x + 0.5f) - PadX) / ContentWidth;
            uprightY = ((y + 0.5f) - PadY) / ContentHeight;
            return uprightX >= 0f && uprightX <= 1f && uprightY >= 0f && uprightY <= 1f;
        }

        /// <summary>Maps a box normalized to the whole tensor into upright-frame normalized coordinates.</summary>
        public NormalizedRect TensorToUpright(NormalizedRect tensorBox)
        {
            var left = ((tensorBox.X * TensorWidth) - PadX) / ContentWidth;
            var top = ((tensorBox.Y * TensorHeight) - PadY) / ContentHeight;
            var right = (((tensorBox.X + tensorBox.Width) * TensorWidth) - PadX) / ContentWidth;
            var bottom = (((tensorBox.Y + tensorBox.Height) * TensorHeight) - PadY) / ContentHeight;
            left = Clamp01(left);
            top = Clamp01(top);
            right = Clamp01(right);
            bottom = Clamp01(bottom);
            return new NormalizedRect(left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
