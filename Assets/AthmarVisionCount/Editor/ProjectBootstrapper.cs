using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AthmarLabs.VisionCount.Editor
{
    public static class ProjectBootstrapper
    {
        private const string GeneratedRoot = "Assets/Generated";
        private const string ConfigDirectory = GeneratedRoot + "/Resources";
        private const string ConfigPath = ConfigDirectory + "/AthmarVisionCountConfig.asset";
        private const string SceneDirectory = GeneratedRoot + "/Scenes";
        private const string ScenePath = SceneDirectory + "/Main.unity";

        [MenuItem("Athmar/Vision Count/Prepare Production Project")]
        public static void EnsureProductionProject()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureDirectory(ConfigDirectory);
            EnsureDirectory(SceneDirectory);

            var config = AssetDatabase.LoadAssetAtPath<AppConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AppConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            ConfigureAsset(config);
            EnsureProductionScene();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Athmar Vision Count generic runtime-configured project settings were prepared.");
        }

        private static void ConfigureAsset(AppConfig config)
        {
            var serialized = new SerializedObject(config);
            SetString(serialized, "applicationDisplayName", ReadEnvironment("ATHMAR_PRODUCT_NAME", "Athmar Vision Count"));
            SetString(serialized, "customerCode", "runtime-configured");
            SetString(serialized, "defaultLanguage", ReadEnvironment("ATHMAR_DEFAULT_LANGUAGE", "ar"));
            SetObject(serialized, "modelAsset", null);
            SetObject(serialized, "skuCatalogueCsv", null);
            SetString(serialized, "modelVersion", "runtime");
            SetString(serialized, "catalogueVersion", "runtime");
            SetInteger(serialized, "modelInputWidth", 640);
            SetInteger(serialized, "modelInputHeight", 640);
            SetEnum(serialized, "modelInputLayout", "Nchw", typeof(ModelInputLayout));
            SetInteger(serialized, "outputTensorIndex", 0);
            SetEnum(serialized, "outputTensorLayout", "ChannelsFirst", typeof(DetectionTensorLayout));
            SetBoolean(serialized, "outputHasObjectness", false);
            SetBoolean(serialized, "outputCoordinatesNormalized", false);
            SetInteger(serialized, "maxDetections", 100);
            SetBoolean(serialized, "preferGpu", true);
            SetFloat(serialized, "inferenceIntervalSeconds", 0.25f);
            SetFloat(serialized, "minimumConfidence", 0.65f);
            SetFloat(serialized, "duplicateIouThreshold", 0.45f);
            SetFloat(serialized, "nonMaxSuppressionIouThreshold", 0.45f);
            SetFloat(serialized, "trackTtlSeconds", 1.25f);
            SetBoolean(serialized, "requireHumanConfirmation", true);
            SetBoolean(serialized, "storeCapturedImages", false);
            SetInteger(serialized, "retentionDays", ReadInteger("ATHMAR_RETENTION_DAYS", 30, 1, 365));
            SetString(serialized, "privacyNoticeVersion", ReadEnvironment("ATHMAR_PRIVACY_NOTICE_VERSION", "1.0"));
            SetBoolean(serialized, "networkSyncEnabled", false);
            SetString(serialized, "syncEndpoint", string.Empty);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void EnsureProductionScene()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Athmar Labs";
            PlayerSettings.productName = ReadEnvironment("ATHMAR_PRODUCT_NAME", "Athmar Vision Count");
            PlayerSettings.bundleVersion = ReadEnvironment("ATHMAR_APP_VERSION", "1.0.0");
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                ReadEnvironment("ATHMAR_ANDROID_APPLICATION_ID", "com.athmarlabs.visioncount"));
            PlayerSettings.Android.bundleVersionCode = ReadInteger("ATHMAR_ANDROID_VERSION_CODE", 1, 1, int.MaxValue);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.runInBackground = false;
        }

        private static void EnsureDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void SetObject(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            RequireProperty(serialized, name).objectReferenceValue = value;
        }

        private static void SetString(SerializedObject serialized, string name, string value)
        {
            RequireProperty(serialized, name).stringValue = value ?? string.Empty;
        }

        private static void SetInteger(SerializedObject serialized, string name, int value)
        {
            RequireProperty(serialized, name).intValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string name, float value)
        {
            RequireProperty(serialized, name).floatValue = value;
        }

        private static void SetBoolean(SerializedObject serialized, string name, bool value)
        {
            RequireProperty(serialized, name).boolValue = value;
        }

        private static void SetEnum(SerializedObject serialized, string name, string value, Type enumType)
        {
            if (!Enum.TryParse(enumType, value, true, out var parsed))
                throw new InvalidOperationException($"Value '{value}' is not valid for {enumType.Name}.");
            RequireProperty(serialized, name).enumValueIndex = Convert.ToInt32(parsed, CultureInfo.InvariantCulture);
        }

        private static SerializedProperty RequireProperty(SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException($"AppConfig serialized property '{name}' was not found.");
            return property;
        }

        private static string ReadEnvironment(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int ReadInteger(string name, int fallback, int minimum, int maximum)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
                throw new InvalidOperationException($"Environment variable {name} must be an integer from {minimum} to {maximum}.");
            return parsed;
        }
    }
}
