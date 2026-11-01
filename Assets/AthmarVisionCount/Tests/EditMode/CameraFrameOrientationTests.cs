using NUnit.Framework;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CameraFrameOrientationTests
    {
        [TestCase(0, 0)]
        [TestCase(90, 1)]
        [TestCase(180, 2)]
        [TestCase(270, 3)]
        [TestCase(-90, 3)]
        [TestCase(450, 1)]
        public void RotationAngleMapsToQuarterTurns(int angle, int expected)
        {
            Assert.That(CameraFrameOrientation.ToQuarterTurns(angle), Is.EqualTo(expected));
        }

        [TestCase(0, false)]
        [TestCase(90, false)]
        [TestCase(180, false)]
        [TestCase(270, false)]
        [TestCase(90, true)]
        [TestCase(270, true)]
        public void UprightAndTextureMappingsAreInverse(int angle, bool mirrored)
        {
            var orientation = new CameraFrameOrientation(angle, mirrored);
            orientation.UprightToTexture(0.2f, 0.7f, out var tx, out var ty);
            orientation.TextureToUpright(tx, ty, out var ux, out var uy);

            Assert.That(ux, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(uy, Is.EqualTo(0.7f).Within(1e-5f));
        }

        [Test]
        public void ClockwiseQuarterTurnMovesUprightTopLeftToTextureBottomLeft()
        {
            // Displaying the texture rotated 90° clockwise puts texture bottom-left at upright top-left.
            var orientation = new CameraFrameOrientation(90, false);
            orientation.UprightToTexture(0f, 0f, out var tx, out var ty);

            Assert.That(tx, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ty, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void UprightRectMapsToTextureRect()
        {
            var orientation = new CameraFrameOrientation(90, false);
            var texture = orientation.UprightToTexture(new NormalizedRect(0.1f, 0.2f, 0.3f, 0.4f));

            Assert.That(texture.X, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(texture.Y, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(texture.Width, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(texture.Height, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void UprightWriterFlipsBottomOriginRowsWithoutRotation()
        {
            // 1x2 bottom-origin source: row 0 (bottom) is black, row 1 (top) is white.
            var source = new[] { new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255) };
            var destination = new float[2 * 3];

            CpuRgbTensorWriter.WriteUprightRgb01(
                source, 1, 2, new CameraFrameOrientation(0, false), 1, 2, ModelInputLayout.Nchw, destination);

            Assert.That(destination[0], Is.EqualTo(1f).Within(1e-5f), "top tensor row must be the top of the image");
            Assert.That(destination[1], Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void UprightWriterRotatesLandscapeSensorFrameToPortrait()
        {
            // 2x1 bottom-origin source: left pixel red, right pixel blue. Rotated 90° clockwise,
            // the left edge of the sensor becomes the top of the upright frame.
            var source = new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) };
            var destination = new float[2 * 3];

            CpuRgbTensorWriter.WriteUprightRgb01(
                source, 2, 1, new CameraFrameOrientation(90, false), 1, 2, ModelInputLayout.Nhwc, destination);

            Assert.That(destination[0], Is.EqualTo(1f).Within(1e-5f), "top pixel red");
            Assert.That(destination[2], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(destination[3], Is.EqualTo(0f).Within(1e-5f), "bottom pixel blue");
            Assert.That(destination[5], Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
