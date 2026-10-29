using System;
using System.Collections.Generic;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Product-level recognition that requires agreement across several enrollment references.
    /// A single accidental high cosine score is never enough to accept a SKU.
    /// </summary>
    public sealed class RobustProductRecognitionMatcher
    {
        public const float DefaultMinimumSimilarity = 0.86f;
        public const float DefaultMinimumMargin = 0.03f;
        public const float DefaultReferenceSupportSimilarity = 0.80f;
        public const int DefaultConsensusReferenceCount = 3;

        private readonly float _minimumSimilarity;
        private readonly float _minimumMargin;
        private readonly float _referenceSupportSimilarity;
        private readonly int _consensusReferenceCount;

        public RobustProductRecognitionMatcher(
            float minimumSimilarity = DefaultMinimumSimilarity,
            float minimumMargin = DefaultMinimumMargin,
            float referenceSupportSimilarity = DefaultReferenceSupportSimilarity,
            int consensusReferenceCount = DefaultConsensusReferenceCount)
        {
            if (minimumSimilarity < -1f || minimumSimilarity > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumSimilarity));
            if (minimumMargin < 0f || minimumMargin > 2f)
                throw new ArgumentOutOfRangeException(nameof(minimumMargin));
            if (referenceSupportSimilarity < -1f || referenceSupportSimilarity > 1f)
                throw new ArgumentOutOfRangeException(nameof(referenceSupportSimilarity));
            if (consensusReferenceCount < 2 || consensusReferenceCount > ProductEnrollmentStore.MaximumReferenceCount)
                throw new ArgumentOutOfRangeException(nameof(consensusReferenceCount));

            _minimumSimilarity = minimumSimilarity;
            _minimumMargin = minimumMargin;
            _referenceSupportSimilarity = referenceSupportSimilarity;
            _consensusReferenceCount = consensusReferenceCount;
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

            var diagnosticBestSku = string.Empty;
            var diagnosticBest = -1f;
            var diagnosticRunnerUp = -1f;

            for (var productIndex = 0; productIndex < catalogue.products.Count; productIndex++)
            {
                var product = catalogue.products[productIndex];
                if (product == null || !product.active || product.references == null)
                    continue;

                if (!TryScoreProduct(queryEmbedding, product, out var productScore, out var supportCount))
                    continue;

                if (productScore > diagnosticBest)
                {
                    diagnosticRunnerUp = diagnosticBest;
                    diagnosticBest = productScore;
                    diagnosticBestSku = product.sku ?? string.Empty;
                }
                else if (productScore > diagnosticRunnerUp)
                {
                    diagnosticRunnerUp = productScore;
                }

                if (supportCount < _consensusReferenceCount)
                    continue;

                if (productScore > best)
                {
                    runnerUp = best;
                    best = productScore;
                    bestSku = product.sku ?? string.Empty;
                }
                else if (productScore > runnerUp)
                {
                    runnerUp = productScore;
                }
            }

            if (bestSku.Length == 0)
            {
                return new ProductMatchResult(
                    false,
                    false,
                    diagnosticBestSku,
                    diagnosticBest,
                    diagnosticRunnerUp);
            }

            if (best < _minimumSimilarity)
                return new ProductMatchResult(false, false, bestSku, best, runnerUp);

            var ambiguous = runnerUp >= -0.5f && best - runnerUp < _minimumMargin;
            return new ProductMatchResult(!ambiguous, ambiguous, bestSku, best, runnerUp);
        }

        private bool TryScoreProduct(
            float[] queryEmbedding,
            ProductEnrollmentRecord product,
            out float score,
            out int supportCount)
        {
            score = -1f;
            supportCount = 0;

            if (product.references == null || product.references.Count < _consensusReferenceCount)
                return false;

            var similarities = new List<float>(product.references.Count);
            for (var referenceIndex = 0; referenceIndex < product.references.Count; referenceIndex++)
            {
                var values = product.references[referenceIndex]?.values;
                var similarity = ProductEmbeddingMath.CosineSimilarity(queryEmbedding, values);
                if (similarity < -0.9999f)
                    continue;

                similarities.Add(similarity);
                if (similarity >= _referenceSupportSimilarity)
                    supportCount++;
            }

            if (similarities.Count < _consensusReferenceCount)
                return false;

            similarities.Sort((left, right) => right.CompareTo(left));
            double total = 0d;
            for (var index = 0; index < _consensusReferenceCount; index++)
                total += similarities[index];

            score = (float)(total / _consensusReferenceCount);
            return true;
        }
    }
}
