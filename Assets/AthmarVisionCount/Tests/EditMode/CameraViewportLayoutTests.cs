using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CameraViewportLayoutTests
    {
        [Test]
        public void RotatedLandscapeSensorKeepsPortraitAspectInsidePanel()
        {
            // Field case: 1280x720 sensor, phone upright (90 degrees), camera panel 855x1000.
            var layout = new CameraViewportLayout(1280, 720, new CameraFrameOrientation(90, false), 855f, 1000f);

            Assert.That(layout.VisualHeight, Is.EqualTo(1000f).Within(0.01f));
            Assert.That(layout.VisualWidth, Is.EqualTo(562.5f).Within(0.01f));
            Assert.That(layout.VisualLeft, Is.EqualTo(146.25f).Within(0.01f));
            Assert.That(layout.UnrotatedWidth, Is.EqualTo(1000f).Within(0.01f), "rect is sized before its 90 degree rotation");
            Assert.That(layout.UnrotatedHeight, Is.EqualTo(562.5f).Within(0.01f));
        }

        [Test]
        public void UnrotatedFrameFitsWidth()
        {
            var layout = new CameraViewportLayout(1280, 720, new CameraFrameOrientation(0, false), 855f, 1000f);

            Assert.That(layout.VisualWidth, Is.EqualTo(855f).Within(0.01f));
            Assert.That(layout.VisualHeight, Is.EqualTo(480.94f).Within(0.01f));
            Assert.That(layout.UnrotatedWidth, Is.EqualTo(layout.VisualWidth).Within(0.01f));
        }

        [Test]
        public void TwoBottlesSideBySideStaySideBySideOnScreen()
        {
            // Upright scene: bottles left and right at the same height. In the 90-degree sensor
            // texture they are stacked vertically (the field bug drew them that way).
            var orientation = new CameraFrameOrientation(90, false);
            var layout = new CameraViewportLayout(1280, 720, orientation, 855f, 1000f);
            var leftBottle = orientation.UprightToTexture(new NormalizedRect(0.1f, 0.4f, 0.25f, 0.3f));
            var rightBottle = orientation.UprightToTexture(new NormalizedRect(0.6f, 0.4f, 0.25f, 0.3f));

            var left = layout.TextureBoxToPanel(leftBottle);
            var right = layout.TextureBoxToPanel(rightBottle);

            Assert.That(left.Y, Is.EqualTo(right.Y).Within(1e-4f), "same height on screen");
            Assert.That(right.X - left.X, Is.EqualTo(0.5f * 562.5f / 855f).Within(1e-4f));
            Assert.That(left.X, Is.EqualTo((146.25f + (0.1f * 562.5f)) / 855f).Within(1e-4f));
            Assert.That(left.Y, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(left.Width, Is.EqualTo(0.25f * 562.5f / 855f).Within(1e-4f));
            Assert.That(left.Height, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [TestCase(0, false)]
        [TestCase(90, false)]
        [TestCase(180, false)]
        [TestCase(270, false)]
        [TestCase(90, true)]
        public void TextureAndUprightRectMappingsAreInverse(int rotation, bool mirrored)
        {
            var orientation = new CameraFrameOrientation(rotation, mirrored);
            var upright = new NormalizedRect(0.2f, 0.3f, 0.4f, 0.1f);

            var back = orientation.TextureToUpright(orientation.UprightToTexture(upright));

            Assert.That(back.X, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(back.Y, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(back.Width, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(back.Height, Is.EqualTo(0.1f).Within(1e-5f));
        }
    }
}
