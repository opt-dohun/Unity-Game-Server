using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProjectSetup
{
    [MenuItem("Crimson Tide/Create Boss Battle Scene")]
    public static void Create()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerSettings.productName = "Crimson Tide";
        PlayerSettings.companyName = "Crimson Tide Prototype";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 5.625f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .13f, .2f);
        cameraObject.transform.position = new Vector3(0, 0, -10);
        cameraObject.AddComponent<AudioListener>();
        new GameObject("Boss Battle").AddComponent<TurnBasedBossBattle>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/BossBattle.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/BossBattle.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Crimson Tide: BossBattle scene created and added to Build Settings.");
    }

    [MenuItem("Crimson Tide/Build macOS Game")]
    public static void BuildMac()
    {
        string output = "Builds/CrimsonTide-TurnBased.app";
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/BossBattle.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Crimson Tide macOS build failed: " + report.summary.result);
        Debug.Log("Crimson Tide macOS build ready at " + output);
    }
}
