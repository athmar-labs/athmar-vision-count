using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    [Serializable]
    public sealed class ProductReferenceEmbedding
    {
        public string capturedAtUtc;
        public float[] values;
    }

    [Serializable]
    public sealed class ProductEnrollmentRecord
    {
        public string sku;
        public string nameEnglish;
        public string nameArabic;
        public string barcode;
        public bool active = true;
        public string updatedAtUtc;
        public List<ProductReferenceEmbedding> references = new List<ProductReferenceEmbedding>();
    }

    [Serializable]
    public sealed class ProductEnrollmentCatalogueData
    {
        public int schemaVersion = 1;
        public string customerCode;
        public List<ProductEnrollmentRecord> products = new List<ProductEnrollmentRecord>();
    }

    public sealed class ProductEnrollmentDraft
    {
        public string Sku { get; }
        public string NameEnglish { get; }
        public string NameArabic { get; }
        public string Barcode { get; }

        public ProductEnrollmentDraft(string sku, string nameEnglish, string nameArabic, string barcode)
        {
            Sku = sku;
            NameEnglish = nameEnglish;
            NameArabic = nameArabic;
            Barcode = barcode;
        }
    }

    public sealed class ProductEnrollmentStore
    {
        public const int MinimumReferenceCount = 8;
        public const int MaximumReferenceCount = 15;
        public const int MaximumProductCount = 1000;

        private const int SchemaVersion = 1;
        private const string CatalogueFileName = "catalogue.json";
        private readonly string _customerCode;
        private readonly string _customerDirectory;
        private readonly string _cataloguePath;

        public ProductEnrollmentStore(string customerCode, string rootDirectory = null)
        {
            _customerCode = CustomerStorageScope.Require(customerCode);
            var root = ResolveRootDirectory(rootDirectory);
            _customerDirectory = Path.Combine(root, CustomerStorageScope.StorageKey(_customerCode));
            _cataloguePath = Path.Combine(_customerDirectory, CatalogueFileName);
        }

        public string CustomerCode => _customerCode;
        public string CustomerDirectory => _customerDirectory;

        public ProductEnrollmentCatalogueData Load()
        {
            if (!File.Exists(_cataloguePath))
                return NewCatalogue();

            var json = File.ReadAllText(_cataloguePath);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The local product enrollment catalogue is empty.");

            var catalogue = JsonUtility.FromJson<ProductEnrollmentCatalogueData>(json);
            if (catalogue == null)
                throw new InvalidDataException("The local product enrollment catalogue could not be parsed.");
            ValidateCatalogue(catalogue);
            return catalogue;
        }

        public ProductEnrollmentRecord UpsertProduct(
            ProductEnrollmentDraft draft,
            IReadOnlyList<float[]> referenceEmbeddings,
            DateTime? utcNow = null)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));

            var sku = RequireText(draft.Sku, "SKU", 64);
            var nameEnglish = OptionalText(draft.NameEnglish, 160);
            var nameArabic = OptionalText(draft.NameArabic, 160);
            var barcode = OptionalText(draft.Barcode, 128);
            if (nameEnglish.Length == 0 && nameArabic.Length == 0)
                throw new InvalidOperationException("Enter at least one product name.");

            var references = BuildReferences(referenceEmbeddings, utcNow ?? DateTime.UtcNow);
            var catalogue = Load();
            var existingIndex = FindProductIndex(catalogue.products, sku);

            if (existingIndex < 0 && catalogue.products.Count >= MaximumProductCount)
                throw new InvalidOperationException($"The local catalogue limit is {MaximumProductCount} products.");

            if (barcode.Length > 0)
            {
                for (var index = 0; index < catalogue.products.Count; index++)
                {
                    if (index == existingIndex)
                        continue;
                    var existingBarcode = catalogue.products[index]?.barcode;
                    if (!string.IsNullOrWhiteSpace(existingBarcode) &&
                        string.Equals(existingBarcode.Trim(), barcode, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Barcode '{barcode}' is already assigned to another product.");
                    }
                }
            }

            var timestamp = (utcNow ?? DateTime.UtcNow).ToUniversalTime();
            var record = new ProductEnrollmentRecord
            {
                sku = sku,
                nameEnglish = nameEnglish,
                nameArabic = nameArabic,
                barcode = barcode,
                active = true,
                updatedAtUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
                references = references
            };

            if (existingIndex >= 0)
                catalogue.products[existingIndex] = record;
            else
                catalogue.products.Add(record);

            catalogue.products.Sort((left, right) =>
                string.Compare(left?.sku, right?.sku, StringComparison.OrdinalIgnoreCase));
            Save(catalogue);
            return record;
        }

        public bool SetProductActive(string sku, bool active, DateTime? utcNow = null)
        {
            var normalizedSku = RequireText(sku, "SKU", 64);
            var catalogue = Load();
            var index = FindProductIndex(catalogue.products, normalizedSku);
            if (index < 0)
                return false;

            catalogue.products[index].active = active;
            catalogue.products[index].updatedAtUtc =
                (utcNow ?? DateTime.UtcNow).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            Save(catalogue);
            return true;
        }

        public bool DeleteProduct(string sku)
        {
            var normalizedSku = RequireText(sku, "SKU", 64);
            var catalogue = Load();
            var index = FindProductIndex(catalogue.products, normalizedSku);
            if (index < 0)
                return false;
            catalogue.products.RemoveAt(index);
            Save(catalogue);
            return true;
        }

        public void DeleteAll()
        {
            if (Directory.Exists(_customerDirectory))
                Directory.Delete(_customerDirectory, true);
        }

        public static void DeleteAllLocalData(string rootDirectory = null)
        {
            var root = ResolveRootDirectory(rootDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static string ResolveRootDirectory(string rootDirectory)
        {
            return string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "product-enrollments")
                : Path.GetFullPath(rootDirectory);
        }

        private ProductEnrollmentCatalogueData NewCatalogue()
        {
            return new ProductEnrollmentCatalogueData
            {
                schemaVersion = SchemaVersion,
                customerCode = _customerCode,
                products = new List<ProductEnrollmentRecord>()
            };
        }

        private void Save(ProductEnrollmentCatalogueData catalogue)
        {
            ValidateCatalogue(catalogue);
            Directory.CreateDirectory(_customerDirectory);

            var json = JsonUtility.ToJson(catalogue, true) + "\n";
            var temporaryPath = _cataloguePath + ".tmp";
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(_cataloguePath))
            {
                var backupPath = _cataloguePath + ".bak";
                File.Copy(_cataloguePath, backupPath, true);
            }

            File.Copy(temporaryPath, _cataloguePath, true);
            File.Delete(temporaryPath);
        }

        private void ValidateCatalogue(ProductEnrollmentCatalogueData catalogue)
        {
            if (catalogue.schemaVersion != SchemaVersion)
                throw new InvalidDataException($"Unsupported product enrollment schema {catalogue.schemaVersion}.");

            var storedCustomerCode = CustomerStorageScope.Require(catalogue.customerCode);
            if (!string.Equals(storedCustomerCode, _customerCode, StringComparison.Ordinal))
                throw new InvalidDataException("The product enrollment catalogue belongs to another customer.");

            if (catalogue.products == null)
                catalogue.products = new List<ProductEnrollmentRecord>();
            if (catalogue.products.Count > MaximumProductCount)
                throw new InvalidDataException("The product enrollment catalogue exceeds the local product limit.");

            var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var barcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < catalogue.products.Count; index++)
            {
                var product = catalogue.products[index];
                if (product == null)
                    throw new InvalidDataException($"Product enrollment record {index} is null.");

                product.sku = RequireText(product.sku, "SKU", 64);
                product.nameEnglish = OptionalText(product.nameEnglish, 160);
                product.nameArabic = OptionalText(product.nameArabic, 160);
                product.barcode = OptionalText(product.barcode, 128);
                if (!skus.Add(product.sku))
                    throw new InvalidDataException($"Duplicate enrolled SKU '{product.sku}'.");
                if (product.barcode.Length > 0 && !barcodes.Add(product.barcode))
                    throw new InvalidDataException($"Duplicate enrolled barcode '{product.barcode}'.");

                if (product.references == null ||
                    product.references.Count < MinimumReferenceCount ||
                    product.references.Count > MaximumReferenceCount)
                {
                    throw new InvalidDataException(
                        $"SKU '{product.sku}' must have {MinimumReferenceCount}-{MaximumReferenceCount} reference embeddings.");
                }

                var expectedDimension = -1;
                for (var referenceIndex = 0; referenceIndex < product.references.Count; referenceIndex++)
                {
                    var reference = product.references[referenceIndex];
                    if (reference?.values == null || reference.values.Length < 2)
                        throw new InvalidDataException($"SKU '{product.sku}' contains an invalid reference embedding.");
                    if (expectedDimension < 0)
                        expectedDimension = reference.values.Length;
                    if (reference.values.Length != expectedDimension)
                        throw new InvalidDataException($"SKU '{product.sku}' contains mixed embedding dimensions.");
                    ProductEmbeddingMath.ValidateFinite(reference.values);
                }
            }
        }

        private static List<ProductReferenceEmbedding> BuildReferences(
            IReadOnlyList<float[]> referenceEmbeddings,
            DateTime capturedAtUtc)
        {
            if (referenceEmbeddings == null ||
                referenceEmbeddings.Count < MinimumReferenceCount ||
                referenceEmbeddings.Count > MaximumReferenceCount)
            {
                throw new InvalidOperationException(
                    $"Capture {MinimumReferenceCount}-{MaximumReferenceCount} reference views before saving the product.");
            }

            var result = new List<ProductReferenceEmbedding>(referenceEmbeddings.Count);
            var expectedDimension = -1;
            for (var index = 0; index < referenceEmbeddings.Count; index++)
            {
                var source = referenceEmbeddings[index];
                if (source == null || source.Length < 2)
                    throw new InvalidOperationException("A captured reference embedding is invalid.");
                if (expectedDimension < 0)
                    expectedDimension = source.Length;
                if (source.Length != expectedDimension)
                    throw new InvalidOperationException("All reference embeddings must use the same dimension.");

                ProductEmbeddingMath.ValidateFinite(source);
                var copy = new float[source.Length];
                Array.Copy(source, copy, source.Length);
                ProductEmbeddingMath.NormalizeInPlace(copy);

                result.Add(new ProductReferenceEmbedding
                {
                    capturedAtUtc = capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    values = copy
                });
            }

            return result;
        }

        private static int FindProductIndex(IReadOnlyList<ProductEnrollmentRecord> products, string sku)
        {
            for (var index = 0; index < products.Count; index++)
            {
                if (products[index] != null &&
                    string.Equals(products[index].sku, sku, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }

        private static string RequireText(string value, string fieldName, int maximumLength)
        {
            var normalized = OptionalText(value, maximumLength);
            if (normalized.Length == 0)
                throw new InvalidOperationException($"{fieldName} is required.");
            return normalized;
        }

        private static string OptionalText(string value, int maximumLength)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (normalized.Length > maximumLength)
                throw new InvalidOperationException($"Value exceeds the maximum length of {maximumLength} characters.");
            return normalized;
        }
    }

    public interface IProductEmbeddingExtractor
    {
        int Dimension { get; }
        float[] Extract(Texture2D texture);
    }

    public sealed class MvpVisualEmbeddingExtractor : IProductEmbeddingExtractor
    {
        private const int GridSize = 8;
        private const int HueBins = 16;
        private const int SaturationBins = 8;
        private const int ValueBins = 8;
        private const int LuminanceBins = 16;
        private const int GridFeatureCount = GridSize * GridSize * 3;
        public const int FeatureDimension = GridFeatureCount + HueBins + SaturationBins + ValueBins + LuminanceBins;

        public int Dimension => FeatureDimension;

        public float[] Extract(Texture2D texture)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            if (texture.width < 16 || texture.height < 16)
                throw new InvalidOperationException("The captured frame is too small for product enrollment.");

            var pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != texture.width * texture.height)
                throw new InvalidOperationException("Unable to read captured camera pixels.");

            var features = new float[FeatureDimension];
            var cellCounts = new int[GridSize * GridSize];

            var minX = texture.width / 10;
            var maxX = texture.width - minX;
            var minY = texture.height / 10;
            var maxY = texture.height - minY;
            var cropWidth = Math.Max(1, maxX - minX);
            var cropHeight = Math.Max(1, maxY - minY);
            var samplingStep = Math.Max(1, Math.Min(cropWidth, cropHeight) / 160);

            for (var y = minY; y < maxY; y += samplingStep)
            {
                var gridY = Math.Min(GridSize - 1, ((y - minY) * GridSize) / cropHeight);
                for (var x = minX; x < maxX; x += samplingStep)
                {
                    var gridX = Math.Min(GridSize - 1, ((x - minX) * GridSize) / cropWidth);
                    var color32 = pixels[y * texture.width + x];
                    var r = color32.r / 255f;
                    var g = color32.g / 255f;
                    var b = color32.b / 255f;

                    var cell = gridY * GridSize + gridX;
                    var gridOffset = cell * 3;
                    features[gridOffset] += r;
                    features[gridOffset + 1] += g;
                    features[gridOffset + 2] += b;
                    cellCounts[cell]++;

                    Color.RGBToHSV(new Color(r, g, b, 1f), out var hue, out var saturation, out var value);
                    features[GridFeatureCount + Math.Min(HueBins - 1, (int)(hue * HueBins))] += 1f;
                    features[GridFeatureCount + HueBins +
                             Math.Min(SaturationBins - 1, (int)(saturation * SaturationBins))] += 1f;
                    features[GridFeatureCount + HueBins + SaturationBins +
                             Math.Min(ValueBins - 1, (int)(value * ValueBins))] += 1f;

                    var luminance = Mathf.Clamp01(0.2126f * r + 0.7152f * g + 0.0722f * b);
                    features[GridFeatureCount + HueBins + SaturationBins + ValueBins +
                             Math.Min(LuminanceBins - 1, (int)(luminance * LuminanceBins))] += 1f;
                }
            }

            for (var cell = 0; cell < cellCounts.Length; cell++)
            {
                if (cellCounts[cell] <= 0)
                    continue;
                var scale = 1f / cellCounts[cell];
                var offset = cell * 3;
                features[offset] *= scale;
                features[offset + 1] *= scale;
                features[offset + 2] *= scale;
            }

            NormalizeHistogram(features, GridFeatureCount, HueBins);
            NormalizeHistogram(features, GridFeatureCount + HueBins, SaturationBins);
            NormalizeHistogram(features, GridFeatureCount + HueBins + SaturationBins, ValueBins);
            NormalizeHistogram(features, GridFeatureCount + HueBins + SaturationBins + ValueBins, LuminanceBins);
            ProductEmbeddingMath.NormalizeInPlace(features);
            return features;
        }

        private static void NormalizeHistogram(float[] features, int offset, int count)
        {
            var total = 0f;
            for (var index = 0; index < count; index++)
                total += features[offset + index];
            if (total <= 0f)
                return;
            var scale = 1f / total;
            for (var index = 0; index < count; index++)
                features[offset + index] *= scale;
        }
    }

    public static class ProductEmbeddingMath
    {
        public static float CosineSimilarity(float[] left, float[] right)
        {
            if (left == null || right == null || left.Length == 0 || left.Length != right.Length)
                return -1f;

            double dot = 0d;
            double leftNorm = 0d;
            double rightNorm = 0d;
            for (var index = 0; index < left.Length; index++)
            {
                var leftValue = left[index];
                var rightValue = right[index];
                if (float.IsNaN(leftValue) || float.IsInfinity(leftValue) ||
                    float.IsNaN(rightValue) || float.IsInfinity(rightValue))
                    return -1f;
                dot += leftValue * rightValue;
                leftNorm += leftValue * leftValue;
                rightNorm += rightValue * rightValue;
            }

            if (leftNorm <= 1e-12d || rightNorm <= 1e-12d)
                return -1f;
            return Mathf.Clamp((float)(dot / Math.Sqrt(leftNorm * rightNorm)), -1f, 1f);
        }

        public static void NormalizeInPlace(float[] values)
        {
            if (values == null || values.Length == 0)
                throw new InvalidOperationException("Embedding values are empty.");

            ValidateFinite(values);
            double normSquared = 0d;
            for (var index = 0; index < values.Length; index++)
                normSquared += values[index] * values[index];
            if (normSquared <= 1e-12d)
                throw new InvalidOperationException("Embedding magnitude is zero.");

            var inverseNorm = 1f / (float)Math.Sqrt(normSquared);
            for (var index = 0; index < values.Length; index++)
                values[index] *= inverseNorm;
        }

        public static void ValidateFinite(float[] values)
        {
            if (values == null)
                throw new InvalidOperationException("Embedding values are missing.");
            for (var index = 0; index < values.Length; index++)
            {
                if (float.IsNaN(values[index]) || float.IsInfinity(values[index]))
                    throw new InvalidOperationException("Embedding contains a non-finite value.");
            }
        }
    }

    public sealed class ProductMatchResult
    {
        public bool IsMatch { get; }
        public bool IsAmbiguous { get; }
        public string Sku { get; }
        public float Similarity { get; }
        public float RunnerUpSimilarity { get; }

        public ProductMatchResult(bool isMatch, bool isAmbiguous, string sku, float similarity, float runnerUpSimilarity)
        {
            IsMatch = isMatch;
            IsAmbiguous = isAmbiguous;
            Sku = sku ?? string.Empty;
            Similarity = similarity;
            RunnerUpSimilarity = runnerUpSimilarity;
        }
    }

    public sealed class ProductRecognitionMatcher
    {
        public const float DefaultMinimumSimilarity = 0.86f;
        public const float DefaultMinimumMargin = 0.03f;

        private readonly float _minimumSimilarity;
        private readonly float _minimumMargin;

        public ProductRecognitionMatcher(float minimumSimilarity = DefaultMinimumSimilarity, float minimumMargin = DefaultMinimumMargin)
        {
            if (minimumSimilarity < -1f || minimumSimilarity > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumSimilarity));
            if (minimumMargin < 0f || minimumMargin > 2f)
                throw new ArgumentOutOfRangeException(nameof(minimumMargin));
            _minimumSimilarity = minimumSimilarity;
            _minimumMargin = minimumMargin;
        }

        public ProductMatchResult Match(float[] queryEmbedding, ProductEnrollmentCatalogueData catalogue)
        {
            if (queryEmbedding == null || queryEmbedding.Length < 2)
                throw new ArgumentException("A query embedding is required.", nameof(queryEmbedding));
            if (catalogue?.products == null || catalogue.products.Count == 0)
                return new ProductMatchResult(false, false, string.Empty, -1f, -1f);

            ProductEmbeddingMath.ValidateFinite(queryEmbedding);
            var bestSku = string.Empty;
            var best = -1f;
            var runnerUp = -1f;

            for (var productIndex = 0; productIndex < catalogue.products.Count; productIndex++)
            {
                var product = catalogue.products[productIndex];
                if (product == null || !product.active || product.references == null)
                    continue;

                var productBest = -1f;
                for (var referenceIndex = 0; referenceIndex < product.references.Count; referenceIndex++)
                {
                    var values = product.references[referenceIndex]?.values;
                    var similarity = ProductEmbeddingMath.CosineSimilarity(queryEmbedding, values);
                    if (similarity > productBest)
                        productBest = similarity;
                }

                if (productBest > best)
                {
                    runnerUp = best;
                    best = productBest;
                    bestSku = product.sku ?? string.Empty;
                }
                else if (productBest > runnerUp)
                {
                    runnerUp = productBest;
                }
            }

            if (bestSku.Length == 0 || best < _minimumSimilarity)
                return new ProductMatchResult(false, false, bestSku, best, runnerUp);

            var ambiguous = runnerUp >= -0.5f && best - runnerUp < _minimumMargin;
            return new ProductMatchResult(!ambiguous, ambiguous, bestSku, best, runnerUp);
        }
    }
}
