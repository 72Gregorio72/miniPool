using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Saves every module's procedural look (LevelBuilder.ModuleVisual) as Resources/Modules/<name>.prefab, with its materials
// as assets in Modules/Materials, so look and scripts are edited in Unity. Existing prefabs are never overwritten.
[InitializeOnLoad]
static class ModulePrefabCreator
{
    const string Folder = "Assets/Resources/Modules", MatFolder = Folder + "/Materials";

    static ModulePrefabCreator()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !AssetDatabase.IsValidFolder(Folder)) Create();
        };
    }

    [MenuItem("MiniPool/Rigenera tutti i prefab moduli")]
    static void Regenerate()
    {
        if (!EditorUtility.DisplayDialog("Prefab moduli", "Ricreare tutti i prefab dei moduli? Le modifiche fatte ai prefab vanno perse (i materiali restano).", "Rigenera", "Annulla")) return;
        foreach (var name in LevelBuilder.ModuleNames.Values.Distinct()) AssetDatabase.DeleteAsset($"{Folder}/{name}.prefab");
        Create();
    }

    [MenuItem("MiniPool/Crea prefab moduli mancanti")]
    static void Create()
    {
        Directory.CreateDirectory(MatFolder);
        AssetDatabase.Refresh();
        int made = 0;
        foreach (var group in LevelBuilder.ModuleNames.GroupBy(kv => kv.Value))
        {
            string path = $"{Folder}/{group.Key}.prefab";
            if (File.Exists(path)) continue;
            var go = LevelBuilder.ModuleVisual(group.First().Key);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(SaveMat).ToArray();
            foreach (var fan in go.GetComponentsInChildren<Fan>(true)) fan.airMat = SaveMat(fan.airMat);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            made++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"MiniPool: {made} prefab moduli creati in {Folder}");
    }

    // runtime material -> asset (one per name; unnamed ones by color), reused if it already exists
    static Material SaveMat(Material m)
    {
        if (!m || AssetDatabase.Contains(m)) return m;
        // unnamed runtime materials carry the shader's name ("Universal Render Pipeline/Lit")
        string name = string.IsNullOrEmpty(m.name) || m.name == m.shader.name ? "Color_" + ColorUtility.ToHtmlStringRGB(m.color) : m.name;
        string path = $"{MatFolder}/{name}.mat";
        var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!asset)
        {
            AssetDatabase.CreateAsset(asset = new Material(m), path);
        }
        return asset;
    }
}
