using NUnit.Framework;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class TensorLetterboxTests
    {
        [Test]
        public void PortraitFrameIsCentredWithSidePadding()
        {
            var letterbox = new TensorLetterbox(720, 1280, 640, 640);

            Assert.That(letterbox.ContentHeight, Is.EqualTo(640f).Within(1e-3f));
            Assert.That(letterbox.ContentWidth, Is.EqualTo(360f).Within(1e-3f));
            Assert.That(letterbox.PadX, Is.EqualTo(140f).Within(1e-3f));
            Assert.That(letterbox.PadY, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void RotatedLandscapeSensorUsesPortraitUprightSize()
        {
            var letterbox = TensorLetterbox.ForCamera(1280, 720, new CameraFrameOrientation(90, false), 640, 640);

            Assert.That(letterbox.ContentWidth, Is.EqualTo(360f).Within(1e-3f));
            Assert.That(letterbox.ContentHeight, Is.EqualTo(640f).Within(1e-3f));
        }

        [Test]
        public void TensorBoxMapsBackToUprightFrame()
        {
            var letterbox = new TensorLetterbox(720, 1280, 640, 640);
            // Upright box x 0.25..0.75, y 0.5..1.0 sits at tensor x 230..410, y 320..640.
            var tensorBox = new NormalizedRect(230f / 640f, 0.5f, 180f / 640f, 0.5f);

            var upright = letterbox.TensorToUpright(tensorBox);

            Assert.That(upright.X, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(upright.Y, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(upright.Width, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(upright.Height, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void PaddingPixelsAreGreyAndContentIsNotStretched()
        {
            // 2x4 bottom-origin portrait source, all white.
            var source = new Color32[8];
            for (var i = 0; i < source.Length; i++)
                source[i] = new Color32(255, 255, 255, 255);
            var destination = new float[8 * 8 * 3];
            var letterbox = new TensorLetterbox(2, 4, 8, 8);

            CpuRgbTensorWriter.WriteUprightRgb01(
                source, 2, 4, new CameraFrameOrientation(0, false), 8, 8, ModelInputLayout.Nchw, destination, letterbox);

            Assert.That(destination[0], Is.EqualTo(TensorLetterbox.PadValue).Within(1e-5f), "left padding");
            Assert.That(destination[7], Is.EqualTo(TensorLetterbox.PadValue).Within(1e-5f), "right padding");
            Assert.That(destination[3], Is.EqualTo(1f).Within(1e-5f), "content column");
            Assert.That(destination[4], Is.EqualTo(1f).Within(1e-5f), "content column");
        }

        [TestCase(0)]
        [TestCase(90)]
        [TestCase(270)]
        public void ProductPositionSurvivesTensorRoundTrip(int rotation)
        {
            // Landscape sensor frame with a red block at known texture coordinates (top-origin).
            const int width = 64, height = 36;
            var source = new Color32[width * height];
            for (var row = 0; row < height; row++)
            {
                var textureY = 1f - ((row + 0.5f) / height);
                for (var column = 0; column < width; column++)
                {
                    var textureX = (column + 0.5f) / width;
                    var inside = textureX > 0.25f && textureX < 0.5f && textureY > 0.5f && textureY < 0.75f;
                    source[(row * width) + column] = inside ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 0, 255);
                }
            }

            var orientation = new CameraFrameOrientation(rotation, false);
            var letterbox = TensorLetterbox.ForCamera(width, height, orientation, 64, 64);
            var tensor = new float[64 * 64 * 3];
            CpuRgbTensorWriter.WriteUprightRgb01(
                source, width, height, orientation, 64, 64, ModelInputLayout.Nchw, tensor, letterbox);

            int minX = 64, minY = 64, maxX = -1, maxY = -1;
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var index = (y * 64) + x;
                    if (tensor[index] > 0.9f && tensor[(64 * 64) + index] < 0.1f)
                    {
                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
            }

            Assert.That(maxX, Is.GreaterThanOrEqualTo(0), "red block must appear in the tensor");
            var tensorBox = new NormalizedRect(minX / 64f, minY / 64f, (maxX + 1 - minX) / 64f, (maxY + 1 - minY) / 64f);
            var texture = orientation.UprightToTexture(letterbox.TensorToUpright(tensorBox));

            Assert.That(texture.X, Is.EqualTo(0.25f).Within(0.05f));
            Assert.That(texture.Y, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(texture.Width, Is.EqualTo(0.25f).Within(0.05f));
            Assert.That(texture.Height, Is.EqualTo(0.25f).Within(0.05f));
        }
    }
}
