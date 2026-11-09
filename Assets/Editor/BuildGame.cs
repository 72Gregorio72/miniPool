using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// MiniPool > Build Windows: a player build in Builds/MiniPool (zip the folder and send it).
// The game makes its materials in code (Shader.Find), and a build only ships shaders (and keyword variants) that some asset uses:
// the materials in Resources/BuildShaders exist just to pull in URP Lit (opaque + transparent, for the ghosts) and URP Unlit (aim lines).
public static class BuildGame
{
    const string ShaderDir = "Assets/Resources/BuildShaders", Output = "Builds/MiniPool/MiniPool.exe";

    [MenuItem("MiniPool/Build Windows")]
    static void Build()
    {
        IncludeShaders();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Level01.unity" },
            locationPathName = Output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"MiniPool build ok: {Path.GetFullPath(Output)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            EditorUtility.RevealInFinder(Output);
        }
        else EditorUtility.DisplayDialog("Build fallita", $"{report.summary.result}: {report.summary.totalErrors} errori, vedi la Console.", "OK");
    }

    static void IncludeShaders()
    {
        Directory.CreateDirectory(ShaderDir);
        var lit = Save("Lit", new Material(Shader.Find("Universal Render Pipeline/Lit")));
        Save("LitTransparent", new Material(BallArt.Ghost(lit)));
        Save("Unlit", new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        AssetDatabase.SaveAssets();
    }

    static Material Save(string name, Material m)
    {
        var path = $"{ShaderDir}/{name}.mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (old) return old;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
