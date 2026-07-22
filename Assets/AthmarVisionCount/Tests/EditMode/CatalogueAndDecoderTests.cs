using System;
using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CatalogueAndDecoderTests
    {
        [Test]
        public void CatalogueParsesQuotedBilingualValuesAndInactiveRows()
        {
            const string csv =
                "label_index,sku,name_en,name_ar,active\n" +
                "0,SKU-001,\"Apple, Red\",تفاح أحمر,true\n" +
                "1,SKU-002,Banana,موز,false\n";

            var catalogue = SkuCatalogue.Parse(csv);

            Assert.That(catalogue.Count, Is.EqualTo(1));
            Assert.That(catalogue.TryGetByLabelIndex(0, out var active), Is.True);
            Assert.That(active.GetDisplayName("en"), Is.EqualTo("Apple, Red"));
            Assert.That(active.GetDisplayName("ar"), Is.EqualTo("تفاح أحمر"));
            Assert.That(catalogue.TryGetByLabelIndex(1, out var inactive), Is.True);
            Assert.That(inactive.Active, Is.False);
        }

        [Test]
        public void CatalogueRejectsDuplicateLabelsAndSkus()
        {
            const string duplicateLabel =
                "label_index,sku,name_en,name_ar\n" +
                "0,SKU-001,One,واحد\n" +
                "0,SKU-002,Two,اثنان\n";
            const string duplicateSku =
                "label_index,sku,name_en,name_ar\n" +
                "0,SKU-001,One,واحد\n" +
                "1,SKU-001,Two,اثنان\n";

            Assert.Throws<FormatException>(() => SkuCatalogue.Parse(duplicateLabel));
            Assert.Throws<FormatException>(() => SkuCatalogue.Parse(duplicateSku));
        }

        [Test]
        public void ChannelsFirstDecoderAppliesConfidenceCatalogueAndNms()
        {
            var catalogue = SkuCatalogue.Parse(
                "label_index,sku,name_en,name_ar\n" +
                "0,SKU-001,Apple,تفاح\n" +
                "1,SKU-002,Banana,موز\n");

            var output = new[]
            {
                320f, 322f,
                320f, 322f,
                100f, 100f,
                100f, 100f,
                0.90f, 0.80f,
                0.10f, 0.20f
            };

            var detections = YoloOutputDecoder.Decode(
                output,
                6,
                2,
                DetectionTensorLayout.ChannelsFirst,
                false,
                false,
                640,
                640,
                0.50f,
                0.45f,
                100,
                catalogue);

            Assert.That(detections, Has.Count.EqualTo(1));
            Assert.That(detections[0].Sku, Is.EqualTo("SKU-001"));
            Assert.That(detections[0].Confidence, Is.EqualTo(0.90f).Within(0.0001f));
            Assert.That(detections[0].Bounds.Width, Is.EqualTo(100f / 640f).Within(0.0001f));
        }

        [Test]
        public void RowsFirstDecoderUsesObjectnessAndRejectsInactiveClass()
        {
            var catalogue = SkuCatalogue.Parse(
                "label_index,sku,name_en,name_ar,active\n" +
                "0,SKU-001,Apple,تفاح,true\n" +
                "1,SKU-002,Banana,موز,false\n");

            var output = new[]
            {
                0.5f, 0.5f, 0.2f, 0.2f, 0.8f, 0.9f, 0.1f,
                0.4f, 0.4f, 0.2f, 0.2f, 0.9f, 0.1f, 0.95f
            };

            var detections = YoloOutputDecoder.Decode(
                output,
                2,
                7,
                DetectionTensorLayout.RowsFirst,
                true,
                true,
                640,
                640,
                0.50f,
                0.45f,
                100,
                catalogue);

            Assert.That(detections, Has.Count.EqualTo(1));
            Assert.That(detections[0].Sku, Is.EqualTo("SKU-001"));
            Assert.That(detections[0].Confidence, Is.EqualTo(0.72f).Within(0.0001f));
        }

        [Test]
        public void DecoderRejectsMismatchedTensorLength()
        {
            var catalogue = SkuCatalogue.Parse("label_index,sku,name_en,name_ar\n0,SKU-001,Apple,تفاح\n");

            Assert.Throws<ArgumentException>(() => YoloOutputDecoder.Decode(
                new float[3],
                5,
                5,
                DetectionTensorLayout.ChannelsFirst,
                false,
                false,
                640,
                640,
                0.5f,
                0.45f,
                100,
                catalogue));
        }
    }
}
