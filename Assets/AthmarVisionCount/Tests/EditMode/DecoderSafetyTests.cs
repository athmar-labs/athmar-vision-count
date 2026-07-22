using System;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class DecoderSafetyTests
    {
        [TestCase(1.2f)]
        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void DecoderRejectsInvalidClassProbability(float score)
        {
            var catalogue = SkuCatalogue.Parse("label_index,sku,name_en,name_ar\n0,SKU-001,Product,منتج\n");
            var output = new[] { 0.5f, 0.5f, 0.2f, 0.2f, score };

            Assert.Throws<InvalidOperationException>(() => YoloOutputDecoder.Decode(
                output,
                1,
                5,
                DetectionTensorLayout.RowsFirst,
                false,
                true,
                640,
                640,
                0.5f,
                0.45f,
                100,
                catalogue));
        }

        [Test]
        public void DecoderRejectsNonFiniteCoordinate()
        {
            var catalogue = SkuCatalogue.Parse("label_index,sku,name_en,name_ar\n0,SKU-001,Product,منتج\n");
            var output = new[] { float.NaN, 0.5f, 0.2f, 0.2f, 0.9f };

            Assert.Throws<InvalidOperationException>(() => YoloOutputDecoder.Decode(
                output,
                1,
                5,
                DetectionTensorLayout.RowsFirst,
                false,
                true,
                640,
                640,
                0.5f,
                0.45f,
                100,
                catalogue));
        }
    }
}
