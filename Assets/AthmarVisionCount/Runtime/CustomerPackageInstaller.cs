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
            try
            {
                ProgressChanged?.Invoke("Downloading and verifying customer manifest...");
                byte[] manifestBytes;
                using (var request = UnityWebRequest.Get(manifestUrl))
                {
                    request.timeout = 60;
                    yield return request.SendWebRequest();
                    EnsureSuccessful(request, "customer manifest");
                    manifestBytes = request.downloadHandler.data;
                }

                CustomerPackageStore.VerifyBytesSha256(manifestBytes, expectedManifestSha256, "Customer manifest");
                var manifestJson = Encoding.UTF8.GetString(manifestBytes);
                var manifest = CustomerPackageManifest.Parse(manifestJson);
                var stagingDirectory = store.PrepareStagingDirectory();
                File.WriteAllBytes(Path.Combine(stagingDirectory, CustomerPackageStore.ManifestFileName), manifestBytes);

                ProgressChanged?.Invoke("Downloading and verifying inference model...");
                byte[] modelBytes;
                using (var request = UnityWebRequest.Get(manifest.modelUrl))
                {
                    request.timeout = 300;
                    yield return request.SendWebRequest();
                    EnsureSuccessful(request, "inference model");
                    modelBytes = request.downloadHandler.data;
                }

                CustomerPackageStore.VerifyBytesSha256(modelBytes, manifest.modelSha256, "Inference model");
                var modelPath = Path.Combine(stagingDirectory, CustomerPackageStore.ModelFileName);
                File.WriteAllBytes(modelPath, modelBytes);
                modelBytes = null;

                ProgressChanged?.Invoke("Downloading and verifying SKU catalogue...");
                byte[] catalogueBytes;
                using (var request = UnityWebRequest.Get(manifest.catalogueUrl))
                {
                    request.timeout = 60;
                    yield return request.SendWebRequest();
                    EnsureSuccessful(request, "SKU catalogue");
                    catalogueBytes = request.downloadHandler.data;
                }

                CustomerPackageStore.VerifyBytesSha256(catalogueBytes, manifest.catalogueSha256, "SKU catalogue");
                var cataloguePath = Path.Combine(stagingDirectory, CustomerPackageStore.CatalogueFileName);
                File.WriteAllBytes(cataloguePath, catalogueBytes);
                SkuCatalogue.Parse(Encoding.UTF8.GetString(catalogueBytes));

                ProgressChanged?.Invoke("Validating model compatibility...");
                ModelLoader.Load(modelPath);
                var activated = store.ActivateStaging();
                ProgressChanged?.Invoke("Customer package activated.");
                InstallationCompleted?.Invoke(activated);
            }
            catch (Exception exception)
            {
                store.DiscardStaging();
                InstallationFailed?.Invoke(exception.Message);
                Debug.LogError("Customer package installation failed: " + exception);
            }
            finally
            {
                _installing = false;
            }
        }

        private static void EnsureSuccessful(UnityWebRequest request, string displayName)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"Unable to download {displayName}: {request.error}");
            if (request.downloadHandler == null || request.downloadHandler.data == null || request.downloadHandler.data.Length == 0)
                throw new IOException($"Downloaded {displayName} is empty.");
        }
    }
}
