#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class AndroidArm64PreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

#if UNITY_2021_2_OR_NEWER
        PlayerSettings.SetScriptingBackend(
            NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP
        );
#else
        PlayerSettings.SetScriptingBackend(
            BuildTargetGroup.Android,
            ScriptingImplementation.IL2CPP
        );
#endif

        PlayerSettings.Android.targetArchitectures =
            AndroidArchitecture.ARM64;

        AssetDatabase.SaveAssets();
    }
}

#endif
