using System;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Maps between the raw camera texture (top-origin normalized coordinates, as used by
    /// detections, overlays and crops) and the upright frame the operator sees.
    /// The view shows the texture vertically mirrored (when requested) and then rotated
    /// clockwise by the camera's videoRotationAngle; this type applies the same convention.
    /// </summary>
    public readonly struct CameraFrameOrientation
    {
        public CameraFrameOrientation(int rotationAngle, bool verticallyMirrored)
        {
            QuarterTurnsClockwise = ToQuarterTurns(rotationAngle);
            VerticallyMirrored = verticallyMirrored;
        }

        public int QuarterTurnsClockwise { get; }
        public bool VerticallyMirrored { get; }
        public bool SwapsAxes => (QuarterTurnsClockwise & 1) == 1;
        public bool IsIdentity => QuarterTurnsClockwise == 0 && !VerticallyMirrored;

        public static int ToQuarterTurns(int rotationAngle)
        {
            var normalized = rotationAngle % 360;
            if (normalized < 0)
                normalized += 360;
            if (normalized < 45 || normalized >= 315)
                return 0;
            if (normalized < 135)
                return 1;
            return normalized < 225 ? 2 : 3;
        }

        /// <summary>Maps an upright point to the raw texture, both top-origin and normalized.</summary>
        public void UprightToTexture(float uprightX, float uprightY, out float textureX, out float textureY)
        {
            switch (QuarterTurnsClockwise)
            {
                case 1:
                    textureX = uprightY;
                    textureY = 1f - uprightX;
                    break;
                case 2:
                    textureX = 1f - uprightX;
                    textureY = 1f - uprightY;
                    break;
                case 3:
                    textureX = 1f - uprightY;
                    textureY = uprightX;
                    break;
                default:
                    textureX = uprightX;
                    textureY = uprightY;
                    break;
            }

            if (VerticallyMirrored)
                textureY = 1f - textureY;
        }

        /// <summary>Maps a raw texture point to the upright frame, both top-origin and normalized.</summary>
        public void TextureToUpright(float textureX, float textureY, out float uprightX, out float uprightY)
        {
            if (VerticallyMirrored)
                textureY = 1f - textureY;

            switch (QuarterTurnsClockwise)
            {
                case 1:
                    uprightX = 1f - textureY;
                    uprightY = textureX;
                    break;
                case 2:
                    uprightX = 1f - textureX;
                    uprightY = 1f - textureY;
                    break;
                case 3:
                    uprightX = textureY;
                    uprightY = 1f - textureX;
                    break;
                default:
                    uprightX = textureX;
                    uprightY = textureY;
                    break;
            }
        }

        public NormalizedRect TextureToUpright(NormalizedRect texture)
        {
            TextureToUpright(texture.X, texture.Y, out var ax, out var ay);
            TextureToUpright(texture.X + texture.Width, texture.Y + texture.Height, out var bx, out var by);
            var left = Math.Min(ax, bx);
            var top = Math.Min(ay, by);
            return new NormalizedRect(left, top, Math.Abs(bx - ax), Math.Abs(by - ay));
        }

        public NormalizedRect UprightToTexture(NormalizedRect upright)
        {
            UprightToTexture(upright.X, upright.Y, out var ax, out var ay);
            UprightToTexture(upright.X + upright.Width, upright.Y + upright.Height, out var bx, out var by);
            var left = Math.Min(ax, bx);
            var top = Math.Min(ay, by);
            return new NormalizedRect(left, top, Math.Abs(bx - ax), Math.Abs(by - ay));
        }
    }
}
