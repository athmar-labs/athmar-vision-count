using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class GenericEmbeddingPilotBuilder
    {
        private const string ModelAssetPath = "Assets/Generated/Resources/AthmarGenericEmbedding.onnx";
        private const string OutputPath = "Build/AthmarVisionCount-SentisPilot.apk";

        public static void BuildAndroidPilot()
        {
            ProjectBootstrapper.EnsureProductionProject();
            ProductionReadinessValidator.ValidateOrThrow();

            AssetDatabase.ImportAsset(ModelAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var embeddingModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelAssetPath);
            if (embeddingModel == null)
            {
                throw new BuildFailedException(
                    "The prepared generic Sentis embedding model is missing. " +
                    "Run tools/prepare_generic_embedding_model.py before building the phone pilot.");
            }

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length != 1)
                throw new BuildFailedException("The phone pilot requires exactly one enabled bootstrap scene.");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Build");
            var previousBundleMode = EditorUserBuildSettings.buildAppBundle;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = OutputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Sentis phone pilot build failed with result {report.summary.result}.");
                if (!File.Exists(OutputPath) || new FileInfo(OutputPath).Length == 0)
                    throw new BuildFailedException("Sentis phone pilot APK is missing or empty after a successful build report.");

                using var stream = File.OpenRead(OutputPath);
                using var algorithm = SHA256.Create();
                var digest = algorithm.ComputeHash(stream);
                var hash = BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
                File.WriteAllText(OutputPath + ".sha256", hash + "  " + Path.GetFileName(OutputPath) + Environment.NewLine);
                Debug.Log($"Sentis phone pilot APK created: {OutputPath} sha256={hash}");
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundleMode;
            }
        }
    }
}
