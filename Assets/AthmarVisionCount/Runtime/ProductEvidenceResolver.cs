using System;

namespace AthmarLabs.VisionCount
{
    public enum ProductRecognitionSource
    {
        None = 0,
        Barcode = 1,
        Ocr = 2,
        ManualHardCase = 3,
        BulkVisual = 4,
        ManualAndBulkVisual = 5
    }

    public sealed class ProductRecognitionEvidence
    {
        public string Barcode { get; set; } = string.Empty;
        public string OcrText { get; set; } = string.Empty;
        public float[] VisualEmbedding { get; set; }
    }

    public sealed class ProductResolution
    {
        public ProductResolution(
            bool isMatch,
            bool isAmbiguous,
            string sku,
            float score,
            float runnerUpScore,
            ProductRecognitionSource source)
        {
            IsMatch = isMatch;
            IsAmbiguous = isAmbiguous;
            Sku = sku ?? string.Empty;
            Score = score;
            RunnerUpScore = runnerUpScore;
            Source = source;
        }

        public bool IsMatch { get; }
        public bool IsAmbiguous { get; }
        public string Sku { get; }
        public float Score { get; }
        public float RunnerUpScore { get; }
        public ProductRecognitionSource Source { get; }
    }

    /// <summary>
    /// Evidence-first product resolution. Exact barcode and unique OCR evidence beat visual guessing.
    /// Manual enrollment is an overlay for hard/new products, not the primary onboarding path.
    /// </summary>
    public sealed class ProductEvidenceResolver
    {
        public const float DefaultBulkMinimumSimilarity = 0.90f;
        public const float DefaultBulkMinimumMargin = 0.04f;

        private readonly BulkProductCatalogue _bulkCatalogue;
        private readonly BulkEmbeddingIndex _bulkEmbeddings;
        private readonly RobustProductRecognitionMatcher _manualMatcher;
        private readonly float _bulkMinimumSimilarity;
        private readonly float _bulkMinimumMargin;
        private readonly int _candidateLimit;

        public ProductEvidenceResolver(
            BulkProductCatalogue bulkCatalogue,
            BulkEmbeddingIndex bulkEmbeddings,
            float bulkMinimumSimilarity = DefaultBulkMinimumSimilarity,
            float bulkMinimumMargin = DefaultBulkMinimumMargin,
            int candidateLimit = BulkEmbeddingIndex.DefaultCandidateLimit)
        {
            _bulkCatalogue = bulkCatalogue;
            _bulkEmbeddings = bulkEmbeddings;
            _manualMatcher = new RobustProductRecognitionMatcher();
            if (bulkMinimumSimilarity < -1f || bulkMinimumSimilarity > 1f)
                throw new ArgumentOutOfRangeException(nameof(bulkMinimumSimilarity));
            if (bulkMinimumMargin < 0f || bulkMinimumMargin > 2f)
                throw new ArgumentOutOfRangeException(nameof(bulkMinimumMargin));
            if (candidateLimit < 1)
                throw new ArgumentOutOfRangeException(nameof(candidateLimit));
            _bulkMinimumSimilarity = bulkMinimumSimilarity;
            _bulkMinimumMargin = bulkMinimumMargin;
            _candidateLimit = candidateLimit;
        }

        public ProductResolution Resolve(
            ProductRecognitionEvidence evidence,
            ProductEnrollmentCatalogueData manualHardCases = null)
        {
            evidence ??= new ProductRecognitionEvidence();

            if (_bulkCatalogue != null &&
                _bulkCatalogue.TryGetByBarcode(evidence.Barcode, out var barcodeProduct) &&
                barcodeProduct.Active)
            {
                return new ProductResolution(true, false, barcodeProduct.Sku, 1f, -1f, ProductRecognitionSource.Barcode);
            }

            if (_bulkCatalogue != null && !string.IsNullOrWhiteSpace(evidence.OcrText))
            {
                if (_bulkCatalogue.TryResolveOcrText(evidence.OcrText, out var ocrProduct, out var ocrAmbiguous) && ocrProduct.Active)
                    return new ProductResolution(true, false, ocrProduct.Sku, 1f, -1f, ProductRecognitionSource.Ocr);
                if (ocrAmbiguous)
                    return new ProductResolution(false, true, string.Empty, -1f, -1f, ProductRecognitionSource.Ocr);
            }

            if (evidence.VisualEmbedding == null || evidence.VisualEmbedding.Length < 2)
                return new ProductResolution(false, false, string.Empty, -1f, -1f, ProductRecognitionSource.None);

            ProductMatchResult manual = null;
            if (manualHardCases?.products != null && manualHardCases.products.Count > 0)
                manual = _manualMatcher.Match(evidence.VisualEmbedding, manualHardCases);

            BulkEmbeddingSearchResult bulk = null;
            var bulkAccepted = false;
            if (_bulkEmbeddings != null && evidence.VisualEmbedding.Length == _bulkEmbeddings.Dimension)
            {
                bulk = _bulkEmbeddings.Search(evidence.VisualEmbedding, _candidateLimit);
                bulkAccepted = bulk.Sku.Length > 0 &&
                               bulk.Similarity >= _bulkMinimumSimilarity &&
                               (bulk.RunnerUpSimilarity < -0.5f || bulk.Similarity - bulk.RunnerUpSimilarity >= _bulkMinimumMargin);
            }

            var manualAccepted = manual != null && manual.IsMatch;
            if (manualAccepted && bulkAccepted)
            {
                if (string.Equals(manual.Sku, bulk.Sku, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProductResolution(
                        true,
                        false,
                        manual.Sku,
                        Math.Max(manual.Similarity, bulk.Similarity),
                        Math.Max(manual.RunnerUpSimilarity, bulk.RunnerUpSimilarity),
                        ProductRecognitionSource.ManualAndBulkVisual);
                }

                return new ProductResolution(
                    false,
                    true,
                    string.Empty,
                    Math.Max(manual.Similarity, bulk.Similarity),
                    Math.Min(manual.Similarity, bulk.Similarity),
                    ProductRecognitionSource.None);
            }

            if (manualAccepted)
            {
                return new ProductResolution(
                    true,
                    false,
                    manual.Sku,
                    manual.Similarity,
                    manual.RunnerUpSimilarity,
                    ProductRecognitionSource.ManualHardCase);
            }

            if (bulkAccepted)
            {
                return new ProductResolution(
                    true,
                    false,
                    bulk.Sku,
                    bulk.Similarity,
                    bulk.RunnerUpSimilarity,
                    ProductRecognitionSource.BulkVisual);
            }

            var bestScore = -1f;
            var runnerUp = -1f;
            if (manual != null && manual.Similarity > bestScore)
            {
                bestScore = manual.Similarity;
                runnerUp = manual.RunnerUpSimilarity;
            }
            if (bulk != null && bulk.Similarity > bestScore)
            {
                runnerUp = Math.Max(bestScore, bulk.RunnerUpSimilarity);
                bestScore = bulk.Similarity;
            }

            return new ProductResolution(false, false, string.Empty, bestScore, runnerUp, ProductRecognitionSource.None);
        }
    }
}
