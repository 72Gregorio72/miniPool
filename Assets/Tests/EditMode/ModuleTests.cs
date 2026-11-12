using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Floors, ramps and the newer modules (PoolPhysics + LevelBuilder).
public class ModuleTests
{
    LevelBuilder.Level lv;
    const float R = Ball.Radius, Step = LevelBuilder.FloorStep;

    LevelBuilder.Level Build(string text) => lv = LevelBuilder.Build(text, "test");

    // one lane between rails, the given middle row; extra = lines after "---"
    LevelBuilder.Level Lane(string row, string extra = "") =>
        Build(new string('#', row.Length) + "\n" + row + "\n" + new string('#', row.Length) + "\n---\n" + extra);

    static string Floors(int from, int to, int z, int floor)
    {
        var s = "";
        for (int x = from; x <= to; x++) s += $"= {x} {z} {floor}\n";
        return s;
    }

    [TearDown] public void Clean() { if (lv?.root) Object.DestroyImmediate(lv.root.gameObject); lv = null; } // pure-logic tests build no level

    [Test] public void DiagRail_TurnsTheBallNinetyDegrees()
    {
        Build("######\n#....#\n#0../#\n######\n---\n> 4 1 90\n"); // rail half toward the near-right corner
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0);
        bool turned = false;
        for (int i = 0; i < 200; i++) // stun slows to 5/7: the rail is reached at ~1.05 s
        {
            PoolPhysics.Simulate(lv, 0.01f);
            var p = b.transform.position;
            Assert.Less(p.x - p.z, 3 - R * Mathf.Sqrt(2) + 0.02f, "never inside the rail half");
            turned |= b.v.z > 0.5f && Mathf.Abs(b.v.x) < b.v.z * 0.3f;
        }
        Assert.IsTrue(turned);
    }

    [Test] public void Ramp_FastBallClimbsToTheUpperFloor()
    {
        Lane("#0.S...............#", "> 3 1 90\n" + Floors(4, 19, 1, 1) + Floors(4, 19, 0, 1) + Floors(4, 19, 2, 1));
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 10, 0, 0.4f);
        PoolPhysics.Simulate(lv, 3f);
        Assert.Greater(b.transform.position.x, 4.5f);
        Assert.AreEqual(Step + R, b.transform.position.y, 1e-3f);
        Assert.IsFalse(b.falling || b.airborne);
    }

    [Test] public void Ramp_SlowBallRollsBack()
    {
        Lane("#0.S...............#", "> 3 1 90\n" + Floors(4, 19, 1, 1));
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 4f);
        Assert.Less(b.transform.position.x, 3f);
        Assert.AreEqual(R, b.transform.position.y, 1e-3f);
    }

    [Test] public void StepDown_BallFallsToTheLowerFloor()
    {
        Lane("#0........#", Floors(0, 3, 0, 1) + Floors(1, 3, 1, 1) + Floors(0, 3, 2, 1));
        var b = lv.balls[0];
        Assert.AreEqual(Step + R, b.transform.position.y, 1e-3f, "spawned on its floor");
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 3f);
        Assert.IsFalse(b.falling);
        Assert.AreEqual(R, b.transform.position.y, 1e-3f);
        Assert.Greater(b.transform.position.x, 4f);
    }

    [Test] public void StepUp_BouncesABallFromBelow()
    {
        Lane("#.....0...#", Floors(1, 3, 1, 1));
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.left, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.Greater(b.v.x, 0, "bounced back");
        Assert.GreaterOrEqual(b.transform.position.x, 3.5f + R - 1e-3f);
    }

    [Test] public void Portal_SameFacing_KeepsDirectionAndSide()
    {
        Build("#######\n#0.T..#\n#.....#\n#.....#\n#..Y..#\n#.....#\n#######\n---\n> 3 5 0\n> 3 2 0\n");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 5, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.4f);
        Assert.AreEqual(2, b.transform.position.z, 0.05f, "came out of the orange one");
        Assert.Greater(b.v.x, 0);
    }

    [Test] public void Portal_TurnedExit_TurnsVelocityAndSpin()
    {
        lv = Build("#######\n#0.T..#\n#.....#\n#.....#\n#.....#\n#..Y..#\n#.....#\n#######\n---\n> 3 6 0\n> 3 2 90\n");
        var b = lv.balls[0];
        b.v = new Vector3(4, 0, 1);
        b.w = new Vector3(0, 3, 4);
        var to = lv.portalOrange.Value;
        var p = PoolPhysics.Teleport(lv, b, new Vector3(2.6f, R, 6.2f), lv.portalBlue.Value, to);
        var turn = Quaternion.Euler(0, 90, 0);
        Assert.Less((b.v - turn * new Vector3(4, 0, 1)).magnitude, 1e-4f);
        Assert.Less((b.w - turn * new Vector3(0, 3, 4)).magnitude, 1e-4f);
        var expected = new Vector3(to.x, 0, to.y) + turn * new Vector3(-0.4f, 0, 0.2f);
        Assert.Less((new Vector3(p.x, 0, p.z) - expected).magnitude, 1e-4f, "entry offset turned with the portal");
    }

    [Test] public void Ice_KeepsBackspinAndSpeed()
    {
        Lane("#IIIIIIIIIIIIIIIIIII#", "0 3 3\n");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, -0.5f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.v.x, 5.5f);
        Assert.Greater(b.w.z, 0, "draw still on: no grip to turn it into roll"); // rolling along +x has w.z < 0, draw w.z > 0
    }

    [Test] public void Sand_StopsTheBallQuickly()
    {
        Lane("#0.MMMMMMMMMMMMM...#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 3f);
        Assert.IsFalse(b.IsMoving);
        Assert.Less(b.transform.position.x, 6f);
    }

    [Test] public void Manhole_CatchesTheBall()
    {
        Lane("#0..W.....#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsFalse(b.IsMoving);
        Assert.AreEqual(4, b.transform.position.x, 1e-3f);
    }

    [Test] public void Gate_BlocksUntilAButtonIsHeld()
    {
        Lane("#0..G.....#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.Less(b.transform.position.x, 3.5f, "closed");
        Clean();

        Build("###########\n#0..G.....#\n#.......K.#\n###########\n---\n1 24 3\n"); // ball 1 sits on the button
        b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.transform.position.x, 4.5f, "open");
    }

    [Test] public void TimedWall_StartsUp()
    {
        Lane("#0..L.....#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 8, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.8f);
        Assert.Less(b.transform.position.x, 3.5f);
        Assert.IsTrue(lv.time < PoolPhysics.TimedPeriod);
    }

    [Test] public void TimedWall_RisingUnderABall_LiftsItOnTop()
    {
        Lane("#...L...#", "0 12 3\n"); // ball on the wall's cell; the wall starts up, goes down at 2 s, up again at 4 s
        var b = lv.balls[0];
        PoolPhysics.Simulate(lv, 2.5f);
        Assert.AreEqual(R, b.transform.position.y, 1e-3f, "on the felt while the wall is down");
        PoolPhysics.Simulate(lv, 1.52f); // rises at 4 s
        Assert.IsTrue(b.airborne && b.v.y > 0, "thrown up by the wall, not teleported on top");
        Assert.Less(b.transform.position.y, LevelBuilder.BlockHeight + R);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.AreEqual(LevelBuilder.BlockHeight + R, b.transform.position.y, 1e-3f, "standing on top");
        Assert.AreEqual(4f, b.transform.position.x, 0.05f);
        Assert.IsFalse(b.airborne || b.falling);
    }

    // ---- piles (stacked modules, "sopra" lines)

    [Test] public void Pile_SpikesOnATimedWall_RideItAndKillOnTheFeltWhenItIsDown()
    {
        Lane("#...L...#", "sopra 4 1 X\n0 12 3\n"); // ball on the felt of the wall's cell; the wall starts up, goes down at 2 s
        var b = lv.balls[0];
        var spikes = lv.stacks[new Vector2Int(4, 1)][1];
        Assert.AreEqual(LevelBuilder.BlockHeight, spikes.y, 1e-3f, "on the raised wall");
        Assert.Contains('X', lv.ModulesAt(new Vector3(4, LevelBuilder.BlockHeight + R, 1)).ConvertAll(m => m.c), "a ball on top feels them");
        PoolPhysics.Simulate(lv, 1.9f);
        Assert.IsFalse(b.falling, "the spikes are up there");
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.AreEqual(0, spikes.y, 1e-3f, "down with the wall");
        Assert.AreEqual(0.03f, spikes.go.transform.position.y, 0.02f, "their look too");
        Assert.IsTrue(b.falling, "still spikes: the ball dies");
    }

    [Test] public void Pile_IceAndBoosterOnOneCell_BothWork()
    {
        Lane("#0.>....#", "> 3 1 90\nsopra 3 1 I\n");
        var felt = lv.ModulesAt(new Vector3(3, R, 1)).ConvertAll(m => m.c);
        Assert.Contains('>', felt);
        Assert.Contains('I', felt);
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.Greater(b.v.x, 5f, "pushed");
    }

    [Test] public void Pile_BoosterOnABlock_TopIsAFloorAndTheSidesStillBounce()
    {
        Lane("#0..P...#", "sopra 4 1 > 90\n");
        Assert.AreEqual(LevelBuilder.BlockHeight, lv.GroundAt(new Vector3(4, LevelBuilder.BlockHeight + R, 1)).Value, 1e-3f, "stands on the block");
        Assert.Contains('>', lv.ModulesAt(new Vector3(4, LevelBuilder.BlockHeight + R, 1)).ConvertAll(m => m.c));
        Assert.AreEqual(0, lv.ModulesAt(new Vector3(4, R, 1)).FindAll(m => m.c == '>').Count, "not for a ball at the block's foot");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.Less(b.transform.position.x, 3.5f, "bounced off the block");
    }

    [Test] public void Pile_BlockOnATimedWall_ItsSidesGoUpAndDownWithIt()
    {
        Lane("#...L...#", "sopra 4 1 P\n");
        var block = lv.stacks[new Vector2Int(4, 1)][1];
        Assert.AreEqual(2 * LevelBuilder.BlockHeight, block.wall.height, 1e-3f);
        PoolPhysics.Simulate(lv, 2.1f); // wall down
        Assert.AreEqual(LevelBuilder.BlockHeight, block.wall.height, 1e-3f);
        Assert.Less(block.wall.bottom, 0);
    }

    [Test] public void Pile_TextRoundTrip_KeepsThePile()
    {
        const string text = "#####\n#.L.#\n#####\n---\nsopra 2 1 X\nsopra 2 1 > 90\n";
        var angles = new Dictionary<Vector2Int, int>();
        var above = new Dictionary<Vector2Int, List<(char c, int angle)>>();
        var cells = LevelBuilder.ToCells(text, Vector2Int.zero, null, angles, null, null, above);
        Assert.AreEqual(text, LevelBuilder.ToText(cells, out _, null, angles, null, null, above));
    }

    [Test] public void SetTile_Stacking_PilesUpAndTheEraserTakesTheTopOff()
    {
        var cells = LevelBuilder.ToCells("###\n#L#\n###\n", Vector2Int.zero);
        var angles = new Dictionary<Vector2Int, int>();
        var under = new Dictionary<Vector2Int, char>();
        var above = new Dictionary<Vector2Int, List<(char c, int angle)>>();
        var k = new Vector2Int(1, 1);
        Assert.IsTrue(LevelBuilder.SetTile(cells, angles, k, 'X', 0, under, above, true));
        Assert.IsFalse(LevelBuilder.SetTile(cells, angles, k, 'X', 0, under, above, true), "the same one again: a drag");
        Assert.AreEqual('L', cells[k]);
        Assert.AreEqual('X', above[k][0].c);
        Assert.IsTrue(LevelBuilder.EraseTop(cells, angles, under, above, k));
        Assert.IsFalse(above.ContainsKey(k));
        Assert.IsTrue(LevelBuilder.EraseTop(cells, angles, under, above, k));
        Assert.AreEqual('.', cells[k], "the last module leaves the felt");
        LevelBuilder.SetTile(cells, angles, k, 'L', 0, under, above, true); // nothing to stack on: plain paint
        LevelBuilder.SetTile(cells, angles, k, 'X', 0, under, above, true);
        Assert.IsTrue(LevelBuilder.SetTile(cells, angles, k, 'M', 0, under, above, false));
        Assert.AreEqual('M', cells[k]);
        Assert.IsFalse(above.ContainsKey(k), "stacking off: replaced, pile and all");
    }

    [Test] public void Bridge_BallOnTheDeckCrossesIt()
    {
        Lane("#0.._....#", Floors(0, 9, 0, 2) + Floors(0, 9, 1, 2) + Floors(0, 9, 2, 2));
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        float maxX = 0;
        for (int i = 0; i < 100 && maxX < 5.5f; i++)
        {
            PoolPhysics.Simulate(lv, 0.01f);
            var p = b.transform.position;
            maxX = Mathf.Max(maxX, p.x);
            if (p.x > 3.6f && p.x < 4.4f) Assert.AreEqual(2 * Step + R, p.y, 1e-3f, "height on the bridge cell");
        }
        Assert.Greater(maxX, 5.5f, "went across");
        Assert.IsFalse(b.falling || b.airborne);
    }

    [Test] public void Bridge_BallBelowPassesUnder()
    {
        Lane("#0.._....#", "= 4 1 2\n"); // deck 2 floors up, felt on floor 0 under it
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        float maxX = 0;
        for (int i = 0; i < 100 && maxX < 5.5f; i++)
        {
            PoolPhysics.Simulate(lv, 0.01f);
            var p = b.transform.position;
            maxX = Mathf.Max(maxX, p.x);
            if (p.x > 3.6f && p.x < 4.4f) Assert.AreEqual(R, p.y, 1e-3f, "height on the bridge cell");
        }
        Assert.Greater(maxX, 5.5f, "went across");
    }

    // ---------- floors: each one is a table of its own ----------

    // floor 0: a lane with sand on (4, 1); floor 2: felt over cells 3..5 of the same lane
    const string SandUnderFelt = "#########\n#0..M...#\n#########\npiano 2 3 1\n...\n";

    [Test] public void Floor_AboveAnother_LeavesWhatIsUnderIt()
    {
        Build(SandUnderFelt);
        var under = new Vector3(4, R, 1);
        var over = new Vector3(4, 2 * Step + R, 1);

        Assert.AreEqual(2, lv.sheets.Count);
        Assert.AreEqual('M', lv.Sheet(under).TileAt(under), "the sand is still there on floor 0");
        Assert.AreEqual(0, lv.Sheet(under).GroundAt(under).Value, 1e-3f);
        Assert.AreEqual('.', lv.Sheet(over).TileAt(over), "plain felt on floor 2");
        Assert.AreEqual(2 * Step, lv.Sheet(over).GroundAt(over).Value, 1e-3f);
    }

    [Test] public void Floor_TwoUp_BallRollsUnderIt()
    {
        Build(SandUnderFelt);
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 12, 0, 0.4f);
        float maxX = 0;
        for (int i = 0; i < 300; i++)
        {
            PoolPhysics.Simulate(lv, 0.01f);
            maxX = Mathf.Max(maxX, b.transform.position.x);
            Assert.AreEqual(R, b.transform.position.y, 1e-3f, "stays on floor 0");
        }
        Assert.Greater(maxX, 5.5f, "went under the upper floor");
    }

    [Test] public void Floor_WithNothingUnder_MakesNoTableBelow()
    {
        Build("#0.#\npiano 2 5 0\n..\n"); // floor 2 beside the lane, over nothing
        var under = new Vector3(5, R, 0);
        Assert.IsEmpty(lv.sheets[1].columns);
        Assert.IsNull(lv.Sheet(under).GroundAt(under), "nothing was made under the raised cell");
    }

    [Test] public void Floor_OneUp_IsAStepForTheFloorBelow()
    {
        Build("#########\n#0......#\n#########\npiano 1 4 1\n.\n"); // one cell on floor 1 over the lane
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        float maxX = 0;
        for (int i = 0; i < 200; i++)
        {
            PoolPhysics.Simulate(lv, 0.01f);
            maxX = Mathf.Max(maxX, b.transform.position.x);
        }
        Assert.Less(maxX, 3.5f - R + 0.02f, "no room under a floor one step up: it bounces off");
    }

    [Test] public void OldFile_RaisedCells_BecomeASolidLevel()
    {
        Build("#0.._....#\n---\n= 4 0 2\n= 7 0 2\n"); // old format: a bridge and a raised cell
        Assert.AreEqual(2, lv.sheets.Count);
        Assert.AreEqual('.', lv.TileAt(new Vector3(4, R, 0)), "felt under the old bridge");
        Assert.AreEqual(' ', lv.TileAt(new Vector3(7, R, 0)), "the raised cell left floor 0...");
        Assert.IsTrue(lv.WallsNear(new Vector3(7, R, 0)).Exists(w => w.bounds.Contains(new Vector2(7, 0))), "...but is a column a ball bounces off");
    }

    [Test] public void WindFan_PushesLessFartherAway()
    {
        Lane("#U......#", "> 1 1 90\n"); // blows toward +x
        float At(int x) => lv.WindAt(new Vector3(x, R, 1)).x;
        Assert.AreEqual(PoolPhysics.WindPush, At(2), 1e-3f);
        Assert.Greater(At(2), At(3));
        Assert.Greater(At(3), At(4));
        Assert.Greater(At(4), 0);
        Assert.AreEqual(0, At(5));
    }

    [Test] public void WindFan_PushesABallPassingInFront()
    {
        Build("#####\n#...#\n#...#\n#0..#\n#.U.#\n#####\n---\n> 2 1 0\n"); // blows toward +z, across the ball's path
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.4f);
        Assert.Greater(b.v.z, 1f);
    }

    [Test] public void SetTile_ModuleOnARail_KeepsTheRailInTheText()
    {
        var angles = new Dictionary<Vector2Int, int>();
        var under = new Dictionary<Vector2Int, char>();
        var cells = LevelBuilder.ToCells("###\n#.#\n###\n", Vector2Int.zero);
        LevelBuilder.SetTile(cells, angles, new Vector2Int(1, 2), LevelBuilder.WindFan, 180, under);
        var text = LevelBuilder.ToText(cells, out _, null, angles, null, under);

        var under2 = new Dictionary<Vector2Int, char>();
        var cells2 = LevelBuilder.ToCells(text, Vector2Int.zero, null, null, null, under2);
        Assert.AreEqual(LevelBuilder.WindFan, cells2[new Vector2Int(1, 2)]);
        Assert.AreEqual('#', under2[new Vector2Int(1, 2)]);

        LevelBuilder.SetTile(cells, angles, new Vector2Int(1, 2), '#', 0, under);
        Assert.IsFalse(under.ContainsKey(new Vector2Int(1, 2)), "painting the rail back drops the record");
    }

    [Test] public void ModuleOnARail_StandsOnTopOfTheRail()
    {
        Lane("#0......U#", "sotto 8 1 #\n> 8 1 270\n"); // fan standing on a rail, blowing back
        Assert.AreEqual('#', lv.ground[8, 1], "the rail is still the table there");
        Assert.IsTrue(lv.WallsNear(new Vector3(8, 0, 1)).Exists(w => Mathf.Approximately(w.height, LevelBuilder.RailHeight)), "rail wall built");
        Assert.AreEqual(LevelBuilder.RailHeight, lv.modules[new Vector2Int(8, 1)].transform.position.y, 1e-3f, "fan on top of the rail");
    }

    [Test] public void SpikesOnABridge_KillOnlyTheBallOnTheDeck()
    {
        Lane("#0..X....#", "= 4 1 2\nsotto 4 1 _\n"); // spikes on the deck, underpass on floor 0
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsFalse(b.falling, "passed under the spikes");

        Object.DestroyImmediate(lv.root.gameObject);
        Lane("#0..X....#", "sotto 4 1 _\n" + Floors(0, 9, 0, 2) + Floors(0, 9, 1, 2) + Floors(0, 9, 2, 2)); // everything on floor 2: on the deck
        b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsTrue(b.falling, "died on the spikes on the deck");
    }

    [Test] public void WindFan_OnABridge_DoesntBlowUnderIt()
    {
        // fan on a deck on floor 2 blowing along x; the ball sits on floor 0 under the next bridge cells
        Lane("#.U__....#", "sotto 2 1 _\n= 2 1 2\n= 3 1 2\n= 4 1 2\n> 2 1 90\n0 9 3\n");
        var b = lv.balls[0];
        Assert.AreEqual(R, b.transform.position.y, 1e-3f, "starts under the bridge");
        PoolPhysics.Simulate(lv, 1f);
        Assert.AreEqual(3f, b.transform.position.x, 1e-3f, "no wind down here");
    }

    [Test] public void WindFan_PushesABallAtRest()
    {
        Lane("#U.0.....#", "> 1 1 90\n"); // ball still, 2 tiles ahead of the fan
        var b = lv.balls[0];
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.transform.position.x, 3.5f);
    }

    [Test] public void WindFan_BallPinnedAgainstARail_StopsMoving()
    {
        Lane("#U.0#", "> 1 1 90\n"); // the air pushes the ball into the rail
        var b = lv.balls[0];
        for (int i = 0; i < 150; i++) PoolPhysics.Simulate(lv, 0.02f);
        Assert.IsFalse(b.IsMoving, "the shot can end");
        Assert.AreEqual(3.25f, b.transform.position.x, 0.02f);
    }

    [Test] public void TimedWallInverse_IsDownWhileThePurpleOneIsUp()
    {
        Lane("#0..%..L...#");
        var green = lv.movers.Find(m => m.inverted);
        var purple = lv.movers.Find(m => !m.inverted);
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 8, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.8f);
        Assert.IsTrue(purple.up && !green.up);
        Assert.Greater(b.transform.position.x, 4.5f, "rolled through the green one");
        Assert.Less(b.transform.position.x, 6.5f, "stopped by the purple one");
        PoolPhysics.Simulate(lv, PoolPhysics.TimedPeriod);
        Assert.IsTrue(!purple.up && green.up, "swapped");
    }

    [Test] public void Bumper_KicksTheBallBackFaster()
    {
        Lane("#0...Q.....#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        float fastest = 0;
        for (int i = 0; i < 40; i++)
        {
            PoolPhysics.Simulate(lv, 0.05f);
            fastest = Mathf.Max(fastest, -b.v.x);
        }
        Assert.Greater(fastest, 5f, "more than it came in with");
    }

    [Test] public void Belt_CarriesAStillBall()
    {
        Lane("#.N......#", "> 2 1 90\n0 6 3\n");
        var b = lv.balls[0];
        PoolPhysics.Simulate(lv, 0.1f);
        Assert.IsTrue(b.IsMoving, "being carried counts as moving (the shot isn't over)");
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.transform.position.x, 2.5f);
        PoolPhysics.Simulate(lv, 3f);
        Assert.IsFalse(b.IsMoving);
    }

    [Test] public void Platform_CarriesTheBallOverTheGap()
    {
        Lane("#.H    .#", "> 2 1 90\n0 6 3\n");
        var b = lv.balls[0];
        Assert.AreEqual(4, lv.platforms[0].range);
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsFalse(b.falling);
        Assert.AreEqual(2 + PoolPhysics.PlatformSpeed, b.transform.position.x, 0.05f);
    }

    [Test] public void TimedSpikes_KillOnlyWhileUp()
    {
        Lane("#.!&.#", "0 6 3\n* 9 3\n"); // white on the purple ones (up first), grey on the green ones (down first)
        PoolPhysics.Simulate(lv, 0.1f);
        Assert.IsTrue(lv.balls[0].falling, "purple spikes start up");
        Assert.IsFalse(lv.balls[1].falling, "green spikes start down");
        PoolPhysics.Simulate(lv, PoolPhysics.TimedPeriod);
        Assert.IsTrue(lv.balls[1].falling, "the green ones come up when the purple go down");
    }

    [Test] public void Platform_VerticalTrack_CarriesTheBallUpAFloor()
    {
        // platform -> track piece going up -> track piece on floor 1 right above it
        Build("#####\n#H}.#\n#####\n---\n> 1 1 90\n0 3 3\npiano 1 2 1\n=\n---\n> 0 0 90\n");
        var pl = lv.platforms[0];
        Assert.AreEqual(2, pl.range);
        Assert.AreEqual(Step, pl.path[^1].y, 1e-3f, "the last stop is one floor up");
        Assert.Contains(pl, lv.sheets[1].platforms, "floor 1 knows it too");
        PoolPhysics.Simulate(lv, 2f / PoolPhysics.PlatformSpeed);
        var b = lv.balls[0];
        Assert.IsFalse(b.falling);
        Assert.AreEqual(2, b.transform.position.x, 0.05f);
        Assert.AreEqual(Step + R, b.transform.position.y, 0.05f, "carried up a floor");
    }

    [Test] public void Platform_CarriesItsSpikes()
    {
        Lane("#.H    .#", "> 2 1 90\nsopra 2 1 X\n0 6 3\n");
        Assert.Contains('X', lv.platforms[0].riders);
        PoolPhysics.Simulate(lv, 0.1f);
        Assert.IsTrue(lv.balls[0].falling, "a ball on a spiked platform dies");
    }

    [Test] public void Platform_TakesOnlySurfacesOnItsPile()
    {
        var cells = new Dictionary<Vector2Int, char> { [Vector2Int.zero] = LevelBuilder.PlatformTile };
        var angles = new Dictionary<Vector2Int, int>();
        var above = new Dictionary<Vector2Int, List<(char c, int angle)>>();
        Assert.IsTrue(LevelBuilder.SetTile(cells, angles, Vector2Int.zero, 'M', 0, null, above, true));
        Assert.AreEqual('M', above[Vector2Int.zero][0].c);
        Assert.IsTrue(LevelBuilder.SetTile(cells, angles, Vector2Int.zero, 'P', 0, null, above, true)); // a block replaces it
        Assert.AreEqual('P', cells[Vector2Int.zero]);
        Assert.IsFalse(above.ContainsKey(Vector2Int.zero));
    }

    [Test] public void Platform_FollowsItsTrackAroundACorner()
    {
        // platform going right onto a track piece that turns it toward the far side, two more pieces up
        Build("#####\n# = #\n# = #\n#H= #\n#####\n---\n> 1 1 90\n> 2 1 0\n> 2 2 0\n> 2 3 0\n");
        var pl = lv.platforms[0];
        Assert.AreEqual(3, pl.range);
        Assert.IsFalse(pl.loop);
        Assert.IsNull(lv.GroundAt(new Vector3(2, R, 2)), "no table under the track");
        PoolPhysics.Simulate(lv, 1f / PoolPhysics.PlatformSpeed * 1.5f); // 1.5 tiles along
        Assert.AreEqual(2f, pl.center.x, 1e-3f);
        Assert.AreEqual(1.5f, pl.center.z, 1e-3f);
    }

    [Test] public void Platform_ClosedTrack_GoesRoundAndRound()
    {
        Build("####\n#==#\n#H=#\n####\n---\n> 1 1 90\n> 2 1 0\n> 2 2 270\n> 1 2 180\n");
        var pl = lv.platforms[0];
        Assert.IsTrue(pl.loop);
        PoolPhysics.Simulate(lv, 4f / PoolPhysics.PlatformSpeed); // one lap: back at the start (back and forth would be 2 cells away)
        Assert.AreEqual(1f, pl.center.x, 0.01f);
        Assert.AreEqual(1f, pl.center.z, 0.01f);
    }

    [Test] public void Saw_KillsTheBallThatRunsIntoIt()
    {
        Lane("#0..@...#", "> 4 1 0\n"); // blade across the lane
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1.5f);
        Assert.IsTrue(b.falling);
    }

    [Test] public void Saw_RidesItsTrackIntoAStillBall()
    {
        Lane("#.@====.#", "> 2 1 90\n> 3 1 90\n> 4 1 90\n> 5 1 90\n> 6 1 90\n0 15 3\n");
        var b = lv.balls[0];
        Assert.AreEqual(R, b.transform.position.y, 1e-3f, "felt under a saw's track");
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsFalse(b.falling, "not there yet");
        PoolPhysics.Simulate(lv, 1.5f);
        Assert.IsTrue(b.falling);
    }

    [Test] public void Magnet_PullsANearbyBallIn()
    {
        Build("#########\n#.......#\n#..0....#\n#....Z..#\n#########\n");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.Less(b.transform.position.z, 1.8f, "pulled toward the magnet as it passes");

        // a very slow ball near the magnet is released so it stops and the turn can end
        b.transform.position = new Vector3(4, R, 1);
        b.v = Vector3.right * 0.2f;
        b.w = Vector3.zero;
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.IsFalse(b.IsMoving);
    }

    [Test] public void Text_KeepsFloorsAndAngles()
    {
        var floors = new Dictionary<Vector2Int, int>();
        var angles = new Dictionary<Vector2Int, int>();
        var cells = LevelBuilder.ToCells("...\n.S.\n...\n---\n= 1 1 2\n> 1 1 180\n", Vector2Int.zero, new Dictionary<Vector2Int, char>(), angles, floors);
        var text = LevelBuilder.ToText(cells, out _, null, angles, floors);
        Assert.AreEqual(2, LevelBuilder.ParseFloors(text)[new Vector2Int(1, 1)]);
        Assert.AreEqual(180, LevelBuilder.ParseAngles(text)[new Vector2Int(1, 1)]);
        Assert.AreEqual('S', LevelBuilder.Parse(text)[1, 1]);
    }

    [Test] public void SetTile_MovesThePortalInsteadOfAddingOne()
    {
        var cells = new Dictionary<Vector2Int, char> { [Vector2Int.zero] = 'T', [Vector2Int.right] = '.' };
        LevelBuilder.SetTile(cells, new Dictionary<Vector2Int, int>(), Vector2Int.right, 'T', 0);
        Assert.AreEqual('.', cells[Vector2Int.zero]);
        Assert.AreEqual('T', cells[Vector2Int.right]);
    }
}
