using System.Linq;
using NUnit.Framework;
using UnityEngine;

// every shipped level builds and is playable: a cue ball, the black and a pocket
public class LevelsTests
{
    [Test] public void EveryLevelHasCueBlackAndPocket()
    {
        var files = Resources.LoadAll<TextAsset>("Levels").Where(t => t.name.StartsWith("level")).ToList();
        Assert.IsNotEmpty(files);
        foreach (var t in files)
        {
            var lv = LevelBuilder.Build(t.text, t.name);
            try
            {
                Assert.IsTrue(lv.balls.Any(b => b.number == 0), $"{t.name}: manca la bianca");
                Assert.IsTrue(lv.balls.Any(b => b.number == 8), $"{t.name}: manca la nera");
                Assert.IsNotEmpty(lv.pockets, $"{t.name}: manca la buca");
            }
            finally { Object.DestroyImmediate(lv.root.gameObject); }
        }
    }
}
