using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates scenes through the Unity API (never hand-edited YAML) so it is safe while the editor is open.
[InitializeOnLoad]
static class LevelSceneCreator
{
    const string Level1Path = "Assets/Scenes/Level01.unity";

    static LevelSceneCreator()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !System.IO.File.Exists(Level1Path)) Create(1);
        };
    }

    [MenuItem("MiniPool/Ricrea scena Livello 1")]
    static void CreateLevel1() => Create(1);

    static void Create(int index)
    {
        string path = $"Assets/Scenes/Level{index:00}.unity";
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (string.IsNullOrEmpty(EditorSceneManager.GetSceneAt(i).path))
            {
                Debug.LogWarning("MiniPool: salva la scena aperta, poi usa MiniPool > Ricrea scena Livello 1");
                return;
            }
        // additive + close, so the scene the user has open is not touched
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
        var game = new GameObject("Game").AddComponent<GameManager>();
        game.levelIndex = index;
        EditorSceneManager.MoveGameObjectToScene(game.gameObject, scene);
        EditorSceneManager.SaveScene(scene, path);
        EditorSceneManager.CloseScene(scene, true);

        var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        scenes.Insert(index - 1 < scenes.Count ? index - 1 : scenes.Count, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"MiniPool: creata {path}");
    }
}
