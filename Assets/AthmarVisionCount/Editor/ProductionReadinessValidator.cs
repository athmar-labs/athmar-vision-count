using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class ProductionReadinessValidator
    {
        private const string RequiredUnityVersion = "6000.3.10f1";
        private const string RequiredConfigFileName = "AthmarVisionCountConfig.asset";

        [MenuItem("Athmar/Vision Count/Validate Production Readiness")]
        public static void ValidateFromMenu()
        {
            ProjectBootstrapper.EnsureProductionProject();
            var errors = CollectErrors();
            if (errors.Count == 0)
            {
                Debug.Log("Athmar Vision Count passed the automated production-readiness checks.");
                return;
            }

            foreach (var error in errors)
                Debug.LogError(error);
            throw new BuildFailedException("Production readiness validation failed. See Console for details.");
        }

        public static void ValidateOrThrow()
        {
            var errors = CollectErrors();
            if (errors.Count > 0)
                throw new BuildFailedException("Production readiness validation failed:\n- " + string.Join("\n- ", errors));
        }

        public static void BuildAndroidRelease()
        {
            ProjectBootstrapper.EnsureProductionProject();
            ValidateOrThrow();

            var keystorePath = RequireEnvironment("ANDROID_KEYSTORE_PATH");
            var keystorePassword = RequireEnvironment("ANDROID_KEYSTORE_PASS");
            var keyAlias = RequireEnvironment("ANDROID_KEY_ALIAS");
            var keyAliasPassword = RequireEnvironment("ANDROID_KEY_ALIAS_PASS");
            var outputPath = Environment.GetEnvironmentVariable("BUILD_OUTPUT_PATH");
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = Path.Combine("Build", "AthmarVisionCount.aab");

            keystorePath = Path.GetFullPath(keystorePath);
            outputPath = Path.GetFullPath(outputPath);
            if (!File.Exists(keystorePath))
                throw new BuildFailedException("Android keystore was not found at the configured path.");

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var previousUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            var previousKeystoreName = PlayerSettings.Android.keystoreName;
            var previousKeystorePassword = PlayerSettings.Android.keystorePass;
            var previousAliasName = PlayerSettings.Android.keyaliasName;
            var previousAliasPassword = PlayerSettings.Android.keyaliasPass;
            var previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;

            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasName = keyAlias;
                PlayerSettings.Android.keyaliasPass = keyAliasPassword;
                EditorUserBuildSettings.buildAppBundle = true;

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Android build failed with result {report.summary.result}.");
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                    throw new BuildFailedException("Android build reported success but the App Bundle is missing or empty.");

                WriteSha256(outputPath);
                Debug.Log($"Validated signed Android App Bundle created at {outputPath}.");
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = previousUseCustomKeystore;
                PlayerSettings.Android.keystoreName = previousKeystoreName;
                PlayerSettings.Android.keystorePass = previousKeystorePassword;
                PlayerSettings.Android.keyaliasName = previousAliasName;
                PlayerSettings.Android.keyaliasPass = previousAliasPassword;
                EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
            }
        }

        private static List<string> CollectErrors()
        {
            var errors = new List<string>();

            if (!Application.unityVersion.StartsWith(RequiredUnityVersion, StringComparison.Ordinal))
                errors.Add($"Use Unity {RequiredUnityVersion}; current editor is {Application.unityVersion}.");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                errors.Add("Install Android Build Support, SDK, NDK and OpenJDK for the pinned Unity editor.");

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (scenes.Length != 1)
                errors.Add("The production build must contain exactly one enabled bootstrap scene.");
            else if (!File.Exists(scenes[0].path))
                errors.Add("The enabled production scene does not exist on disk.");

            var applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrWhiteSpace(applicationId) || applicationId.Contains("Company.ProductName") || applicationId.Contains("DefaultCompany"))
                errors.Add("Set a unique Android application identifier, for example com.athmarlabs.visioncount.");

            if (string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion) || !IsSemanticVersion(PlayerSettings.bundleVersion))
                errors.Add("Set a production semantic version such as 1.0.0 in Player Settings.");
            if (PlayerSettings.Android.bundleVersionCode < 1)
                errors.Add("Android bundle version code must be at least 1.");
            if ((int)PlayerSettings.Android.minSdkVersion < (int)AndroidSdkVersions.AndroidApiLevel26)
                errors.Add("Android minimum SDK must be API 26 or higher for the supported pilot baseline.");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                errors.Add("Android production builds must use IL2CPP.");
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                errors.Add("Android production builds must include ARM64.");

            var config = FindConfig(out var configPath);
            if (config == null)
            {
                errors.Add("Create exactly one AppConfig asset for the production build.");
                return errors;
            }

            if (!configPath.Contains("/Resources/", StringComparison.Ordinal) || !string.Equals(Path.GetFileName(configPath), RequiredConfigFileName, StringComparison.Ordinal))
                errors.Add($"The AppConfig asset must be named {RequiredConfigFileName} and stored under a Resources folder.");
            if (config.ModelAsset == null)
                errors.Add("Assign the customer-approved, legally usable on-device model to AppConfig.");
            if (config.SkuCatalogueCsv == null)
                errors.Add("Assign the customer-approved SKU catalogue to AppConfig.");
            else
                ValidateCatalogue(config, errors);
            if (string.IsNullOrWhiteSpace(config.ModelVersion) || string.Equals(config.ModelVersion, "unassigned", StringComparison.OrdinalIgnoreCase))
                errors.Add("Set the immutable production model version.");
            if (string.IsNullOrWhiteSpace(config.CatalogueVersion) || string.Equals(config.CatalogueVersion, "unassigned", StringComparison.OrdinalIgnoreCase))
                errors.Add("Set the immutable production SKU catalogue version.");
            if (config.ModelInputWidth < 32 || config.ModelInputHeight < 32)
                errors.Add("Model input dimensions must be at least 32 by 32 pixels.");
            if (config.InferenceIntervalSeconds < 0.05f)
                errors.Add("Inference interval is too small for the supported mobile baseline.");
            if (config.MaxDetections < 1 || config.MaxDetections > 500)
                errors.Add("Maximum detections must be from 1 to 500.");
            if (!config.RequireHumanConfirmation)
                errors.Add("Human confirmation must remain mandatory before export or integration.");
            if (config.StoreCapturedImages)
                errors.Add("Captured image storage is disabled for the privacy-first release; perform a privacy review before enabling it.");
            if (config.RetentionDays < 1 || config.RetentionDays > 365)
                errors.Add("Set local retention between 1 and 365 days and document the customer-approved value.");
            if (string.IsNullOrWhiteSpace(config.PrivacyNoticeVersion) || string.Equals(config.PrivacyNoticeVersion, "draft", StringComparison.OrdinalIgnoreCase))
                errors.Add("Set AppConfig privacy notice version to the approved published version.");
            if (!string.Equals(config.DefaultLanguage, "ar", StringComparison.OrdinalIgnoreCase) && !string.Equals(config.DefaultLanguage, "en", StringComparison.OrdinalIgnoreCase))
                errors.Add("Default language must be ar or en.");

            if (config.NetworkSyncEnabled)
            {
                if (!Uri.TryCreate(config.SyncEndpoint, UriKind.Absolute, out var endpoint) ||
                    !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Network sync requires a valid HTTPS endpoint.");
                }
            }

            return errors;
        }

        private static void ValidateCatalogue(AppConfig config, ICollection<string> errors)
        {
            try
            {
                var catalogue = SkuCatalogue.Parse(config.SkuCatalogueCsv.text);
                if (catalogue.Count < 1)
                    errors.Add("The production SKU catalogue has no active products.");
            }
            catch (Exception exception)
            {
                errors.Add("The production SKU catalogue is invalid: " + exception.Message);
            }
        }

        private static AppConfig FindConfig(out string assetPath)
        {
            assetPath = string.Empty;
            var guids = AssetDatabase.FindAssets("t:AppConfig");
            if (guids.Length != 1)
                return null;
            assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<AppConfig>(assetPath);
        }

        private static bool IsSemanticVersion(string value)
        {
            var parts = value.Split('.');
            if (parts.Length != 3)
                return false;
            return parts.All(part => int.TryParse(part, out var number) && number >= 0);
        }

        private static void WriteSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            var digest = algorithm.ComputeHash(stream);
            var hash = BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            File.WriteAllText(path + ".sha256", hash + "  " + Path.GetFileName(path) + Environment.NewLine);
        }

        private static string RequireEnvironment(string variableName)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value))
                throw new BuildFailedException($"Required environment variable {variableName} is missing.");
            return value;
        }
    }
}
