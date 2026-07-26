using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Converts logical Arabic text into visual-order Arabic presentation forms for UnityEngine.UI.Text.
    /// Unity's legacy Text component does not perform Arabic shaping or bidirectional layout on Android.
    /// </summary>
    public static class ArabicUiTextFormatter
    {
        private enum Direction
        {
            Neutral,
            LeftToRight,
            RightToLeft
        }

        private readonly struct Forms
        {
            public Forms(char isolated, char final, char initial, char medial)
            {
                Isolated = isolated;
                Final = final;
                Initial = initial;
                Medial = medial;
            }

            public char Isolated { get; }
            public char Final { get; }
            public char Initial { get; }
            public char Medial { get; }
            public bool ConnectsToPrevious => Final != '\0';
            public bool ConnectsToNext => Initial != '\0';

            public char Select(bool joinsPrevious, bool joinsNext)
            {
                if (joinsPrevious && joinsNext && Medial != '\0')
                    return Medial;
                if (joinsPrevious && Final != '\0')
                    return Final;
                if (joinsNext && Initial != '\0')
                    return Initial;
                return Isolated;
            }
        }

        private sealed class GlyphUnit
        {
            public Forms Forms;
            public bool HasForms;
            public char Literal;
            public readonly StringBuilder Marks = new StringBuilder();
        }

        private sealed class DirectionalRun
        {
            public Direction Direction;
            public readonly StringBuilder Text = new StringBuilder();
        }

        private static readonly Dictionary<char, Forms> ArabicForms = new Dictionary<char, Forms>
        {
            ['\u0621'] = new Forms('\uFE80', '\0', '\0', '\0'),
            ['\u0622'] = new Forms('\uFE81', '\uFE82', '\0', '\0'),
            ['\u0623'] = new Forms('\uFE83', '\uFE84', '\0', '\0'),
            ['\u0624'] = new Forms('\uFE85', '\uFE86', '\0', '\0'),
            ['\u0625'] = new Forms('\uFE87', '\uFE88', '\0', '\0'),
            ['\u0626'] = new Forms('\uFE89', '\uFE8A', '\uFE8B', '\uFE8C'),
            ['\u0627'] = new Forms('\uFE8D', '\uFE8E', '\0', '\0'),
            ['\u0628'] = new Forms('\uFE8F', '\uFE90', '\uFE91', '\uFE92'),
            ['\u0629'] = new Forms('\uFE93', '\uFE94', '\0', '\0'),
            ['\u062A'] = new Forms('\uFE95', '\uFE96', '\uFE97', '\uFE98'),
            ['\u062B'] = new Forms('\uFE99', '\uFE9A', '\uFE9B', '\uFE9C'),
            ['\u062C'] = new Forms('\uFE9D', '\uFE9E', '\uFE9F', '\uFEA0'),
            ['\u062D'] = new Forms('\uFEA1', '\uFEA2', '\uFEA3', '\uFEA4'),
            ['\u062E'] = new Forms('\uFEA5', '\uFEA6', '\uFEA7', '\uFEA8'),
            ['\u062F'] = new Forms('\uFEA9', '\uFEAA', '\0', '\0'),
            ['\u0630'] = new Forms('\uFEAB', '\uFEAC', '\0', '\0'),
            ['\u0631'] = new Forms('\uFEAD', '\uFEAE', '\0', '\0'),
            ['\u0632'] = new Forms('\uFEAF', '\uFEB0', '\0', '\0'),
            ['\u0633'] = new Forms('\uFEB1', '\uFEB2', '\uFEB3', '\uFEB4'),
            ['\u0634'] = new Forms('\uFEB5', '\uFEB6', '\uFEB7', '\uFEB8'),
            ['\u0635'] = new Forms('\uFEB9', '\uFEBA', '\uFEBB', '\uFEBC'),
            ['\u0636'] = new Forms('\uFEBD', '\uFEBE', '\uFEBF', '\uFEC0'),
            ['\u0637'] = new Forms('\uFEC1', '\uFEC2', '\uFEC3', '\uFEC4'),
            ['\u0638'] = new Forms('\uFEC5', '\uFEC6', '\uFEC7', '\uFEC8'),
            ['\u0639'] = new Forms('\uFEC9', '\uFECA', '\uFECB', '\uFECC'),
            ['\u063A'] = new Forms('\uFECD', '\uFECE', '\uFECF', '\uFED0'),
            ['\u0640'] = new Forms('\u0640', '\u0640', '\u0640', '\u0640'),
            ['\u0641'] = new Forms('\uFED1', '\uFED2', '\uFED3', '\uFED4'),
            ['\u0642'] = new Forms('\uFED5', '\uFED6', '\uFED7', '\uFED8'),
            ['\u0643'] = new Forms('\uFED9', '\uFEDA', '\uFEDB', '\uFEDC'),
            ['\u0644'] = new Forms('\uFEDD', '\uFEDE', '\uFEDF', '\uFEE0'),
            ['\u0645'] = new Forms('\uFEE1', '\uFEE2', '\uFEE3', '\uFEE4'),
            ['\u0646'] = new Forms('\uFEE5', '\uFEE6', '\uFEE7', '\uFEE8'),
            ['\u0647'] = new Forms('\uFEE9', '\uFEEA', '\uFEEB', '\uFEEC'),
            ['\u0648'] = new Forms('\uFEED', '\uFEEE', '\0', '\0'),
            ['\u0649'] = new Forms('\uFEEF', '\uFEF0', '\0', '\0'),
            ['\u064A'] = new Forms('\uFEF1', '\uFEF2', '\uFEF3', '\uFEF4'),
            ['\u067E'] = new Forms('\uFB56', '\uFB57', '\uFB58', '\uFB59'),
            ['\u0686'] = new Forms('\uFB7A', '\uFB7B', '\uFB7C', '\uFB7D'),
            ['\u0698'] = new Forms('\uFB8A', '\uFB8B', '\0', '\0'),
            ['\u06A9'] = new Forms('\uFB8E', '\uFB8F', '\uFB90', '\uFB91'),
            ['\u06AF'] = new Forms('\uFB92', '\uFB93', '\uFB94', '\uFB95'),
            ['\u06CC'] = new Forms('\uFBFC', '\uFBFD', '\uFBFE', '\uFBFF')
        };

        public static bool ContainsArabic(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                if (IsArabicCharacter(value[index]))
                    return true;
            }

            return false;
        }

        public static string Format(string value)
        {
            if (string.IsNullOrEmpty(value) || !ContainsArabic(value))
                return value ?? string.Empty;

            // Avoid applying the transformation twice when a Text component is scanned repeatedly.
            if (!ContainsBaseArabic(value) && ContainsArabicPresentationForm(value))
                return value;

            var output = new StringBuilder(value.Length * 2);
            var lineStart = 0;
            for (var index = 0; index <= value.Length; index++)
            {
                if (index < value.Length && value[index] != '\r' && value[index] != '\n')
                    continue;

                output.Append(FormatLine(value.Substring(lineStart, index - lineStart)));
                if (index < value.Length)
                {
                    if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                    {
                        output.Append("\r\n");
                        index++;
                    }
                    else
                    {
                        output.Append(value[index]);
                    }
                }
                lineStart = index + 1;
            }

            return output.ToString();
        }

        private static string FormatLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !ContainsArabic(line))
                return line ?? string.Empty;

            var rawDirections = new Direction[line.Length];
            var leftStrong = new Direction[line.Length];
            var rightStrong = new Direction[line.Length];

            var last = Direction.Neutral;
            for (var index = 0; index < line.Length; index++)
            {
                rawDirections[index] = GetStrongDirection(line[index]);
                if (rawDirections[index] != Direction.Neutral)
                    last = rawDirections[index];
                leftStrong[index] = last;
            }

            last = Direction.Neutral;
            for (var index = line.Length - 1; index >= 0; index--)
            {
                if (rawDirections[index] != Direction.Neutral)
                    last = rawDirections[index];
                rightStrong[index] = last;
            }

            var runs = new List<DirectionalRun>();
            for (var index = 0; index < line.Length; index++)
            {
                var direction = rawDirections[index];
                if (direction == Direction.Neutral)
                {
                    var left = leftStrong[index];
                    var right = rightStrong[index];
                    if (left != Direction.Neutral && left == right)
                        direction = left;
                    else if (left == Direction.Neutral)
                        direction = right;
                    else if (right == Direction.Neutral)
                        direction = left;
                }

                if (runs.Count == 0 || runs[runs.Count - 1].Direction != direction)
                    runs.Add(new DirectionalRun { Direction = direction });
                runs[runs.Count - 1].Text.Append(line[index]);
            }

            var result = new StringBuilder(line.Length * 2);
            for (var index = runs.Count - 1; index >= 0; index--)
            {
                var run = runs[index];
                result.Append(run.Direction == Direction.RightToLeft
                    ? ShapeAndReverse(run.Text.ToString())
                    : run.Text.ToString());
            }

            return result.ToString();
        }

        private static string ShapeAndReverse(string logicalRun)
        {
            var units = new List<GlyphUnit>();
            for (var index = 0; index < logicalRun.Length; index++)
            {
                var character = logicalRun[index];
                if (IsCombiningMark(character) && units.Count > 0)
                {
                    units[units.Count - 1].Marks.Append(character);
                    continue;
                }

                if (character == '\u0644' && index + 1 < logicalRun.Length &&
                    TryGetLamAlefForms(logicalRun[index + 1], out var ligatureForms))
                {
                    units.Add(new GlyphUnit { HasForms = true, Forms = ligatureForms });
                    index++;
                    continue;
                }

                if (ArabicForms.TryGetValue(character, out var forms))
                    units.Add(new GlyphUnit { HasForms = true, Forms = forms });
                else
                    units.Add(new GlyphUnit { Literal = character });
            }

            var shaped = new char[units.Count];
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (!unit.HasForms)
                {
                    shaped[index] = unit.Literal;
                    continue;
                }

                var joinsPrevious = index > 0 && units[index - 1].HasForms &&
                                    units[index - 1].Forms.ConnectsToNext && unit.Forms.ConnectsToPrevious;
                var joinsNext = index + 1 < units.Count && units[index + 1].HasForms &&
                                unit.Forms.ConnectsToNext && units[index + 1].Forms.ConnectsToPrevious;
                shaped[index] = unit.Forms.Select(joinsPrevious, joinsNext);
            }

            var result = new StringBuilder(logicalRun.Length * 2);
            for (var index = units.Count - 1; index >= 0; index--)
            {
                result.Append(shaped[index]);
                result.Append(units[index].Marks);
            }
            return result.ToString();
        }

        private static Direction GetStrongDirection(char character)
        {
            if (IsArabicCharacter(character))
                return Direction.RightToLeft;

            var category = char.GetUnicodeCategory(character);
            if (char.IsLetterOrDigit(character) ||
                category == UnicodeCategory.DecimalDigitNumber ||
                category == UnicodeCategory.LetterNumber ||
                category == UnicodeCategory.OtherNumber)
            {
                return Direction.LeftToRight;
            }

            return Direction.Neutral;
        }

        private static bool IsArabicCharacter(char character)
        {
            return character >= '\u0600' && character <= '\u06FF' ||
                   character >= '\u0750' && character <= '\u077F' ||
                   character >= '\u08A0' && character <= '\u08FF' ||
                   character >= '\uFB50' && character <= '\uFDFF' ||
                   character >= '\uFE70' && character <= '\uFEFF';
        }

        private static bool ContainsBaseArabic(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character >= '\u0600' && character <= '\u06FF' ||
                    character >= '\u0750' && character <= '\u077F' ||
                    character >= '\u08A0' && character <= '\u08FF')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsArabicPresentationForm(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character >= '\uFB50' && character <= '\uFDFF' ||
                    character >= '\uFE70' && character <= '\uFEFF')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsCombiningMark(char character)
        {
            var category = char.GetUnicodeCategory(character);
            return category == UnicodeCategory.NonSpacingMark ||
                   category == UnicodeCategory.SpacingCombiningMark ||
                   category == UnicodeCategory.EnclosingMark;
        }

        private static bool TryGetLamAlefForms(char alef, out Forms forms)
        {
            switch (alef)
            {
                case '\u0622':
                    forms = new Forms('\uFEF5', '\uFEF6', '\0', '\0');
                    return true;
                case '\u0623':
                    forms = new Forms('\uFEF7', '\uFEF8', '\0', '\0');
                    return true;
                case '\u0625':
                    forms = new Forms('\uFEF9', '\uFEFA', '\0', '\0');
                    return true;
                case '\u0627':
                    forms = new Forms('\uFEFB', '\uFEFC', '\0', '\0');
                    return true;
                default:
                    forms = default;
                    return false;
            }
        }
    }
}
