using System;
using System.Collections.Generic;

namespace AthmarLabs.VisionCount
{
    public static class YoloOutputDecoder
    {
        public static List<Detection> Decode(
            float[] data,
            int dimensionA,
            int dimensionB,
            DetectionTensorLayout layout,
            bool hasObjectness,
            bool coordinatesNormalized,
            int inputWidth,
            int inputHeight,
            float minimumConfidence,
            float nmsIouThreshold,
            int maxDetections,
            SkuCatalogue catalogue)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (catalogue == null)
                throw new ArgumentNullException(nameof(catalogue));
            if (dimensionA <= 0 || dimensionB <= 0 || data.LongLength != (long)dimensionA * dimensionB)
                throw new ArgumentException("The output tensor dimensions do not match its data length.", nameof(data));
            if (inputWidth <= 0 || inputHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(inputWidth));
            if (minimumConfidence < 0f || minimumConfidence > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
            if (nmsIouThreshold < 0f || nmsIouThreshold > 1f)
                throw new ArgumentOutOfRangeException(nameof(nmsIouThreshold));
            if (maxDetections <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDetections));

            var featureCount = layout == DetectionTensorLayout.ChannelsFirst ? dimensionA : dimensionB;
            var candidateCount = layout == DetectionTensorLayout.ChannelsFirst ? dimensionB : dimensionA;
            var classOffset = hasObjectness ? 5 : 4;
            var classCount = featureCount - classOffset;
            if (classCount <= 0)
                throw new InvalidOperationException("The model output does not contain any class scores.");

            var candidates = new List<Detection>();
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var objectness = hasObjectness ? Read(data, layout, featureCount, candidateCount, candidateIndex, 4) : 1f;
                ValidateProbability(objectness, "objectness", candidateIndex);
                if (objectness <= 0f)
                    continue;

                var bestClass = -1;
                var bestClassScore = float.MinValue;
                for (var classIndex = 0; classIndex < classCount; classIndex++)
                {
                    var score = Read(data, layout, featureCount, candidateCount, candidateIndex, classOffset + classIndex);
                    ValidateProbability(score, "class score", candidateIndex);
                    if (score <= bestClassScore)
                        continue;
                    bestClassScore = score;
                    bestClass = classIndex;
                }

                var confidence = objectness * bestClassScore;
                if (bestClass < 0 || confidence < minimumConfidence)
                    continue;
                if (!catalogue.TryGetByLabelIndex(bestClass, out var product) || product == null || !product.Active)
                    continue;

                var centerX = ReadFinite(data, layout, featureCount, candidateCount, candidateIndex, 0, "center X");
                var centerY = ReadFinite(data, layout, featureCount, candidateCount, candidateIndex, 1, "center Y");
                var width = ReadFinite(data, layout, featureCount, candidateCount, candidateIndex, 2, "width");
                var height = ReadFinite(data, layout, featureCount, candidateCount, candidateIndex, 3, "height");

                if (!coordinatesNormalized)
                {
                    centerX /= inputWidth;
                    width /= inputWidth;
                    centerY /= inputHeight;
                    height /= inputHeight;
                }

                if (width <= 0f || height <= 0f)
                    continue;
                width = Clamp01(width);
                height = Clamp01(height);
                var left = Clamp01(centerX - (width * 0.5f));
                var top = Clamp01(centerY - (height * 0.5f));
                width = Math.Min(width, 1f - left);
                height = Math.Min(height, 1f - top);
                if (width <= 0f || height <= 0f)
                    continue;

                candidates.Add(new Detection(product.Sku, confidence, new NormalizedRect(left, top, width, height)));
            }

            candidates.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
            var selected = new List<Detection>(Math.Min(maxDetections, candidates.Count));
            foreach (var candidate in candidates)
            {
                var suppressed = false;
                foreach (var accepted in selected)
                {
                    if (!string.Equals(candidate.Sku, accepted.Sku, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (candidate.Bounds.IntersectionOverUnion(accepted.Bounds) >= nmsIouThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                    continue;
                selected.Add(candidate);
                if (selected.Count >= maxDetections)
                    break;
            }

            return selected;
        }

        private static float ReadFinite(
            IReadOnlyList<float> data,
            DetectionTensorLayout layout,
            int featureCount,
            int candidateCount,
            int candidateIndex,
            int featureIndex,
            string name)
        {
            var value = Read(data, layout, featureCount, candidateCount, candidateIndex, featureIndex);
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException($"Model output contains invalid {name} at candidate {candidateIndex}.");
            return value;
        }

        private static void ValidateProbability(float value, string name, int candidateIndex)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                throw new InvalidOperationException($"Model output {name} at candidate {candidateIndex} must be a probability from 0 to 1.");
        }

        private static float Read(
            IReadOnlyList<float> data,
            DetectionTensorLayout layout,
            int featureCount,
            int candidateCount,
            int candidateIndex,
            int featureIndex)
        {
            var flatIndex = layout == DetectionTensorLayout.ChannelsFirst
                ? (featureIndex * candidateCount) + candidateIndex
                : (candidateIndex * featureCount) + featureIndex;
            return data[flatIndex];
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
