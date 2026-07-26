using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class ArabicUiTextFormatterTests
    {
        [Test]
        public void Format_LeavesEnglishTextUnchanged()
        {
            Assert.That(ArabicUiTextFormatter.Format("Vision Inventory Count"), Is.EqualTo("Vision Inventory Count"));
        }

        [Test]
        public void Format_ShapesArabicIntoPresentationForms()
        {
            var formatted = ArabicUiTextFormatter.Format("عداد المخزون الذكي");

            Assert.That(formatted, Is.Not.EqualTo("عداد المخزون الذكي"));
            Assert.That(ContainsPresentationForm(formatted), Is.True);
            Assert.That(ContainsBaseArabicLetter(formatted), Is.False);
        }

        [Test]
        public void Format_UsesLamAlefLigature()
        {
            Assert.That(ArabicUiTextFormatter.Format("لا"), Is.EqualTo("\uFEFB"));
        }

        [Test]
        public void Format_PreservesEnglishRunInsideMixedText()
        {
            var formatted = ArabicUiTextFormatter.Format("فتح لوحة الإدارة / Unlock");

            StringAssert.StartsWith("Unlock", formatted);
            StringAssert.Contains(" / ", formatted);
            Assert.That(formatted, Does.Not.Contain("kcolnU"));
        }

        [Test]
        public void Format_PreservesTechnicalTokensAndNumbers()
        {
            var formatted = ArabicUiTextFormatter.Format("أدخل SHA-256 و manifest.json خلال 12 ثانية");

            StringAssert.Contains("SHA-256", formatted);
            StringAssert.Contains("manifest.json", formatted);
            StringAssert.Contains("12", formatted);
        }

        [Test]
        public void Format_IsIdempotentForAlreadyFormattedText()
        {
            var first = ArabicUiTextFormatter.Format("تم حفظ الجلسة");
            var second = ArabicUiTextFormatter.Format(first);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Format_PreservesLineBreaks()
        {
            var formatted = ArabicUiTextFormatter.Format("السطر الأول\nالسطر الثاني");

            Assert.That(formatted.Split('\n').Length, Is.EqualTo(2));
        }

        private static bool ContainsPresentationForm(string value)
        {
            foreach (var character in value)
            {
                if (character >= '\uFB50' && character <= '\uFDFF' ||
                    character >= '\uFE70' && character <= '\uFEFF')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsBaseArabicLetter(string value)
        {
            foreach (var character in value)
            {
                if (character >= '\u0621' && character <= '\u064A')
                    return true;
            }
            return false;
        }
    }
}
