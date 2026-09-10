using System;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Converts RGB camera pixels into the float tensor layout expected by CPU inference.
    /// This deliberately avoids Unity Inference Engine TextureConverter on CPU because
    /// package 2.4.1 can leave delayed GPU disposals that crash Worker.Schedule on CPU.
    /// </summary>
    public static class CpuRgbTensorWriter
    {
        public static void ResizeRgb01(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight,
            ModelInputLayout layout,
            float[] destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source and target dimensions must be positive.");
            if (source.Length != checked(sourceWidth * sourceHeight))
                throw new ArgumentException("Source pixel count does not match source dimensions.", nameof(source));
            if (destination.Length != checked(targetWidth * targetHeight * 3))
                throw new ArgumentException("Destination buffer must contain exactly targetWidth * targetHeight * 3 floats.", nameof(destination));

            var scaleX = sourceWidth > 1 && targetWidth > 1
                ? (sourceWidth - 1f) / (targetWidth - 1f)
                : 0f;
            var scaleY = sourceHeight > 1 && targetHeight > 1
                ? (sourceHeight - 1f) / (targetHeight - 1f)
                : 0f;
            var planeSize = targetWidth * targetHeight;

            for (var y = 0; y < targetHeight; y++)
            {
                var sourceY = y * scaleY;
                var y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, sourceHeight - 1);
                var y1 = Mathf.Min(y0 + 1, sourceHeight - 1);
                var fy = sourceY - y0;

                for (var x = 0; x < targetWidth; x++)
                {
                    var sourceX = x * scaleX;
                    var x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, sourceWidth - 1);
                    var x1 = Mathf.Min(x0 + 1, sourceWidth - 1);
                    var fx = sourceX - x0;

                    var c00 = source[(y0 * sourceWidth) + x0];
                    var c10 = source[(y0 * sourceWidth) + x1];
                    var c01 = source[(y1 * sourceWidth) + x0];
                    var c11 = source[(y1 * sourceWidth) + x1];

                    var r = BilinearChannel(c00.r, c10.r, c01.r, c11.r, fx, fy);
                    var g = BilinearChannel(c00.g, c10.g, c01.g, c11.g, fx, fy);
                    var b = BilinearChannel(c00.b, c10.b, c01.b, c11.b, fx, fy);
                    var pixelIndex = (y * targetWidth) + x;

                    if (layout == ModelInputLayout.Nhwc)
                    {
                        var destinationIndex = pixelIndex * 3;
                        destination[destinationIndex] = r;
                        destination[destinationIndex + 1] = g;
                        destination[destinationIndex + 2] = b;
                    }
                    else
                    {
                        destination[pixelIndex] = r;
                        destination[planeSize + pixelIndex] = g;
                        destination[(planeSize * 2) + pixelIndex] = b;
                    }
                }
            }
        }

        private static float BilinearChannel(byte c00, byte c10, byte c01, byte c11, float fx, float fy)
        {
            var lower = Mathf.Lerp(c00, c10, fx);
            var upper = Mathf.Lerp(c01, c11, fx);
            return Mathf.Lerp(lower, upper, fy) / 255f;
        }
    }
}
