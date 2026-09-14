using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AthmarLabs.VisionCount
{
    public sealed class CustomerPackageSnapshot
    {
        public CustomerPackageSnapshot(
            CustomerPackageManifest manifest,
            RuntimeCustomerConfiguration configuration,
            SkuCatalogue catalogue,
            string directoryPath,
            BulkProductCatalogue bulkCatalogue = null,
            BulkEmbeddingIndex bulkEmbeddingIndex = null)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            DirectoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            BulkCatalogue = bulkCatalogue;
            BulkEmbeddingIndex = bulkEmbeddingIndex;
        }

        public CustomerPackageManifest Manifest { get; }
        public RuntimeCustomerConfiguration Configuration { get; }
        public SkuCatalogue Catalogue { get; }
        public BulkProductCatalogue BulkCatalogue { get; }
        public BulkEmbeddingIndex BulkEmbeddingIndex { get; }
        public string DirectoryPath { get; }
        public bool HasBulkRecognitionData => BulkCatalogue != null && BulkEmbeddingIndex != null;
    }

    public sealed class CustomerPackageStore
    {
        public const string ManifestFileName = "manifest.json";
        public const string ModelFileName = "production.sentis";
        public const string CatalogueFileName = "sku_catalogue.csv";
        public const string BulkCatalogueFileName = "bulk_products.csv";
        public const string BulkEmbeddingIndexFileName = "bulk_embeddings.bin";

        private readonly string _rootDirectory;

        public CustomerPackageStore(string rootDirectory = null)
        {
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(UnityEngine.Application.persistentDataPath, "customer-packages")
                : Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(_rootDirectory);
        }

        public string RootDirectory => _rootDirectory;
        public string ActiveDirectory => Path.Combine(_rootDirectory, "active");
        public string PreviousDirectory => Path.Combine(_rootDirectory, "previous");
        public string StagingDirectory => Path.Combine(_rootDirectory, "staging");
        public bool HasActivePackage => Directory.Exists(ActiveDirectory);
        public bool HasPreviousPackage => Directory.Exists(PreviousDirectory);

        public string PrepareStagingDirectory()
        {
            DeleteDirectoryIfExists(StagingDirectory);
            Directory.CreateDirectory(StagingDirectory);
            return StagingDirectory;
        }

        public CustomerPackageSnapshot ValidateDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("A package directory is required.", nameof(directoryPath));

            directoryPath = Path.GetFullPath(directoryPath);
            var manifestPath = Path.Combine(directoryPath, ManifestFileName);
            var modelPath = Path.Combine(directoryPath, ModelFileName);
            var cataloguePath = Path.Combine(directoryPath, CatalogueFileName);
            RequireNonEmptyFile(manifestPath, ManifestFileName);
            RequireNonEmptyFile(modelPath, ModelFileName);
            RequireNonEmptyFile(cataloguePath, CatalogueFileName);

            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = CustomerPackageManifest.Parse(manifestJson);
            VerifyFileSha256(modelPath, manifest.modelSha256, ModelFileName);
            VerifyFileSha256(cataloguePath, manifest.catalogueSha256, CatalogueFileName);
            var catalogue = SkuCatalogue.Parse(File.ReadAllText(cataloguePath, Encoding.UTF8));

            BulkProductCatalogue bulkCatalogue = null;
            BulkEmbeddingIndex bulkEmbeddingIndex = null;
            if (manifest.HasBulkCatalogue)
            {
                var bulkCataloguePath = Path.Combine(directoryPath, BulkCatalogueFileName);
                var bulkEmbeddingPath = Path.Combine(directoryPath, BulkEmbeddingIndexFileName);
                RequireNonEmptyFile(bulkCataloguePath, BulkCatalogueFileName);
                RequireNonEmptyFile(bulkEmbeddingPath, BulkEmbeddingIndexFileName);
                VerifyFileSha256(bulkCataloguePath, manifest.bulkCatalogueSha256, BulkCatalogueFileName);
                VerifyFileSha256(bulkEmbeddingPath, manifest.bulkEmbeddingIndexSha256, BulkEmbeddingIndexFileName);

                bulkCatalogue = BulkProductCatalogue.Parse(File.ReadAllText(bulkCataloguePath, Encoding.UTF8));
                bulkEmbeddingIndex = BulkEmbeddingIndex.Load(bulkEmbeddingPath);
                if (!string.Equals(
                        bulkEmbeddingIndex.ModelId,
                        manifest.bulkEmbeddingModelId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The bulk embedding index model id does not match the customer manifest.");
                }
                if (!string.Equals(
                        manifest.bulkEmbeddingModelId,
                        SentisProductEmbeddingExtractor.ModelId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The customer package embedding model id is incompatible with this application build.");
                }

                bulkEmbeddingIndex.ValidateAgainst(
                    bulkCatalogue,
                    SentisProductEmbeddingExtractor.ModelId,
                    SentisProductEmbeddingExtractor.FeatureDimension);
            }

            var configuration = manifest.CreateConfiguration(modelPath);
            return new CustomerPackageSnapshot(
                manifest,
                configuration,
                catalogue,
                directoryPath,
                bulkCatalogue,
                bulkEmbeddingIndex);
        }

        public bool TryLoadActive(out CustomerPackageSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (!HasActivePackage)
                return false;

            try
            {
                snapshot = ValidateDirectory(ActiveDirectory);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public CustomerPackageSnapshot ActivateStaging()
        {
            var validated = ValidateDirectory(StagingDirectory);
            DeleteDirectoryIfExists(PreviousDirectory);

            var movedActive = false;
            try
            {
                if (Directory.Exists(ActiveDirectory))
                {
                    Directory.Move(ActiveDirectory, PreviousDirectory);
                    movedActive = true;
                }

                Directory.Move(StagingDirectory, ActiveDirectory);
                return ValidateDirectory(ActiveDirectory);
            }
            catch
            {
                DeleteDirectoryIfExists(ActiveDirectory);
                if (movedActive && Directory.Exists(PreviousDirectory))
                    Directory.Move(PreviousDirectory, ActiveDirectory);
                throw;
            }
        }

        public CustomerPackageSnapshot Rollback()
        {
            if (!Directory.Exists(PreviousDirectory))
                throw new InvalidOperationException("No previous customer package is available.");

            ValidateDirectory(PreviousDirectory);
            var failedDirectory = Path.Combine(_rootDirectory, "failed-active");
            DeleteDirectoryIfExists(failedDirectory);

            try
            {
                if (Directory.Exists(ActiveDirectory))
                    Directory.Move(ActiveDirectory, failedDirectory);
                Directory.Move(PreviousDirectory, ActiveDirectory);
                DeleteDirectoryIfExists(failedDirectory);
                return ValidateDirectory(ActiveDirectory);
            }
            catch
            {
                if (!Directory.Exists(ActiveDirectory) && Directory.Exists(failedDirectory))
                    Directory.Move(failedDirectory, ActiveDirectory);
                throw;
            }
        }

        public void DiscardStaging()
        {
            DeleteDirectoryIfExists(StagingDirectory);
        }

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            using var algorithm = SHA256.Create();
            return ToHex(algorithm.ComputeHash(bytes));
        }

        public static string ComputeFileSha256(string path)
        {
            RequireNonEmptyFile(path, Path.GetFileName(path));
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            return ToHex(algorithm.ComputeHash(stream));
        }

        public static void VerifyBytesSha256(byte[] bytes, string expectedSha256, string displayName)
        {
            var expected = CustomerPackageManifest.NormalizeSha256(expectedSha256, displayName + " SHA-256");
            var actual = ComputeSha256(bytes);
            if (!FixedTimeEquals(actual, expected))
                throw new InvalidDataException($"{displayName} failed SHA-256 verification.");
        }

        public static void VerifyFileSha256(string path, string expectedSha256, string displayName)
        {
            var expected = CustomerPackageManifest.NormalizeSha256(expectedSha256, displayName + " SHA-256");
            var actual = ComputeFileSha256(path);
            if (!FixedTimeEquals(actual, expected))
                throw new InvalidDataException($"{displayName} failed SHA-256 verification.");
        }

        private static void RequireNonEmptyFile(string path, string displayName)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new FileNotFoundException($"Customer package file {displayName} is missing or empty.", path);
        }

        private static string ToHex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);
            for (var index = 0; index < digest.Length; index++)
                builder.Append(digest[index].ToString("x2"));
            return builder.ToString();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
