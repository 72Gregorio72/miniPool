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

    [Test] public void Magnet_PullsANearbyBallIn()
    {
        Lane("#.......Z..#", "0 18 3\n");
        var b = lv.balls[0];
        PoolPhysics.Simulate(lv, 8f);
        Assert.AreEqual(8, b.transform.position.x, 0.3f);
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
