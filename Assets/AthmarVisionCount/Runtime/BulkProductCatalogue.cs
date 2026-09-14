using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AthmarLabs.VisionCount
{
    public sealed class BulkProductDefinition
    {
        public BulkProductDefinition(
            string sku,
            string nameEnglish,
            string nameArabic,
            string barcode,
            IReadOnlyList<string> ocrAliases,
            bool active)
        {
            Sku = sku ?? string.Empty;
            NameEnglish = nameEnglish ?? string.Empty;
            NameArabic = nameArabic ?? string.Empty;
            Barcode = barcode ?? string.Empty;
            OcrAliases = ocrAliases ?? Array.Empty<string>();
            Active = active;
        }

        public string Sku { get; }
        public string NameEnglish { get; }
        public string NameArabic { get; }
        public string Barcode { get; }
        public IReadOnlyList<string> OcrAliases { get; }
        public bool Active { get; }

        public string GetDisplayName(string language)
        {
            if (string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(NameArabic))
                return NameArabic;
            if (!string.IsNullOrWhiteSpace(NameEnglish))
                return NameEnglish;
            return Sku;
        }
    }

    /// <summary>
    /// Customer-specific product metadata for the generic visual recognizer. This catalogue is data,
    /// not model code, and can scale independently from the detector label catalogue.
    /// </summary>
    public sealed class BulkProductCatalogue
    {
        public const int MaximumProductCount = 100000;

        private readonly Dictionary<string, BulkProductDefinition> _bySku;
        private readonly Dictionary<string, BulkProductDefinition> _byBarcode;
        private readonly Dictionary<string, string> _ocrAliasToSku;
        private readonly List<BulkProductDefinition> _entries;

        private BulkProductCatalogue(
            Dictionary<string, BulkProductDefinition> bySku,
            Dictionary<string, BulkProductDefinition> byBarcode,
            Dictionary<string, string> ocrAliasToSku,
            List<BulkProductDefinition> entries)
        {
            _bySku = bySku;
            _byBarcode = byBarcode;
            _ocrAliasToSku = ocrAliasToSku;
            _entries = entries;
        }

        public IReadOnlyList<BulkProductDefinition> Entries => _entries;
        public int Count => _entries.Count;

        public bool TryGetBySku(string sku, out BulkProductDefinition product)
        {
            product = null;
            return !string.IsNullOrWhiteSpace(sku) && _bySku.TryGetValue(sku.Trim(), out product);
        }

        public bool TryGetByBarcode(string barcode, out BulkProductDefinition product)
        {
            product = null;
            var normalized = NormalizeBarcode(barcode);
            return normalized.Length > 0 && _byBarcode.TryGetValue(normalized, out product);
        }

        public bool TryResolveOcrText(string ocrText, out BulkProductDefinition product, out bool ambiguous)
        {
            product = null;
            ambiguous = false;
            var tokens = TokenizeOcr(ocrText);
            if (tokens.Count == 0)
                return false;

            string matchedSku = null;
            var maxWords = Math.Min(4, tokens.Count);
            for (var width = 1; width <= maxWords; width++)
            {
                for (var start = 0; start + width <= tokens.Count; start++)
                {
                    var builder = new StringBuilder();
                    for (var index = 0; index < width; index++)
                        builder.Append(tokens[start + index]);

                    var candidate = builder.ToString();
                    if (candidate.Length < 4 || !_ocrAliasToSku.TryGetValue(candidate, out var sku))
                        continue;
                    if (string.IsNullOrEmpty(sku))
                    {
                        ambiguous = true;
                        return false;
                    }

                    if (matchedSku == null)
                    {
                        matchedSku = sku;
                    }
                    else if (!string.Equals(matchedSku, sku, StringComparison.OrdinalIgnoreCase))
                    {
                        ambiguous = true;
                        return false;
                    }
                }
            }

            return matchedSku != null && _bySku.TryGetValue(matchedSku, out product);
        }

        public static BulkProductCatalogue Parse(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                throw new FormatException("The bulk product catalogue is empty.");

            using var reader = new StringReader(csv);
            var headerLine = ReadDataLine(reader, out var lineNumber);
            if (headerLine == null)
                throw new FormatException("The bulk product catalogue has no header row.");

            var header = ParseCsvLine(headerLine);
            var columns = BuildColumnMap(header);
            RequireColumn(columns, "sku");
            RequireColumn(columns, "name_en");
            RequireColumn(columns, "name_ar");

            var bySku = new Dictionary<string, BulkProductDefinition>(StringComparer.OrdinalIgnoreCase);
            var byBarcode = new Dictionary<string, BulkProductDefinition>(StringComparer.OrdinalIgnoreCase);
            var ocrAliasToSku = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<BulkProductDefinition>();
            var totalProducts = 0;

            while (true)
            {
                var line = reader.ReadLine();
                lineNumber++;
                if (line == null)
                    break;
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                totalProducts++;
                if (totalProducts > MaximumProductCount)
                    throw new FormatException($"The bulk product catalogue exceeds {MaximumProductCount} products.");

                var fields = ParseCsvLine(line);
                var sku = GetField(fields, columns["sku"]).Trim();
                if (string.IsNullOrWhiteSpace(sku) || sku.Length > 128)
                    throw new FormatException($"Invalid SKU on bulk catalogue line {lineNumber}.");

                var nameEnglish = GetField(fields, columns["name_en"]).Trim();
                var nameArabic = GetField(fields, columns["name_ar"]).Trim();
                if (nameEnglish.Length == 0 && nameArabic.Length == 0)
                    throw new FormatException($"At least one product name is required on bulk catalogue line {lineNumber}.");

                var barcode = columns.TryGetValue("barcode", out var barcodeColumn)
                    ? NormalizeBarcode(GetField(fields, barcodeColumn))
                    : string.Empty;
                var active = !columns.TryGetValue("active", out var activeColumn) ||
                             ParseBoolean(GetField(fields, activeColumn), lineNumber);
                var aliases = columns.TryGetValue("ocr_aliases", out var aliasesColumn)
                    ? ParseAliases(GetField(fields, aliasesColumn))
                    : new List<string>();

                if (bySku.ContainsKey(sku))
                    throw new FormatException($"Duplicate SKU '{sku}' on bulk catalogue line {lineNumber}.");
                if (barcode.Length > 0 && byBarcode.ContainsKey(barcode))
                    throw new FormatException($"Duplicate barcode '{barcode}' on bulk catalogue line {lineNumber}.");

                var product = new BulkProductDefinition(sku, nameEnglish, nameArabic, barcode, aliases, active);
                bySku.Add(sku, product);
                if (barcode.Length > 0)
                    byBarcode.Add(barcode, product);
                if (active)
                    entries.Add(product);

                AddOcrAlias(ocrAliasToSku, NormalizeOcrAlias(sku), sku);
                if (barcode.Length > 0)
                    AddOcrAlias(ocrAliasToSku, barcode, sku);
                for (var index = 0; index < aliases.Count; index++)
                    AddOcrAlias(ocrAliasToSku, aliases[index], sku);
            }

            if (entries.Count == 0)
                throw new FormatException("The bulk product catalogue has no active products.");

            entries.Sort((left, right) => string.Compare(left.Sku, right.Sku, StringComparison.OrdinalIgnoreCase));
            return new BulkProductCatalogue(bySku, byBarcode, ocrAliasToSku, entries);
        }

        public static string NormalizeBarcode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToUpperInvariant(character));
            }
            return builder.ToString();
        }

        public static string NormalizeOcrAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }

        private static List<string> TokenizeOcr(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return result;

            var current = new StringBuilder();
            void Flush()
            {
                if (current.Length == 0)
                    return;
                result.Add(current.ToString());
                current.Length = 0;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsLetterOrDigit(character))
                    current.Append(char.ToLowerInvariant(character));
                else
                    Flush();
            }
            Flush();
            return result;
        }

        private static List<string> ParseAliases(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return result;

            var pieces = value.Split('|');
            for (var index = 0; index < pieces.Length; index++)
            {
                var normalized = NormalizeOcrAlias(pieces[index]);
                if (normalized.Length >= 4 && !result.Contains(normalized))
                    result.Add(normalized);
            }
            return result;
        }

        private static void AddOcrAlias(IDictionary<string, string> index, string alias, string sku)
        {
            if (string.IsNullOrEmpty(alias) || alias.Length < 4)
                return;
            if (!index.TryGetValue(alias, out var existing))
            {
                index.Add(alias, sku);
                return;
            }
            if (!string.Equals(existing, sku, StringComparison.OrdinalIgnoreCase))
                index[alias] = string.Empty;
        }

        private static string ReadDataLine(StringReader reader, out int lineNumber)
        {
            lineNumber = 0;
            while (true)
            {
                var line = reader.ReadLine();
                lineNumber++;
                if (line == null)
                    return null;
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    return line;
            }
        }

        private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                var name = header[index].Trim();
                if (name.Length == 0)
                    continue;
                if (result.ContainsKey(name))
                    throw new FormatException($"Duplicate bulk catalogue column '{name}'.");
                result.Add(name, index);
            }
            return result;
        }

        private static void RequireColumn(IReadOnlyDictionary<string, int> columns, string name)
        {
            if (!columns.ContainsKey(name))
                throw new FormatException($"The bulk product catalogue is missing required column '{name}'.");
        }

        private static string GetField(IReadOnlyList<string> fields, int index)
        {
            return index >= 0 && index < fields.Count ? fields[index] : string.Empty;
        }

        private static bool ParseBoolean(string value, int lineNumber)
        {
            value = value == null ? string.Empty : value.Trim();
            if (value.Length == 0 || value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            throw new FormatException($"Invalid active value on bulk catalogue line {lineNumber}.");
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
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
                throw new FormatException("The bulk product catalogue contains an unterminated quoted value.");
            result.Add(current.ToString());
            return result;
        }
    }
}
