using System;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor.Build;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class DemoCustomerPackageBuilder
    {
        private const string ModelFileName = "production.sentis";
        private const string CatalogueFileName = "sku_catalogue.csv";
        private const string ManifestFileName = "manifest.json";

        public static void Build()
        {
            var baseUrl = RequireHttpsBaseUrl(ReadArgument("-demoPackageBaseUrl"));
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BuildFailedException("Unable to resolve the Unity project root directory.");

            var outputDirectory = Path.Combine(projectRoot, "Build", "DemoCustomerPackage");
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
            Directory.CreateDirectory(outputDirectory);

            var modelPath = Path.Combine(outputDirectory, ModelFileName);
            CreateAndValidateModel(modelPath);

            var cataloguePath = Path.Combine(outputDirectory, CatalogueFileName);
            var catalogueText = "label_index,sku,name_en,name_ar,active\n0,DEMO-001,Demo Product,منتج تجريبي,true\n";
            File.WriteAllText(cataloguePath, catalogueText, new UTF8Encoding(false));
            SkuCatalogue.Parse(catalogueText);

            var manifest = new CustomerPackageManifest
            {
                customerCode = "ATHMAR-DEMO",
                customerName = "Athmar Demo Customer",
                defaultLanguage = "ar",
                modelVersion = "1.0.0-demo",
                catalogueVersion = "1.0.0-demo",
                privacyNoticeVersion = "demo-1",
                modelUrl = baseUrl + "/" + ModelFileName,
                modelSha256 = CustomerPackageStore.ComputeFileSha256(modelPath),
                catalogueUrl = baseUrl + "/" + CatalogueFileName,
                catalogueSha256 = CustomerPackageStore.ComputeFileSha256(cataloguePath),
                modelInputWidth = 32,
                modelInputHeight = 32,
                modelInputLayout = "Nchw",
                outputTensorIndex = 0,
                outputTensorLayout = "ChannelsFirst",
                outputHasObjectness = false,
                outputCoordinatesNormalized = true,
                maxDetections = 1,
                preferGpu = false,
                inferenceIntervalSeconds = 1f,
                minimumConfidence = 0.5f,
                duplicateIouThreshold = 0.45f,
                nonMaxSuppressionIouThreshold = 0.45f,
                trackTtlSeconds = 2.5f,
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

            Debug.Log($"Demo customer package generated at {outputDirectory}. Manifest SHA-256: {manifestSha256}");
        }

        private static void CreateAndValidateModel(string path)
        {
            var inputShape = new TensorShape(1, 3, 32, 32);
            var outputShape = new TensorShape(1, 5, 1);
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(inputShape, "image");
            var signal = Functional.ReduceMean(input, new[] { 1, 2, 3 }) * 0.000001f;
            var baseline = Functional.Constant(outputShape, new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.95f });
            graph.AddOutput(baseline + signal, "detections");
            ModelWriter.Save(path, graph.Compile());

            var model = ModelLoader.Load(path);
            using var worker = new Worker(model, BackendType.CPU);
            using var inputTensor = new Tensor<float>(inputShape);
            worker.Schedule(inputTensor);
            if (!(worker.PeekOutput() is Tensor<float> outputOnDevice))
                throw new BuildFailedException("The demo model output is not a float tensor.");
            using var output = outputOnDevice.ReadbackAndClone();
            if (output.shape.rank != 3 || output.shape[0] != 1 || output.shape[1] != 5 || output.shape[2] != 1)
                throw new BuildFailedException($"The demo model output shape is invalid: {output.shape}.");

            var detections = YoloOutputDecoder.Decode(
                output.DownloadToArray(),
                output.shape[-2],
                output.shape[-1],
                DetectionTensorLayout.ChannelsFirst,
                false,
                true,
                32,
                32,
                0.5f,
                0.45f,
                1,
                SkuCatalogue.Parse("label_index,sku,name_en,name_ar,active\n0,DEMO-001,Demo Product,منتج تجريبي,true\n"));
            if (detections.Count != 1 || detections[0].Sku != "DEMO-001")
                throw new BuildFailedException("The demo model did not produce the expected detection.");
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
                throw new BuildFailedException("The demo package base URL must be a clean absolute HTTPS URL.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }
    }
}
