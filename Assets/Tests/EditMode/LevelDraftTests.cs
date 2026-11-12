using System.Linq;
using NUnit.Framework;
using UnityEngine;

// the editing shared by the Unity editor window and the in-game editor (LevelDraft): brushes, tools, text round trip
public class LevelDraftTests
{
    const string Table = "#####\n#...#\n#...#\n#...#\n#####\n";

    static LevelDraft Draft(string text = Table)
    {
        var d = new LevelDraft();
        d.SetText(text, Vector2Int.zero);
        return d;
    }

    [Test] public void PaintThenErase_LeavesCellEmpty()
    {
        var d = Draft();
        var k = new Vector2Int(2, 2);
        Assert.IsTrue(d.Apply('P', k, k * LevelBuilder.Sub, null, 90, 0, false, false));
        Assert.AreEqual('P', d.cells[k]);
        Assert.IsFalse(d.Apply('P', k, k * LevelBuilder.Sub, null, 90, 0, false, false), "same brush again: no change");
        Assert.IsTrue(d.Apply('P', k, k * LevelBuilder.Sub, null, 90, 0, false, true));
        Assert.IsFalse(d.cells.ContainsKey(k));
    }

    [Test] public void ShiftRectangle_PaintsEveryCell()
    {
        var d = Draft();
        d.Apply('I', new Vector2Int(3, 3), Vector2Int.zero, new Vector2Int(1, 1), 0, 0, false, false);
        for (int x = 1; x <= 3; x++)
            for (int z = 1; z <= 3; z++)
                Assert.AreEqual('I', d.cells[new Vector2Int(x, z)]);
    }

    [Test] public void PlaceSameBallTwice_MovesIt()
    {
        var d = Draft();
        d.PlaceObject(new Vector2Int(3, 3), '8');
        d.PlaceObject(new Vector2Int(9, 9), '8');
        Assert.AreEqual(1, d.objects.Values.Count(c => c == '8'));
        Assert.AreEqual('8', d.objects[new Vector2Int(9, 9)]);
    }

    [Test] public void TextRoundTrip_KeepsEverything()
    {
        var d = Draft();
        d.Apply('>', new Vector2Int(2, 2), Vector2Int.zero, null, 135, 0, false, false);
        d.Apply('.', new Vector2Int(1, 1), Vector2Int.zero, null, 0, 2, false, false); // felt on floor 2: floor 0 stays as it was
        d.PlaceObject(new Vector2Int(9, 6), '0');
        d.par = 3;
        d.fouls = 2;
        d.theme = 1;
        var text = d.GetText(out var min);
        var back = Draft(text);
        Assert.AreEqual(text, back.GetText(out var min2));
        Assert.AreEqual(min, min2);
        Assert.AreEqual(135, back.angles[new Vector2Int(2, 2)]);
        Assert.AreEqual('.', back.plans[2].cells[new Vector2Int(1, 1)]);
        Assert.AreEqual(d.plans[0].cells.Count, back.plans[0].cells.Count, "painting on floor 2 left floor 0 alone");
        Assert.AreEqual((3, 2, 1), (back.par, back.fouls, back.theme));
        Assert.AreEqual(1, LevelBuilder.ThemeLine(text));
        Assert.AreEqual(-1, LevelBuilder.ThemeLine(Table));
    }

    [Test] public void CopyThenPaste_KeepsSource()
    {
        var d = Draft();
        d.Apply('X', new Vector2Int(1, 1), Vector2Int.zero, null, 0, 0, false, false);
        d.Select(LevelDraft.CopyTool, new Vector2Int(1, 1), new Vector2Int(1, 1), Vector2Int.zero);
        Assert.IsTrue(d.Paste(new Vector2Int(3, 3), Vector2Int.zero));
        Assert.AreEqual('X', d.cells[new Vector2Int(1, 1)], "a copy leaves the source");
        Assert.AreEqual('X', d.cells[new Vector2Int(3, 3)]);
    }

    [Test] public void RotateClip_TurnsCellsAndAngles()
    {
        var d = Draft();
        d.Apply('>', new Vector2Int(1, 1), Vector2Int.zero, null, 0, 0, false, false); // booster toward the far side
        d.Apply('X', new Vector2Int(2, 1), Vector2Int.zero, null, 0, 0, false, false); // spikes to its right
        d.Select(LevelDraft.CopyTool, new Vector2Int(1, 1), new Vector2Int(2, 1), Vector2Int.zero);
        d.RotateClip(1); // clockwise: right becomes near
        Assert.IsTrue(d.Paste(new Vector2Int(3, 3), Vector2Int.zero));
        Assert.AreEqual('>', d.cells[new Vector2Int(3, 3)]);
        Assert.AreEqual(90, d.angles[new Vector2Int(3, 3)], "the booster now points right");
        Assert.AreEqual('X', d.cells[new Vector2Int(3, 2)], "the spikes are now on the near side of the booster");
    }

    [Test] public void MoveThenDrop_LeavesFelt()
    {
        var d = Draft();
        d.Apply('P', new Vector2Int(1, 1), Vector2Int.zero, null, 0, 0, false, false);
        d.Select(LevelDraft.MoveTool, new Vector2Int(1, 1), new Vector2Int(1, 1), new Vector2Int(3, 3));
        Assert.IsTrue(d.Moving);
        d.Paste(new Vector2Int(3, 3), Vector2Int.zero);
        Assert.AreEqual('.', d.cells[new Vector2Int(1, 1)]);
        Assert.AreEqual('P', d.cells[new Vector2Int(3, 3)]);
    }

    [Test] public void PaintOnAFloor_LeavesTheOtherFloorsAlone()
    {
        var d = new LevelDraft();
        var k = new Vector2Int(1, 1);
        d.Paint(k, k, 'M', 0, 0, false); // sand on floor 0

        d.Paint(k, k, '.', 0, 2, false); // felt over it, on floor 2
        d.Paint(k, k, ' ', 0, 1, false); // the eraser on floor 1: nothing there

        Assert.AreEqual('M', d.plans[0].cells[k], "the sand is still under it");
        Assert.AreEqual('.', d.plans[2].cells[k]);
        var back = new LevelDraft();
        back.SetText(d.GetText(out _), Vector2Int.zero);
        Assert.AreEqual('M', back.plans[0].cells[Vector2Int.zero], "saved floor by floor");
        Assert.AreEqual('.', back.plans[2].cells[Vector2Int.zero]);
        Assert.IsFalse(back.solid, "new levels: nothing is made under a raised cell");
    }

    [Test] public void ColumnSwitch_MakesOnlyThePaintedCellAColumn()
    {
        var d = new LevelDraft();
        Vector2Int a = new(4, 0), b = new(6, 0);
        d.Paint(Vector2Int.zero, new Vector2Int(8, 0), '.', 0, 0, false); // a lane on floor 0
        d.Paint(a, a, ' ', 0, 0, false);
        d.Paint(b, b, ' ', 0, 0, false); // two holes in it
        d.column = true;
        d.Paint(a, a, '.', 0, 2, false); // a column over the first hole
        d.column = false;
        d.Paint(b, b, '.', 0, 2, false); // a slab over the second

        var back = new LevelDraft();
        back.SetText(d.GetText(out _), Vector2Int.zero);
        var lv = LevelBuilder.Build(back.GetText(out _), "Columns");

        try
        {
            CollectionAssert.AreEquivalent(new[] { a }, back.plans[2].solid, "saved cell by cell");
            Assert.IsTrue(lv.WallsNear(new Vector3(a.x, Ball.Radius, 0)).Exists(w => w.bounds.Contains((Vector2)a)), "the column is a wall on floor 0");
            Assert.IsFalse(lv.WallsNear(new Vector3(b.x, Ball.Radius, 0)).Exists(w => w.bounds.Contains((Vector2)b)), "nothing under the slab");
        }
        finally { Object.DestroyImmediate(lv.root.gameObject); }
    }

    [Test] public void NewUserLevel_IsValid()
    {
        var d = Draft(LevelDraft.DefaultTable + "8 36 15\n");
        CollectionAssert.IsEmpty(d.Validate(LevelEditor.Goal).ToList());
        var lv = LevelBuilder.Build(d.GetText(out _), "NewUserLevel");
        try { Assert.IsTrue(lv.balls.Any(b => b.number == 8) && lv.balls.Any(b => b.number == 0)); }
        finally { Object.DestroyImmediate(lv.root.gameObject); }
    }

    [Test] public void SetGoal_AdaptsUserLevelToGameLevel()
    {
        var d = Draft(LevelDraft.DefaultTable + "1 18 15\n8 36 15\n"); // helper 1 + user goal 8
        d.SetGoal(Worlds.BallOf(16)); // level 16: goal is 1
        Assert.AreEqual('1', d.objects[new Vector2Int(36, 15)], "the 8 spot becomes the level's goal ball");
        Assert.AreEqual('8', d.objects[new Vector2Int(18, 15)], "the helper ball swaps with the 8");
        CollectionAssert.IsEmpty(d.Validate(Worlds.BallOf(16)).ToList());
    }
}
