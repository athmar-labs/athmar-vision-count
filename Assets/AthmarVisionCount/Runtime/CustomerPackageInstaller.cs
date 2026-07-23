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

            ProgressChanged?.Invoke("Downloading and verifying SKU catalogue...");
            byte[] catalogueBytes;
            using (var request = UnityWebRequest.Get(manifest.catalogueUrl))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (!TryReadDownload(request, "SKU catalogue", store, out catalogueBytes))
                    yield break;
            }

            try
            {
                CustomerPackageStore.VerifyBytesSha256(catalogueBytes, manifest.catalogueSha256, "SKU catalogue");
                var cataloguePath = Path.Combine(stagingDirectory, CustomerPackageStore.CatalogueFileName);
                File.WriteAllBytes(cataloguePath, catalogueBytes);
                SkuCatalogue.Parse(Encoding.UTF8.GetString(catalogueBytes));

                ProgressChanged?.Invoke("Validating model compatibility...");
                ModelLoader.Load(modelPath);
                var activated = store.ActivateStaging();
                _installing = false;
                ProgressChanged?.Invoke("Customer package activated.");
                InstallationCompleted?.Invoke(activated);
            }
            catch (Exception exception)
            {
                Fail(store, exception);
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
