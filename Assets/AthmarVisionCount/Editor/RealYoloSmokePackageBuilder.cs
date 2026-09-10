using System;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class RealYoloSmokePackageBuilder
    {
        private const string ModelFileName = "production.sentis";
        private const string CatalogueFileName = "sku_catalogue.csv";
        private const string ManifestFileName = "manifest.json";
        private const string GeneratedDirectory = "Assets/Generated/RealYoloSmoke";
        private const string ImportedOnnxPath = GeneratedDirectory + "/yolox_nano_unity.onnx";
        private const int InputSize = 416;
        private const int CandidateCount = 3549;
        private const int FeatureCount = 85;

        public static void Build()
        {
            var sourceOnnxArgument = ReadArgument("-realYoloOnnxPath");
            var baseUrl = RequireHttpsBaseUrl(ReadArgument("-realYoloPackageBaseUrl"));
            var rawSourceSha256 = CustomerPackageManifest.NormalizeSha256(
                ReadArgument("-realYoloSourceSha256"),
                "realYoloSourceSha256");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BuildFailedException("Unable to resolve the Unity project root directory.");

            var sourceOnnxPath = Path.IsPathRooted(sourceOnnxArgument)
                ? Path.GetFullPath(sourceOnnxArgument)
                : Path.GetFullPath(Path.Combine(projectRoot, sourceOnnxArgument));
            if (!File.Exists(sourceOnnxPath) || new FileInfo(sourceOnnxPath).Length == 0)
                throw new BuildFailedException("The prepared YOLOX-Nano ONNX file is missing or empty.");

            var outputDirectory = Path.Combine(projectRoot, "Build", "RealYoloSmokePackage");
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
            Directory.CreateDirectory(outputDirectory);

            PrepareImportedOnnx(projectRoot, sourceOnnxPath);

            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ImportedOnnxPath);
            if (modelAsset == null)
                throw new BuildFailedException("Unity did not import the prepared YOLOX ONNX file as a ModelAsset.");

            var importedModel = ModelLoader.Load(modelAsset);
            if (importedModel == null)
                throw new BuildFailedException("Unity Inference Engine could not load the prepared YOLOX model.");

            var modelPath = Path.Combine(outputDirectory, ModelFileName);
            ModelWriter.Save(modelPath, importedModel);
            if (!File.Exists(modelPath) || new FileInfo(modelPath).Length == 0)
                throw new BuildFailedException("production.sentis was not created.");

            var catalogueText = BuildCatalogue();
            var catalogue = SkuCatalogue.Parse(catalogueText);
            var cataloguePath = Path.Combine(outputDirectory, CatalogueFileName);
            File.WriteAllText(cataloguePath, catalogueText, new UTF8Encoding(false));

            var inferenceSummary = ValidateSentisExecution(modelPath, catalogue);
            var preparedOnnxSha256 = CustomerPackageStore.ComputeFileSha256(sourceOnnxPath);

            var manifest = new CustomerPackageManifest
            {
                schemaVersion = 1,
                customerCode = "ATHMAR-YOLOX-SMOKE",
                customerName = "Athmar YOLOX Smoke Test",
                defaultLanguage = "ar",
                modelVersion = "yolox-nano-coco-0.1.1rc0-athmar-v1",
                catalogueVersion = "coco-smoke-4-v1",
                privacyNoticeVersion = "smoke-test-1",
                modelUrl = baseUrl + "/" + ModelFileName,
                modelSha256 = CustomerPackageStore.ComputeFileSha256(modelPath),
                catalogueUrl = baseUrl + "/" + CatalogueFileName,
                catalogueSha256 = CustomerPackageStore.ComputeFileSha256(cataloguePath),
                modelInputWidth = InputSize,
                modelInputHeight = InputSize,
                modelInputLayout = "Nchw",
                outputTensorIndex = 0,
                outputTensorLayout = "RowsFirst",
                outputHasObjectness = true,
                outputCoordinatesNormalized = false,
                maxDetections = 100,
                preferGpu = true,
                inferenceIntervalSeconds = 0.25f,
                minimumConfidence = 0.30f,
                duplicateIouThreshold = 0.45f,
                nonMaxSuppressionIouThreshold = 0.45f,
                trackTtlSeconds = 1.25f,
                retentionDays = 7,
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
                $"Manifest URL: {baseUrl}/{ManifestFileName}\n" +
                $"Manifest SHA-256: {manifestSha256}\n" +
                "Smoke objects: bottle, cup, cell phone, book\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(outputDirectory, "MODEL_SOURCE.txt"),
                "Model: YOLOX-Nano COCO\n" +
                "Upstream: Megvii-BaseDetection/YOLOX\n" +
                "License: Apache-2.0\n" +
                "Release: 0.1.1rc0\n" +
                "Source ONNX: https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx\n" +
                $"Source ONNX SHA-256: {rawSourceSha256}\n" +
                $"Prepared ONNX SHA-256: {preparedOnnxSha256}\n" +
                "Adaptation: RGB 0..1 to BGR 0..255 plus YOLOX grid/stride decoding. Trained weights are unchanged.\n" +
                "Purpose: generic camera/inference/counting smoke test only; not customer SKU acceptance evidence.\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(outputDirectory, "validation.txt"),
                inferenceSummary + "\n" +
                $"Model SHA-256: {manifest.modelSha256}\n" +
                $"Catalogue SHA-256: {manifest.catalogueSha256}\n" +
                $"Manifest SHA-256: {manifestSha256}\n",
                new UTF8Encoding(false));

            var validationStore = new CustomerPackageStore(Path.Combine(projectRoot, ".ci", "real-yolo-package-validation"));
            var snapshot = validationStore.ValidateDirectory(outputDirectory);
            if (!string.Equals(snapshot.Configuration.CustomerCode, manifest.customerCode, StringComparison.Ordinal))
                throw new BuildFailedException("The generated package did not reload with the expected customer code.");

            Debug.Log(
                $"Real YOLOX smoke package generated at {outputDirectory}. " +
                $"Manifest SHA-256: {manifestSha256}. {inferenceSummary}");
        }

        private static void PrepareImportedOnnx(string projectRoot, string sourceOnnxPath)
        {
            var generatedAbsoluteDirectory = Path.Combine(projectRoot, GeneratedDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(generatedAbsoluteDirectory);
            var importedAbsolutePath = Path.Combine(projectRoot, ImportedOnnxPath.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(sourceOnnxPath, importedAbsolutePath, true);
            AssetDatabase.ImportAsset(
                ImportedOnnxPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ValidateSentisExecution(string modelPath, SkuCatalogue catalogue)
        {
            var model = ModelLoader.Load(modelPath);
            if (model == null)
                throw new BuildFailedException("The generated Sentis model could not be reloaded.");

            using var worker = new Worker(model, BackendType.CPU);
            using var input = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
            worker.Schedule(input);

            if (!(worker.PeekOutput(0) is Tensor<float> outputOnDevice))
                throw new BuildFailedException("The YOLOX Sentis output is not a float tensor.");

            using var output = outputOnDevice.ReadbackAndClone();
            if (output == null || output.shape.rank != 3 || output.shape[0] != 1 ||
                output.shape[1] != CandidateCount || output.shape[2] != FeatureCount)
            {
                throw new BuildFailedException(
                    $"Unexpected YOLOX Sentis output shape: {(output == null ? "null" : output.shape.ToString())}. " +
                    $"Expected [1,{CandidateCount},{FeatureCount}].");
            }

            var values = output.DownloadToArray();
            if (values == null || values.Length != CandidateCount * FeatureCount)
                throw new BuildFailedException("The YOLOX Sentis output could not be downloaded completely.");

            var detections = YoloOutputDecoder.Decode(
                values,
                CandidateCount,
                FeatureCount,
                DetectionTensorLayout.RowsFirst,
                true,
                false,
                InputSize,
                InputSize,
                0.30f,
                0.45f,
                100,
                catalogue);

            return $"CPU Sentis validation passed: input [1,3,{InputSize},{InputSize}], " +
                   $"output [1,{CandidateCount},{FeatureCount}], zero-input smoke detections {detections.Count}.";
        }

        private static string BuildCatalogue()
        {
            return
                "label_index,sku,name_en,name_ar,active\n" +
                "39,COCO-BOTTLE,Bottle,زجاجة,true\n" +
                "41,COCO-CUP,Cup,كوب,true\n" +
                "67,COCO-PHONE,Cell Phone,هاتف محمول,true\n" +
                "73,COCO-BOOK,Book,كتاب,true\n";
        }

        private static string ReadArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            var prefix = name + "=";
            for (var index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal) && index + 1 < arguments.Length)
                    return arguments[index + 1];
                if (arguments[index].StartsWith(prefix, StringComparison.Ordinal))
                    return arguments[index].Substring(prefix.Length);
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
                throw new BuildFailedException("The real YOLO package base URL must be a clean absolute HTTPS URL.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }
    }
}
