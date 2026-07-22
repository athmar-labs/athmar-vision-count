using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class ProductionReadinessValidator
    {
        private const string RequiredUnityVersion = "6000.3.10f1";

        [MenuItem("Athmar/Vision Count/Validate Production Readiness")]
        public static void ValidateFromMenu()
        {
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
            if (scenes.Length == 0)
                errors.Add("Add at least one enabled production scene to Build Settings.");

            var applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrWhiteSpace(applicationId) || applicationId.Contains("Company.ProductName") || applicationId.Contains("DefaultCompany"))
                errors.Add("Set a unique Android application identifier, for example com.athmarlabs.visioncount.");

            if (string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "0.1")
                errors.Add("Set a production semantic version in Player Settings.");

            if (PlayerSettings.Android.bundleVersionCode < 1)
                errors.Add("Android bundle version code must be at least 1.");

            if ((int)PlayerSettings.Android.minSdkVersion < (int)AndroidSdkVersions.AndroidApiLevel26)
                errors.Add("Android minimum SDK must be API 26 or higher for the supported pilot baseline.");

            var config = FindConfig();
            if (config == null)
            {
                errors.Add("Create exactly one AppConfig asset and include it in the production scene.");
                return errors;
            }

            if (config.ModelAsset == null)
                errors.Add("Assign the customer-approved, legally usable on-device model to AppConfig.");
            if (config.SkuCatalogueCsv == null)
                errors.Add("Assign the customer-approved SKU catalogue to AppConfig.");
            if (!config.RequireHumanConfirmation)
                errors.Add("Human confirmation must remain mandatory before export or integration.");
            if (config.StoreCapturedImages)
                errors.Add("Captured image storage is disabled for the privacy-first release; perform a privacy review before enabling it.");
            if (config.RetentionDays < 1 || config.RetentionDays > 365)
                errors.Add("Set local retention between 1 and 365 days and document the customer-approved value.");
            if (string.IsNullOrWhiteSpace(config.PrivacyNoticeVersion) || string.Equals(config.PrivacyNoticeVersion, "draft", StringComparison.OrdinalIgnoreCase))
                errors.Add("Set AppConfig privacy notice version to the approved published version.");

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

        private static AppConfig FindConfig()
        {
            var guids = AssetDatabase.FindAssets("t:AppConfig");
            if (guids.Length != 1)
                return null;
            return AssetDatabase.LoadAssetAtPath<AppConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
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
