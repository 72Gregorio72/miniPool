using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Run-and-observe for the in-game editor's brush icons (LevelEditor.MakeIcons): only with Temp/shot. Renders them the way the
// game does and saves them side by side to production/qa/evidence/editor-icons.png. Delete Temp/shot afterwards.
public class EditorIconTests
{
    [Test]
    public void Icons_RenderToContactSheet()
    {
        if (!File.Exists("Temp/shot")) Assert.Ignore("solo con Temp/shot");
        Directory.CreateDirectory("production/qa/evidence");
        LevelEditor.MakeIcons();
        var icons = LevelEditor.icons.OrderBy(kv => kv.Key).ToList();
        const int size = 160, cols = 8;
        int rows = (icons.Count + cols - 1) / cols;
        var sheet = new Texture2D(cols * size, rows * size, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < icons.Count; i++)
            {
                RenderTexture.active = (RenderTexture)icons[i].Value;
                sheet.ReadPixels(new Rect(0, 0, size, size), i % cols * size, (rows - 1 - i / cols) * size);
            }
            RenderTexture.active = null;
            File.WriteAllBytes("production/qa/evidence/editor-icons.png", sheet.EncodeToPNG());
            Assert.AreEqual(LevelDraft.Brushes.Length - 1 + LevelDraft.BallChars.Length, icons.Count, "every brush but the eraser, plus the numbered balls");
        }
        finally
        {
            Object.DestroyImmediate(sheet);
            foreach (var kv in icons) { ((RenderTexture)kv.Value).Release(); Object.DestroyImmediate(kv.Value); }
            LevelEditor.icons.Clear();
        }
    }
}
