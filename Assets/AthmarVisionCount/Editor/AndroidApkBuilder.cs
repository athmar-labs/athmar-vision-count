using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class AndroidApkBuilder
    {
        public static void BuildSignedApk()
        {
            ProjectBootstrapper.EnsureProductionProject();
            ProductionReadinessValidator.ValidateOrThrow();

            var keystorePath = RequireFirstEnvironment("ANDROID_KEYSTORE_PATH", "ANDROID_KEYSTORE_NAME");
            var keystorePassword = RequireEnvironment("ANDROID_KEYSTORE_PASS");
            var keyAlias = RequireFirstEnvironment("ANDROID_KEY_ALIAS", "ANDROID_KEYALIAS_NAME");
            var keyAliasPassword = RequireFirstEnvironment("ANDROID_KEY_ALIAS_PASS", "ANDROID_KEYALIAS_PASS");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BuildFailedException("Unable to resolve the Unity project root directory.");

            if (!Path.IsPathRooted(keystorePath))
                keystorePath = Path.Combine(projectRoot, keystorePath);

            keystorePath = Path.GetFullPath(keystorePath);
            if (!File.Exists(keystorePath))
                throw new BuildFailedException("Android keystore was not found at the configured path.");

            var outputPath = Path.Combine(projectRoot, "Build", "AthmarVisionCount.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

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
            var previousArchitectures = PlayerSettings.Android.targetArchitectures;
            var previousScriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);

            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasName = keyAlias;
                PlayerSettings.Android.keyaliasPass = keyAliasPassword;
                EditorUserBuildSettings.buildAppBundle = false;

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Android APK build failed with result {report.summary.result}.");
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                    throw new BuildFailedException("Android APK build reported success but the APK is missing or empty.");

                WriteSha256(outputPath);
                Debug.Log($"Validated signed Android APK created at {outputPath}.");
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = previousUseCustomKeystore;
                PlayerSettings.Android.keystoreName = previousKeystoreName;
                PlayerSettings.Android.keystorePass = previousKeystorePassword;
                PlayerSettings.Android.keyaliasName = previousAliasName;
                PlayerSettings.Android.keyaliasPass = previousAliasPassword;
                EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
                PlayerSettings.Android.targetArchitectures = previousArchitectures;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, previousScriptingBackend);
            }
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
            return value.Trim();
        }

        private static string RequireFirstEnvironment(params string[] variableNames)
        {
            foreach (var variableName in variableNames)
            {
                var value = Environment.GetEnvironmentVariable(variableName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            throw new BuildFailedException("Required environment variable is missing. Expected one of: " + string.Join(", ", variableNames) + ".");
        }
    }
}
