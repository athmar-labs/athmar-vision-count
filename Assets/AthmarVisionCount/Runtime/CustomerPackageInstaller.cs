using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Networking;

namespace AthmarLabs.VisionCount
{
    [DisallowMultipleComponent]
    public sealed class CustomerPackageInstaller : MonoBehaviour
    {
        private const long MaximumBulkCatalogueBytes = 64L * 1024L * 1024L;
        private const long MaximumBulkEmbeddingBytes = 256L * 1024L * 1024L;

        public event Action<string> ProgressChanged;
        public event Action<CustomerPackageSnapshot> InstallationCompleted;
        public event Action<string> InstallationFailed;

        private bool _installing;

        public bool IsInstalling => _installing;

        public void Install(string manifestUrl, string expectedManifestSha256, CustomerPackageStore store)
        {
            if (_installing)
                throw new InvalidOperationException("A customer package installation is already running.");
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            manifestUrl = CustomerPackageManifest.RequireHttpsUrl(manifestUrl, "manifestUrl");
            expectedManifestSha256 = CustomerPackageManifest.NormalizeSha256(expectedManifestSha256, "manifestSha256");
            StartCoroutine(InstallRoutine(manifestUrl, expectedManifestSha256, store));
        }

        private IEnumerator InstallRoutine(string manifestUrl, string expectedManifestSha256, CustomerPackageStore store)
        {
            _installing = true;
            ProgressChanged?.Invoke("Downloading and verifying customer manifest...");

            byte[] manifestBytes;
            using (var request = UnityWebRequest.Get(manifestUrl))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (!TryReadDownload(request, "customer manifest", store, out manifestBytes))
                    yield break;
            }

            CustomerPackageManifest manifest;
            string stagingDirectory;
            try
            {
                CustomerPackageStore.VerifyBytesSha256(manifestBytes, expectedManifestSha256, "Customer manifest");
                var manifestJson = Encoding.UTF8.GetString(manifestBytes);
                manifest = CustomerPackageManifest.Parse(manifestJson);
                stagingDirectory = store.PrepareStagingDirectory();
                File.WriteAllBytes(Path.Combine(stagingDirectory, CustomerPackageStore.ManifestFileName), manifestBytes);
            }
            catch (Exception exception)
            {
                Fail(store, exception);
                yield break;
            }

            ProgressChanged?.Invoke("Downloading and verifying inference model...");
            byte[] modelBytes;
            using (var request = UnityWebRequest.Get(manifest.modelUrl))
            {
                request.timeout = 300;
                yield return request.SendWebRequest();
                if (!TryReadDownload(request, "inference model", store, out modelBytes))
                    yield break;
            }

            string modelPath;
            try
            {
                CustomerPackageStore.VerifyBytesSha256(modelBytes, manifest.modelSha256, "Inference model");
                modelPath = Path.Combine(stagingDirectory, CustomerPackageStore.ModelFileName);
                File.WriteAllBytes(modelPath, modelBytes);
                modelBytes = null;
            }
            catch (Exception exception)
            {
                Fail(store, exception);
                yield break;
            }

            ProgressChanged?.Invoke("Downloading and verifying detector catalogue...");
            byte[] catalogueBytes;
            using (var request = UnityWebRequest.Get(manifest.catalogueUrl))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (!TryReadDownload(request, "detector SKU catalogue", store, out catalogueBytes))
                    yield break;
            }

            try
            {
                CustomerPackageStore.VerifyBytesSha256(catalogueBytes, manifest.catalogueSha256, "Detector SKU catalogue");
                var cataloguePath = Path.Combine(stagingDirectory, CustomerPackageStore.CatalogueFileName);
                File.WriteAllBytes(cataloguePath, catalogueBytes);
                SkuCatalogue.Parse(Encoding.UTF8.GetString(catalogueBytes));
            }
            catch (Exception exception)
            {
                Fail(store, exception);
                yield break;
            }

            if (manifest.HasBulkCatalogue)
            {
                ProgressChanged?.Invoke("Downloading bulk product catalogue...");
                var bulkCataloguePath = Path.Combine(stagingDirectory, CustomerPackageStore.BulkCatalogueFileName);
                var bulkCatalogueDownloaded = false;
                yield return DownloadLargeFile(
                    manifest.bulkCatalogueUrl,
                    bulkCataloguePath,
                    "bulk product catalogue",
                    120,
                    MaximumBulkCatalogueBytes,
                    store,
                    success => bulkCatalogueDownloaded = success);
                if (!bulkCatalogueDownloaded)
                    yield break;

                try
                {
                    CustomerPackageStore.VerifyFileSha256(
                        bulkCataloguePath,
                        manifest.bulkCatalogueSha256,
                        "Bulk product catalogue");
                    BulkProductCatalogue.Parse(File.ReadAllText(bulkCataloguePath, Encoding.UTF8));
                }
                catch (Exception exception)
                {
                    Fail(store, exception);
                    yield break;
                }

                ProgressChanged?.Invoke("Downloading compact product embedding index...");
                var embeddingPath = Path.Combine(stagingDirectory, CustomerPackageStore.BulkEmbeddingIndexFileName);
                var embeddingDownloaded = false;
                yield return DownloadLargeFile(
                    manifest.bulkEmbeddingIndexUrl,
                    embeddingPath,
                    "bulk embedding index",
                    600,
                    MaximumBulkEmbeddingBytes,
                    store,
                    success => embeddingDownloaded = success);
                if (!embeddingDownloaded)
                    yield break;

                try
                {
                    CustomerPackageStore.VerifyFileSha256(
                        embeddingPath,
                        manifest.bulkEmbeddingIndexSha256,
                        "Bulk embedding index");
                }
                catch (Exception exception)
                {
                    Fail(store, exception);
                    yield break;
                }
            }

            try
            {
                ProgressChanged?.Invoke("Validating model and customer package compatibility...");
                ModelLoader.Load(modelPath);
                // ValidateDirectory verifies all hashes again and rejects vector/model mismatches
                // before the staged package can become active.
                store.ValidateDirectory(stagingDirectory);
                var activated = store.ActivateStaging();
                _installing = false;
                ProgressChanged?.Invoke(
                    activated.HasBulkRecognitionData
                        ? $"Customer package activated with {activated.BulkCatalogue.Count} bulk products."
                        : "Customer package activated.");
                InstallationCompleted?.Invoke(activated);
            }
            catch (Exception exception)
            {
                Fail(store, exception);
            }
        }

        private IEnumerator DownloadLargeFile(
            string url,
            string destinationPath,
            string displayName,
            int timeoutSeconds,
            long maximumBytes,
            CustomerPackageStore store,
            Action<bool> completed)
        {
            var succeeded = false;
            try
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
                request.downloadHandler = new DownloadHandlerFile(destinationPath)
                {
                    removeFileOnAbort = true
                };
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail(store, new IOException($"Unable to download {displayName}: {request.error}"));
                    yield break;
                }

                if (!File.Exists(destinationPath))
                {
                    Fail(store, new IOException($"Downloaded {displayName} was not written to disk."));
                    yield break;
                }

                var length = new FileInfo(destinationPath).Length;
                if (length <= 0 || length > maximumBytes)
                {
                    Fail(
                        store,
                        new IOException(
                            $"Downloaded {displayName} size {length} bytes is outside the allowed range (1-{maximumBytes})."));
                    yield break;
                }

                succeeded = true;
            }
            finally
            {
                completed?.Invoke(succeeded);
            }
        }

        private bool TryReadDownload(
            UnityWebRequest request,
            string displayName,
            CustomerPackageStore store,
            out byte[] bytes)
        {
            bytes = null;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(store, new IOException($"Unable to download {displayName}: {request.error}"));
                return false;
            }

            bytes = request.downloadHandler == null ? null : request.downloadHandler.data;
            if (bytes == null || bytes.Length == 0)
            {
                Fail(store, new IOException($"Downloaded {displayName} is empty."));
                return false;
            }

            return true;
        }

        private void Fail(CustomerPackageStore store, Exception exception)
        {
            store.DiscardStaging();
            _installing = false;
            InstallationFailed?.Invoke(exception.Message);
            Debug.LogError("Customer package installation failed: " + exception);
        }
    }
}
