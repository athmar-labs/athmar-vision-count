using System;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    /// <summary>
    /// Builds a real customer package from a trained ONNX detection model and a SKU catalogue CSV.
    /// Converts ONNX to Sentis in-editor, then emits manifest + install values exactly like the demo builder.
    /// </summary>
    public static class AthmarCustomerPackageBuilder
    {
        private const string ModelFileName = "production.sentis";
        private const string CatalogueFileName = "sku_catalogue.csv";
        private const string ManifestFileName = "manifest.json";

        public static void Build()
        {
            var baseUrl = RequireHttpsBaseUrl(ReadArgument("-athmarPackageBaseUrl"));
            var onnxPath = ReadArgument("-athmarOnnxPath");
            var cataloguePath = ReadArgument("-athmarCataloguePath");
            var customerCode = ReadArgument("-athmarCustomerCode");
            var customerName = ReadArgument("-athmarCustomerName");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BuildFailedException("Unable to resolve the Unity project root directory.");

            // تحويل المسارات النسبية إلى مطلقة بالنسبة لجذر المشروع
            if (!Path.IsPathRooted(onnxPath))
                onnxPath = Path.Combine(projectRoot, onnxPath);
            if (!Path.IsPathRooted(cataloguePath))
                cataloguePath = Path.Combine(projectRoot, cataloguePath);

            if (!File.Exists(onnxPath))
                throw new BuildFailedException($"ONNX model not found at {onnxPath}.");
            if (!File.Exists(cataloguePath))
                throw new BuildFailedException($"SKU catalogue not found at {cataloguePath}.");

            var outputDirectory = Path.Combine(projectRoot, "Build", "AthmarCustomerPackage");
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
            Directory.CreateDirectory(outputDirectory);

            // 1) تحويل ONNX → Sentis
            var sentisPath = Path.Combine(outputDirectory, ModelFileName);
            ConvertOnnxToSentis(onnxPath, sentisPath);

            // 2) نسخ الكتالوج (مع التحقق من صحته)
            var catalogueText = File.ReadAllText(cataloguePath, Encoding.UTF8);
            var catalogue = SkuCatalogue.Parse(catalogueText);
            var catalogueOut = Path.Combine(outputDirectory, CatalogueFileName);
            File.WriteAllText(catalogueOut, catalogueText, new UTF8Encoding(false));

            // 3) أبعاد النموذج — ثابتة (YOLOv8 detection 640×640)
            var modelInputWidth = 640;
            var modelInputHeight = 640;

            // 4) manifest
            var manifest = new CustomerPackageManifest
            {
                customerCode = customerCode,
                customerName = customerName,
                defaultLanguage = "ar",
                modelVersion = "detect-23cls-v1",
                catalogueVersion = "detect-23cls-v1",
                privacyNoticeVersion = "approved-1",
                modelUrl = baseUrl + "/" + ModelFileName,
                modelSha256 = CustomerPackageStore.ComputeFileSha256(sentisPath),
                catalogueUrl = baseUrl + "/" + CatalogueFileName,
                catalogueSha256 = CustomerPackageStore.ComputeFileSha256(catalogueOut),
                modelInputWidth = modelInputWidth,
                modelInputHeight = modelInputHeight,
                modelInputLayout = "Nchw",
                outputTensorIndex = 0,
                outputTensorLayout = "ChannelsFirst",
                outputHasObjectness = false,
                outputCoordinatesNormalized = true,
                maxDetections = 100,
                preferGpu = true,
                inferenceIntervalSeconds = 0.25f,
                minimumConfidence = 0.5f,
                duplicateIouThreshold = 0.45f,
                nonMaxSuppressionIouThreshold = 0.45f,
                trackTtlSeconds = 1.25f,
                retentionDays = 30,
                networkSyncEnabled = false,
                syncEndpoint = string.Empty
            };
            manifest.Validate();

            var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n", new UTF8Encoding(false));
            var manifestSha256 = CustomerPackageStore.ComputeFileSha256(manifestPath);
            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.sha256"),
                manifestSha256 + "\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(outputDirectory, "install-values.txt"),
                $"Manifest URL: {baseUrl}/{ManifestFileName}\nManifest SHA-256: {manifestSha256}\n",
                new UTF8Encoding(false));

            Debug.Log($"Athmar customer package generated at {outputDirectory}. Classes: {catalogue.Count}. Manifest SHA-256: {manifestSha256}");
        }

        private static void ConvertOnnxToSentis(string onnxPath, string sentisPath)
        {
            // نسخ ONNX إلى داخل المشروع ثم استيراده كـ ModelAsset (طريقة Sentis الصحيحة)
            const string assetRelativePath = "Assets/Generated/Resources/AthmarFoodDetection.onnx";
            var assetAbsolutePath = Path.Combine(Application.dataPath, "Generated", "Resources", "AthmarFoodDetection.onnx");
            Directory.CreateDirectory(Path.GetDirectoryName(assetAbsolutePath) ?? string.Empty);
            File.Copy(onnxPath, assetAbsolutePath, true);

            AssetDatabase.ImportAsset(assetRelativePath, ImportAssetOptions.ForceSynchronousImport);
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetRelativePath);
            if (modelAsset == null)
                throw new BuildFailedException("Failed to import ONNX as a Sentis ModelAsset.");

            var model = ModelLoader.Load(modelAsset);
            ModelWriter.Save(sentisPath, model);
            Debug.Log($"Converted ONNX → Sentis: {Path.GetFileName(sentisPath)}");
        }

        private static string ReadArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return arguments[index + 1];
            }

            throw new BuildFailedException($"Required command-line argument {name} is missing.");
        }

        private static string RequireHttpsBaseUrl(string value)
        {
            value = value?.Trim().TrimEnd('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new BuildFailedException("The package base URL must be a clean absolute HTTPS URL.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }
    }
}
