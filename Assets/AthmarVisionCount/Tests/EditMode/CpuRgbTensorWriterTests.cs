using NUnit.Framework;
using UnityEngine;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CpuRgbTensorWriterTests
    {
        [Test]
        public void ResizeRgb01WritesNhwcOrder()
        {
            var source = new[]
            {
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255)
            };
            var destination = new float[6];

            CpuRgbTensorWriter.ResizeRgb01(source, 2, 1, 2, 1, ModelInputLayout.Nhwc, destination);

            Assert.That(destination, Is.EqualTo(new[] { 1f, 0f, 0f, 0f, 1f, 0f }).Within(0.0001f));
        }

        [Test]
        public void ResizeRgb01WritesNchwOrder()
        {
            var source = new[]
            {
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255)
            };
            var destination = new float[6];

            CpuRgbTensorWriter.ResizeRgb01(source, 2, 1, 2, 1, ModelInputLayout.Nchw, destination);

            Assert.That(destination, Is.EqualTo(new[] { 1f, 0f, 0f, 1f, 0f, 0f }).Within(0.0001f));
        }

        [Test]
        public void ResizeRgb01RejectsWrongDestinationSize()
        {
            var source = new[] { new Color32(255, 255, 255, 255) };

            Assert.Throws<System.ArgumentException>(() =>
                CpuRgbTensorWriter.ResizeRgb01(source, 1, 1, 1, 1, ModelInputLayout.Nhwc, new float[2]));
        }
    }
}
