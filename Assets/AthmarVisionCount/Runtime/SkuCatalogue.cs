using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AthmarLabs.VisionCount
{
    public sealed class SkuDefinition
    {
        public int LabelIndex { get; }
        public string Sku { get; }
        public string NameEnglish { get; }
        public string NameArabic { get; }
        public bool Active { get; }

        public SkuDefinition(int labelIndex, string sku, string nameEnglish, string nameArabic, bool active)
        {
            LabelIndex = labelIndex;
            Sku = sku;
            NameEnglish = nameEnglish;
            NameArabic = nameArabic;
            Active = active;
        }

        public string GetDisplayName(string language)
        {
            if (string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(NameArabic))
                return NameArabic;
            if (!string.IsNullOrWhiteSpace(NameEnglish))
                return NameEnglish;
            return Sku;
        }
    }

    public sealed class SkuCatalogue
    {
        private readonly Dictionary<int, SkuDefinition> _byLabelIndex;
        private readonly Dictionary<string, SkuDefinition> _bySku;
        private readonly List<SkuDefinition> _entries;

        private SkuCatalogue(
            Dictionary<int, SkuDefinition> byLabelIndex,
            Dictionary<string, SkuDefinition> bySku,
            List<SkuDefinition> entries)
        {
            _byLabelIndex = byLabelIndex;
            _bySku = bySku;
            _entries = entries;
        }

        public IReadOnlyList<SkuDefinition> Entries => _entries;
        public int Count => _entries.Count;

        public bool TryGetByLabelIndex(int labelIndex, out SkuDefinition definition)
        {
            return _byLabelIndex.TryGetValue(labelIndex, out definition);
        }

        public bool TryGetBySku(string sku, out SkuDefinition definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(sku) && _bySku.TryGetValue(sku.Trim(), out definition);
        }

        public string ResolveDisplayName(string sku, string language)
        {
            return TryGetBySku(sku, out var definition) ? definition.GetDisplayName(language) : sku;
        }

        public static SkuCatalogue Parse(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                throw new FormatException("The SKU catalogue is empty.");

            using var reader = new StringReader(csv);
            string headerLine = null;
            var lineNumber = 0;
            while (headerLine == null)
            {
                var candidate = reader.ReadLine();
                lineNumber++;
                if (candidate == null)
                    throw new FormatException("The SKU catalogue has no header row.");
                if (string.IsNullOrWhiteSpace(candidate) || candidate.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;
                headerLine = candidate;
            }

            var header = ParseCsvLine(headerLine);
            var columns = BuildColumnMap(header);
            RequireColumn(columns, "label_index");
            RequireColumn(columns, "sku");
            RequireColumn(columns, "name_en");
            RequireColumn(columns, "name_ar");

            var byLabelIndex = new Dictionary<int, SkuDefinition>();
            var bySku = new Dictionary<string, SkuDefinition>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<SkuDefinition>();

            while (true)
            {
                var line = reader.ReadLine();
                lineNumber++;
                if (line == null)
                    break;
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                var fields = ParseCsvLine(line);
                var labelText = GetField(fields, columns["label_index"]);
                if (!int.TryParse(labelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelIndex) || labelIndex < 0)
                    throw new FormatException($"Invalid label_index on catalogue line {lineNumber}.");

                var sku = GetField(fields, columns["sku"]).Trim();
                if (string.IsNullOrWhiteSpace(sku))
                    throw new FormatException($"Missing SKU on catalogue line {lineNumber}.");

                var nameEnglish = GetField(fields, columns["name_en"]).Trim();
                var nameArabic = GetField(fields, columns["name_ar"]).Trim();
                var active = true;
                if (columns.TryGetValue("active", out var activeColumn))
                    active = ParseBoolean(GetField(fields, activeColumn), lineNumber);

                if (byLabelIndex.ContainsKey(labelIndex))
                    throw new FormatException($"Duplicate label_index {labelIndex} on catalogue line {lineNumber}.");
                if (bySku.ContainsKey(sku))
                    throw new FormatException($"Duplicate SKU '{sku}' on catalogue line {lineNumber}.");

                var definition = new SkuDefinition(labelIndex, sku, nameEnglish, nameArabic, active);
                byLabelIndex.Add(labelIndex, definition);
                bySku.Add(sku, definition);
                if (active)
                    entries.Add(definition);
            }

            if (entries.Count == 0)
                throw new FormatException("The SKU catalogue has no active products.");

            entries.Sort((left, right) => left.LabelIndex.CompareTo(right.LabelIndex));
            return new SkuCatalogue(byLabelIndex, bySku, entries);
        }

        private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                var name = header[index].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (result.ContainsKey(name))
                    throw new FormatException($"Duplicate catalogue column '{name}'.");
                result.Add(name, index);
            }
            return result;
        }

        private static void RequireColumn(IReadOnlyDictionary<string, int> columns, string name)
        {
            if (!columns.ContainsKey(name))
                throw new FormatException($"The SKU catalogue is missing required column '{name}'.");
        }

        private static string GetField(IReadOnlyList<string> fields, int index)
        {
            return index >= 0 && index < fields.Count ? fields[index] : string.Empty;
        }

        private static bool ParseBoolean(string value, int lineNumber)
        {
            value = value == null ? string.Empty : value.Trim();
            if (string.IsNullOrEmpty(value) || value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            throw new FormatException($"Invalid active value on catalogue line {lineNumber}.");
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            var quoted = false;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                    continue;
                }

                if (character == ',' && !quoted)
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                current.Append(character);
            }

            if (quoted)
                throw new FormatException("The SKU catalogue contains an unterminated quoted value.");

            result.Add(current.ToString());
            return result;
        }
    }
}
