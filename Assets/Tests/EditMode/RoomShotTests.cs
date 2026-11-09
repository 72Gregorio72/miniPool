using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Run-and-observe for the room (LevelBuilder.Room): only with Temp/shot holding level numbers ("1 16"). Builds each level
// with its room and saves a few views to production/qa/evidence/room-levelNN-<view>.png. Delete Temp/shot afterwards.
public class RoomShotTests
{
    [Test]
    public void Shots()
    {
        if (!File.Exists("Temp/shot")) Assert.Ignore("solo con Temp/shot");
        Directory.CreateDirectory("production/qa/evidence");
        UnityEditor.ShaderUtil.allowAsyncCompilation = false; // otherwise shaders still compiling render as placeholders
        foreach (var n in File.ReadAllText("Temp/shot").Split(' ', '\n', '\r').Where(s => s.Length > 0).Select(int.Parse))
        {
            var theme = Worlds.ThemeOf(Worlds.WorldOf(n));
            LevelBuilder.SetTheme(theme);
            var lv = LevelBuilder.Build(Resources.Load<TextAsset>($"Levels/level{n:00}").text, "shot");
            LevelBuilder.Room(lv);
            lv.root.position = new Vector3(5000, 0, 5000); // away from the Level Editor's preview, which lives at the origin
            var cam = new GameObject("ShotCam").AddComponent<Camera>();
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            try
            {
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = theme.sky;
                cam.farClipPlane = 300;
                var c = lv.bounds.center + lv.root.position;
                // play view (behind the table, like OrbitCamera's default), a high corner view and a zoomed-out one
                var views = new (string name, Vector3 pos)[]
                {
                    ("play", c + new Vector3(-lv.bounds.size.x / 2 - 6, 2.5f, 0)),
                    ("corner", c + new Vector3(-lv.bounds.size.x / 2 - 8, 9, -lv.bounds.size.z / 2 - 8)),
                    ("out", c + new Vector3(-lv.bounds.size.x - 25, 22, -lv.bounds.size.z - 15)),
                };
                foreach (var (name, pos) in views)
                {
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c - pos));
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = null;
                    File.WriteAllBytes($"production/qa/evidence/room-level{n:00}-{name}.png", tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                Object.DestroyImmediate(cam.gameObject);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(lv.root.gameObject);
            }
        }
        LevelBuilder.SetTheme(Worlds.Themes[0]);
        UnityEditor.ShaderUtil.allowAsyncCompilation = true;
    }
}
