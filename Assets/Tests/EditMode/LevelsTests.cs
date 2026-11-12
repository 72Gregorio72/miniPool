using System.Linq;
using NUnit.Framework;
using UnityEngine;

// every shipped level builds and is playable: a cue ball, the level's ball (Worlds.BallOf) and a pocket
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
                int goal = Worlds.BallOf(int.Parse(t.name.Substring(5)));
                Assert.IsTrue(lv.balls.Any(b => b.number == goal), $"{t.name}: manca la palla {goal}");
                Assert.IsTrue(lv.sheets.Any(s => s.pockets.Count > 0), $"{t.name}: manca la buca");
            }
            finally { Object.DestroyImmediate(lv.root.gameObject); }
        }
    }
}
