using NUnit.Framework;
using UnityEngine;

public class BuildPhaseTests
{
    // 3x1 felt inside rails, cue on the left felt cell; world x = 0..4, z = 0..2
    const string Level = "#####\n#...#\n#####\n---\n0 3 3\n";
    const string Straight = "###\n#.#\n#.#\n###\n"; // 1 wide, 2 long, closed

    [Test] public void Attach_OpensTheSharedRail()
    {
        // turned to lie along x: piece x = 4..7, its middle cell (local (1,1)) on world (5,1); its end rail lies on the table's right rail
        var t = LevelBuilder.AttachPiece(Level, Straight, new Vector2Int(5, 1), 90);
        Assert.IsNotNull(t);
        var cells = LevelBuilder.ToCells(t, Vector2Int.zero);
        Assert.AreEqual('.', cells[new Vector2Int(4, 1)], "door between the table and the piece");
        Assert.AreEqual('#', cells[new Vector2Int(4, 0)], "corner rails stay");
        Assert.AreEqual('.', cells[new Vector2Int(5, 1)]);
        Assert.AreEqual('#', cells[new Vector2Int(7, 1)], "the piece's far end stays closed");
    }

    [Test] public void Attach_Rejected()
    {
        Assert.IsNull(LevelBuilder.AttachPiece(Level, Straight, new Vector2Int(2, 1), 90), "covers the table's felt");
        Assert.IsNull(LevelBuilder.AttachPiece(Level, Straight, new Vector2Int(10, 1), 90), "not connected");
        Assert.IsNull(LevelBuilder.AttachPiece(Level, Straight, new Vector2Int(5, 3), 0), "only the corners' rails touch: no felt meets felt");
    }

    [Test] public void Attach_OnTheLeftShiftsTheGridAndKeepsTheBall()
    {
        var t = LevelBuilder.AttachPiece(Level, Straight, new Vector2Int(-2, 1), 90); // piece x = -3..0
        Assert.IsNotNull(t);
        Assert.AreEqual(8, LevelBuilder.Parse(t).GetLength(0));
        Assert.IsTrue(LevelBuilder.ParseObjects(t).ContainsKey(new Vector2Int(3 + 9, 3)), "ball shifted with the grid");
    }

    [Test] public void Attach_TurnsOrientedTiles()
    {
        var t = LevelBuilder.AttachPiece(Level, "###\n#^#\n#.#\n###\n", new Vector2Int(5, 1), 90);
        Assert.AreEqual(90, LevelBuilder.ParseAngles(t)[new Vector2Int(6, 1)], "booster pointing far turned to the right");
    }

    [Test] public void Overlay_GoesOnTopOfTheTable()
    {
        var piece = "XX\n---\n" + LevelBuilder.OverlayTag + "\n";
        Assert.IsTrue(LevelBuilder.IsOverlay(piece));
        var t = LevelBuilder.AttachPiece(Level, piece, new Vector2Int(2, 1), 0); // cells (2,1) and (3,1): felt
        Assert.IsNotNull(t);
        var cells = LevelBuilder.ToCells(t, Vector2Int.zero);
        Assert.AreEqual('X', cells[new Vector2Int(2, 1)]);
        Assert.AreEqual('X', cells[new Vector2Int(3, 1)]);
        Assert.IsNull(LevelBuilder.AttachPiece(Level, piece, new Vector2Int(1, 1), 0), "where the cue starts");
        Assert.IsNull(LevelBuilder.AttachPiece(Level, piece, new Vector2Int(4, 1), 0), "on a rail / off the table");
        Assert.IsFalse(LevelBuilder.IsOverlay(Straight));
    }

    [Test] public void EveryPiece_AttachesSomewhere()
    {
        const string big = "#########\n#.......#\n#.......#\n#.......#\n#.......#\n#.......#\n#########\n---\n0 3 3\n"; // 7x5 felt
        var pieces = Resources.LoadAll<TextAsset>("Pieces");
        Assert.IsNotEmpty(pieces);
        foreach (var p in pieces)
        {
            bool ok = false;
            for (int x = -8; x <= 16 && !ok; x++)
                for (int z = -8; z <= 14 && !ok; z++)
                    for (int rot = 0; rot < 360 && !ok; rot += 90)
                        ok = LevelBuilder.AttachPiece(big, p.text, new Vector2Int(x, z), rot) != null;
            Assert.IsTrue(ok, p.name);
        }
    }

    [Test] public void Pocket_OnlyOneAndMoves()
    {
        const string pockets = "O#O\n#.#\nO#O\n---\n0 3 3\n";
        var one = LevelBuilder.OnePocket(pockets);
        Assert.AreEqual(1, one.Split('O').Length - 1);
        var moved = LevelBuilder.MovePocket(one, new Vector2Int(1, 2));
        var cells = LevelBuilder.ToCells(moved, Vector2Int.zero);
        Assert.AreEqual('O', cells[new Vector2Int(1, 2)]);
        Assert.AreEqual(1, moved.Split('O').Length - 1, "the old one is a rail again");
    }

    [Test] public void Pocket_RemovedWhileBuildingThenBack()
    {
        const string withPocket = "##O##\n#...#\n#####\n---\n0 3 3\n";
        var none = LevelBuilder.RemovePocket(withPocket);
        Assert.IsFalse(none.Contains("O"));
        Assert.IsNotNull(LevelBuilder.AttachPiece(none, Straight, new Vector2Int(2, 3), 0), "a piece can go where the pocket was");
        Assert.AreEqual(1, LevelBuilder.OnePocket(none).Split('O').Length - 1, "nobody placed it: it comes back");
    }

    [Test] public void FeltWithRails_SurroundsAndMerges()
    {
        var cells = new System.Collections.Generic.Dictionary<Vector2Int, char>();
        LevelBuilder.PaintFeltWithRails(cells, Vector2Int.zero);
        Assert.AreEqual(9, cells.Count);
        Assert.AreEqual('#', cells[new Vector2Int(1, 1)]);
        LevelBuilder.PaintFeltWithRails(cells, new Vector2Int(1, 0)); // next to it: the rail between becomes felt
        Assert.AreEqual('.', cells[new Vector2Int(1, 0)]);
        Assert.AreEqual('#', cells[new Vector2Int(2, 0)]);
        Assert.AreEqual(12, cells.Count); // 4x3: two felt cells inside a ring of rails
    }

    [Test] public void Pocket_Rejected()
    {
        Assert.IsNull(LevelBuilder.MovePocket(Level, new Vector2Int(1, 1)), "where the cue starts");
        Assert.IsNull(LevelBuilder.MovePocket(Level, new Vector2Int(9, 9)), "off the table");
        Assert.IsNotNull(LevelBuilder.MovePocket(Level, new Vector2Int(2, 2)), "rail next to felt");
    }
}
