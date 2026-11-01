using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage シーンを作り直す。メニュー「Tools/Build Stage Scene」またはバッチモードの -executeMethod から呼ぶ。
/// </summary>
public static class StageSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Stage.unity";

    [MenuItem("Tools/Build Stage Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.fieldOfView = 30f;
        camera.transform.position = new Vector3(0f, 1.0f, 2.4f);
        camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        camera.gameObject.AddComponent<AudioListener>();

        var key = new GameObject("Key Light").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.1f;
        key.transform.rotation = Quaternion.Euler(30f, 160f, 0f);

        new GameObject("Singing Stage").AddComponent<SingingStage>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"Stage scene saved: {ScenePath}");
    }
}
