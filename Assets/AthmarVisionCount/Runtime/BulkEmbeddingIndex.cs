using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AthmarLabs.VisionCount
{
    public sealed class BulkEmbeddingSearchResult
    {
        public BulkEmbeddingSearchResult(string sku, float similarity, float runnerUpSimilarity)
        {
            Sku = sku ?? string.Empty;
            Similarity = similarity;
            RunnerUpSimilarity = runnerUpSimilarity;
        }

        public string Sku { get; }
        public float Similarity { get; }
        public float RunnerUpSimilarity { get; }
    }

    /// <summary>
    /// Compact customer vector index. Vectors are L2-normalized and int8-quantized offline.
    /// A 64-bit projection signature prunes a large catalogue before exact cosine scoring.
    /// </summary>
    public sealed class BulkEmbeddingIndex
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ATHVEC01");
        public const int FormatVersion = 1;
        public const int MaximumVectorCount = 100000;
        public const int DefaultCandidateLimit = 256;

        private readonly string[] _skus;
        private readonly ulong[] _signatures;
        private readonly float[] _inverseNorms;
        private readonly byte[] _vectors;
        private readonly Dictionary<string, int> _bySku;

        private BulkEmbeddingIndex(
            string modelId,
            int dimension,
            string[] skus,
            ulong[] signatures,
            float[] inverseNorms,
            byte[] vectors,
            Dictionary<string, int> bySku)
        {
            ModelId = modelId;
            Dimension = dimension;
            _skus = skus;
            _signatures = signatures;
            _inverseNorms = inverseNorms;
            _vectors = vectors;
            _bySku = bySku;
        }

        public string ModelId { get; }
        public int Dimension { get; }
        public int Count => _skus.Length;

        public static BulkEmbeddingIndex Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A bulk embedding index path is required.", nameof(path));
            using var stream = File.OpenRead(path);
            return Load(stream);
        }

        public static BulkEmbeddingIndex Load(Stream stream)
        {
            if (stream == null || !stream.CanRead)
                throw new ArgumentException("A readable bulk embedding stream is required.", nameof(stream));

            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || !BytesEqual(magic, Magic))
                throw new InvalidDataException("The bulk embedding index has an invalid magic header.");

            var version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported bulk embedding index version {version}.");

            var dimension = reader.ReadInt32();
            if (dimension < 2 || dimension > 8192)
                throw new InvalidDataException("The bulk embedding dimension is invalid.");
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumVectorCount)
                throw new InvalidDataException($"The bulk embedding index exceeds {MaximumVectorCount} vectors.");

            var modelIdLength = reader.ReadInt32();
            if (modelIdLength < 1 || modelIdLength > 4096)
                throw new InvalidDataException("The bulk embedding model identifier length is invalid.");
            var modelIdBytes = reader.ReadBytes(modelIdLength);
            if (modelIdBytes.Length != modelIdLength)
                throw new EndOfStreamException("The bulk embedding model identifier is truncated.");
            var modelId = Encoding.UTF8.GetString(modelIdBytes);

            var skus = new string[count];
            var signatures = new ulong[count];
            var inverseNorms = new float[count];
            var vectors = new byte[checked(count * dimension)];
            var bySku = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);

            for (var record = 0; record < count; record++)
            {
                var skuLength = reader.ReadUInt16();
                if (skuLength < 1 || skuLength > 1024)
                    throw new InvalidDataException($"Bulk embedding record {record} has an invalid SKU length.");
                var skuBytes = reader.ReadBytes(skuLength);
                if (skuBytes.Length != skuLength)
                    throw new EndOfStreamException($"Bulk embedding record {record} has a truncated SKU.");
                var sku = Encoding.UTF8.GetString(skuBytes).Trim();
                if (sku.Length == 0 || bySku.ContainsKey(sku))
                    throw new InvalidDataException($"Bulk embedding record {record} has an empty or duplicate SKU.");

                var signature = reader.ReadUInt64();
                var raw = reader.ReadBytes(dimension);
                if (raw.Length != dimension)
                    throw new EndOfStreamException($"Bulk embedding vector for SKU '{sku}' is truncated.");

                double normSquared = 0d;
                var destinationOffset = checked(record * dimension);
                for (var index = 0; index < dimension; index++)
                {
                    var quantized = unchecked((sbyte)raw[index]);
                    normSquared += quantized * quantized;
                    vectors[destinationOffset + index] = raw[index];
                }
                if (normSquared <= 1e-9d)
                    throw new InvalidDataException($"Bulk embedding vector for SKU '{sku}' has zero magnitude.");

                skus[record] = sku;
                signatures[record] = signature;
                inverseNorms[record] = 1f / (float)Math.Sqrt(normSquared);
                bySku.Add(sku, record);
            }

            if (stream.CanSeek && stream.Position != stream.Length)
                throw new InvalidDataException("The bulk embedding index contains unexpected trailing bytes.");

            return new BulkEmbeddingIndex(modelId, dimension, skus, signatures, inverseNorms, vectors, bySku);
        }

        public void ValidateAgainst(BulkProductCatalogue catalogue, string expectedModelId, int expectedDimension)
        {
            if (catalogue == null)
                throw new ArgumentNullException(nameof(catalogue));
            if (!string.Equals(ModelId, expectedModelId, StringComparison.Ordinal))
                throw new InvalidDataException("The bulk embedding index was generated by a different embedding model.");
            if (Dimension != expectedDimension)
                throw new InvalidDataException($"Bulk embedding dimension {Dimension} does not match expected dimension {expectedDimension}.");

            for (var index = 0; index < _skus.Length; index++)
            {
                if (!catalogue.TryGetBySku(_skus[index], out _))
                    throw new InvalidDataException($"Bulk embedding SKU '{_skus[index]}' is not present in the bulk product catalogue.");
            }
        }

        public bool ContainsSku(string sku)
        {
            return !string.IsNullOrWhiteSpace(sku) && _bySku.ContainsKey(sku.Trim());
        }

        public BulkEmbeddingSearchResult Search(float[] queryEmbedding, int candidateLimit = DefaultCandidateLimit)
        {
            if (queryEmbedding == null || queryEmbedding.Length != Dimension)
                throw new ArgumentException($"A {Dimension}-dimensional query embedding is required.", nameof(queryEmbedding));
            ProductEmbeddingMath.ValidateFinite(queryEmbedding);
            if (_skus.Length == 0)
                return new BulkEmbeddingSearchResult(string.Empty, -1f, -1f);

            candidateLimit = Math.Max(1, Math.Min(candidateLimit, _skus.Length));
            var querySignature = ComputeProjectionSignature(queryEmbedding);
            var candidateIndexes = new int[candidateLimit];
            var candidateDistances = new int[candidateLimit];
            for (var index = 0; index < candidateLimit; index++)
            {
                candidateIndexes[index] = -1;
                candidateDistances[index] = int.MaxValue;
            }

            for (var record = 0; record < _skus.Length; record++)
            {
                var distance = PopCount(querySignature ^ _signatures[record]);
                if (distance >= candidateDistances[candidateLimit - 1])
                    continue;

                var insertAt = candidateLimit - 1;
                while (insertAt > 0 && distance < candidateDistances[insertAt - 1])
                {
                    candidateDistances[insertAt] = candidateDistances[insertAt - 1];
                    candidateIndexes[insertAt] = candidateIndexes[insertAt - 1];
                    insertAt--;
                }
                candidateDistances[insertAt] = distance;
                candidateIndexes[insertAt] = record;
            }

            double queryNormSquared = 0d;
            for (var index = 0; index < queryEmbedding.Length; index++)
                queryNormSquared += queryEmbedding[index] * queryEmbedding[index];
            if (queryNormSquared <= 1e-12d)
                return new BulkEmbeddingSearchResult(string.Empty, -1f, -1f);
            var queryInverseNorm = 1f / (float)Math.Sqrt(queryNormSquared);

            var best = -1f;
            var runnerUp = -1f;
            var bestSku = string.Empty;
            for (var candidate = 0; candidate < candidateLimit; candidate++)
            {
                var record = candidateIndexes[candidate];
                if (record < 0)
                    continue;
                var similarity = Score(record, queryEmbedding, queryInverseNorm);
                if (similarity > best)
                {
                    runnerUp = best;
                    best = similarity;
                    bestSku = _skus[record];
                }
                else if (similarity > runnerUp)
                {
                    runnerUp = similarity;
                }
            }

            return new BulkEmbeddingSearchResult(bestSku, best, runnerUp);
        }

        public static ulong ComputeProjectionSignature(float[] embedding)
        {
            if (embedding == null || embedding.Length == 0)
                throw new ArgumentException("An embedding is required.", nameof(embedding));

            var accumulators = new double[64];
            for (var index = 0; index < embedding.Length; index++)
            {
                var value = embedding[index];
                var bitA = index & 63;
                var bitB = unchecked((index * 37 + 17) & 63);
                var signA = ((index * 17 + bitA * 13) & 1) == 0 ? 1d : -1d;
                var signB = ((index * 29 + bitB * 7 + 1) & 1) == 0 ? 1d : -1d;
                accumulators[bitA] += value * signA;
                accumulators[bitB] += value * signB;
            }

            ulong signature = 0UL;
            for (var bit = 0; bit < 64; bit++)
            {
                if (accumulators[bit] >= 0d)
                    signature |= 1UL << bit;
            }
            return signature;
        }

        private float Score(int record, float[] query, float queryInverseNorm)
        {
            double dot = 0d;
            var offset = checked(record * Dimension);
            for (var index = 0; index < Dimension; index++)
                dot += query[index] * unchecked((sbyte)_vectors[offset + index]);
            return Math.Max(-1f, Math.Min(1f, (float)dot * queryInverseNorm * _inverseNorms[record]));
        }

        private static int PopCount(ulong value)
        {
            var count = 0;
            while (value != 0UL)
            {
                value &= value - 1UL;
                count++;
            }
            return count;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }
            return true;
        }
    }
}
