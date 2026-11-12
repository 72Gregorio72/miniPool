using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class PhysicsTests
{
    LevelBuilder.Level lv;

    LevelBuilder.Level Make(string row) => lv = LevelBuilder.Build("###########################################\n" + row + "\n###########################################", "test");

    [TearDown] public void Clean() { if (lv?.root) Object.DestroyImmediate(lv.root.gameObject); lv = null; } // pure-logic tests build no level

    static float Slip(Ball b) => new Vector2(b.v.x + Ball.Radius * b.w.z, b.v.z - Ball.Radius * b.w.x).magnitude;

    [Test] public void StunShot_EndsRollingAtFiveSevenths()
    {
        Make("#0.......................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 10, 0, 0);
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.Less(Slip(b), 0.01f);
        Assert.AreEqual(10f * 5 / 7, b.v.x, 0.5f); // minus a little rolling resistance
        Assert.AreEqual(0, b.transform.position.y - Ball.Radius, 1e-4f); // never leaves the cloth
    }

    [Test] public void JumpShot_FliesOverBall()
    {
        Make("#0..8....................................#");
        var cue = lv.balls[0];
        int hit = -1;
        cue.onCueHit = o => hit = o.number;
        PoolPhysics.Shoot(cue, Vector3.right, 20, 0, 0, PoolPhysics.MinElevation);
        PoolPhysics.Simulate(lv, 1.5f);
        Assert.AreEqual(-1, hit, "the cue ball should jump over the 8");
        Assert.Greater(cue.transform.position.x, lv.balls[1].transform.position.x + 1);
        Assert.AreEqual(0, cue.transform.position.y - Ball.Radius, 1e-3f); // landed
    }

    [Test] public void JumpShot_HitsBallStillInTheWay()
    {
        Make("#08......................................#"); // a soft jump stays lower than a ball: it hits the 8 in the air
        var cue = lv.balls[0];
        int hit = -1;
        cue.onCueHit = o => hit = o.number;
        PoolPhysics.Shoot(cue, Vector3.right, 8, 0, 0, PoolPhysics.MinElevation);
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.AreEqual(8, hit, "a flying cue ball must not pass through a ball it meets");
        Assert.Greater(lv.balls[1].v.x + lv.balls[1].transform.position.x, 1.5f, "the 8 was pushed forward");
    }

    [Test] public void DrawShot_ComesBack()
    {
        Make("#...0.8..................................#");
        var cue = lv.balls[0];
        PoolPhysics.Shoot(cue, Vector3.right, 15, 0, -0.5f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.Less(cue.v.x, 0, "cue ball should pull back after hitting the 8");
        Assert.Greater(lv.balls[1].v.x, 5);
    }

    [Test] public void HeadOn_TransfersSpeed()
    {
        Make("#0.8.....................................#");
        int hit = -1;
        lv.balls[0].onCueHit = o => hit = o.number;
        PoolPhysics.Shoot(lv.balls[0], Vector3.right, 10, 0, 0);
        PoolPhysics.Simulate(lv, 0.2f);
        Assert.AreEqual(8, hit);
        Assert.Greater(lv.balls[1].v.x, lv.balls[0].v.x * 3);
    }

    [Test] public void Cushion_BouncesBackSlower()
    {
        Make("#..0.....................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.left, 10, 0, 0.4f); // natural roll
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.v.x, 0);
        Assert.Less(b.v.magnitude, 10 * PoolPhysics.ECushion);
        Assert.GreaterOrEqual(b.transform.position.x, 0.5f + Ball.Radius - 1e-3f);
    }

    [Test] public void Cast_FindsBallAhead()
    {
        Make("#0...8...................................#");
        var hit = PoolPhysics.Cast(lv, lv.balls[0].transform.position, Vector3.right, lv.balls[0]);
        Assert.AreEqual(lv.balls[1], hit.ball);
        Assert.AreEqual(4 - 2 * Ball.Radius, hit.distance, 1e-3f);
    }

    [Test] public void Rack_FifteenBallsNoOverlap()
    {
        Make("#0......R................................#");
        Assert.AreEqual(16, lv.balls.Count);
        for (int i = 0; i < lv.balls.Count; i++)
            for (int j = i + 1; j < lv.balls.Count; j++)
                Assert.GreaterOrEqual((lv.balls[i].transform.position - lv.balls[j].transform.position).magnitude, 2 * Ball.Radius);
    }

    [Test] public void RackNumbers_FollowEightBallRules()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var r = LevelBuilder.RackNumbers(new System.Random(seed));
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, r);
            Assert.AreEqual(8, r[4]);
            Assert.AreNotEqual(r[10] < 8, r[14] < 8, "back corners: one solid, one stripe");
        }
    }

    [Test] public void SidePocket_StraightShotDrops()
    {
        lv = LevelBuilder.Build("####O####\n#.......#\n#.......#\n#...0...#\n#########", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.forward, 8, 0, 0);
        PoolPhysics.Simulate(lv, 1.5f);
        Assert.IsTrue(b.falling);
        Assert.IsNotNull(b.pocket);
        Assert.Less(b.transform.position.y, 0);
    }

    [Test] public void CornerPocket_DiagonalShotPassesJawsAndDrops()
    {
        lv = LevelBuilder.Build("O#####\n#.....\n#.....\n#..0..\n######", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, new Vector3(-1, 0, 1).normalized, 8, 0, 0);
        PoolPhysics.Simulate(lv, 1.5f);
        Assert.IsTrue(b.falling);
        Assert.AreEqual(new Vector2Int(0, 4), b.pocket.cell);
    }

    [Test] public void Jaw_IsAngledNotSquare()
    {
        lv = LevelBuilder.Build("####O####\n#.......#\n#########", "test");
        // side pocket at (4,2), nose line z = 1.5: the mouth is wider than the tile and narrows going in
        bool Solid(float x, float z) => lv.walls.Any(w => { Poly.Closest(w.pts, new Vector2(x, z), out bool inside); return inside; });
        Assert.IsFalse(Solid(4.52f, 1.55f), "just inside the mouth, past the pocket tile edge: open");
        Assert.IsTrue(Solid(4.52f, 2.1f), "deeper at the same offset: the jaw has closed in");
        Assert.IsTrue(Solid(3.4f, 1.6f), "rail beyond the mouth is still solid");
        Assert.IsFalse(Solid(4f, 1.9f), "pocket axis is open");
    }

    [Test] public void Cushions_AreTrianglesNotBoxes()
    {
        lv = LevelBuilder.Build("#####\n#...#\n#####", "test");
        var mesh = lv.root.Find("Cushions").GetComponent<MeshFilter>().sharedMesh;
        Assert.IsTrue(mesh.normals.Any(n => n.y < -0.1f && Mathf.Abs(n.y) < 0.99f), "sloped face (hypotenuse) looks down toward the felt");
        Assert.LessOrEqual(mesh.vertices.Max(v => v.y), LevelBuilder.CushionHeight + 1e-4f);
    }

    [Test] public void Rolling_EventuallyStops()
    {
        Make("#0.......................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 3, 0, 0.4f);
        PoolPhysics.Simulate(lv, 10f);
        Assert.IsFalse(b.IsMoving);
    }
    [Test] public void Trampoline_JumpsOverAGap()
    {
        Make("#0..J ...................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 15, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.2f);
        Assert.IsTrue(b.airborne, "launched off the trampoline");
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.IsFalse(b.falling || b.airborne);
        Assert.Greater(b.transform.position.x, 6f);
        Assert.AreEqual(Ball.Radius, b.transform.position.y, 1e-4f);
    }

    [Test] public void Gap_WithoutTrampoline_DropsTheBall()
    {
        Make("#0... ...................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 15, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.IsTrue(b.falling);
    }

    [Test] public void Spikes_KillTheBall()
    {
        Make("#0..X....................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 5, 0, 0);
        PoolPhysics.Simulate(lv, 1f);
        Assert.IsTrue(b.falling && b.pocket == null);
        Assert.Less(b.transform.position.y, -0.6f);
    }

    [Test] public void Booster_SpeedsUpTheBall()
    {
        Make("#0.>.....................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 4, 0, 0.4f);
        PoolPhysics.Simulate(lv, 1f);
        Assert.Greater(b.transform.position.x, 7f);
    }

    [Test] public void Booster_TurnsTheBall()
    {
        lv = LevelBuilder.Build("#####\n#...#\n#0.^#\n#...#\n#...#\n#####", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.37f); // pushed at the center (~0.34 s), not yet back from the far rail (~0.42 s)
        Assert.Greater(b.v.z, PoolPhysics.BoostSpeed * 0.8f);
    }

    [Test] public void Booster_PushesAtTheCenter()
    {
        lv = LevelBuilder.Build("#####\n#...#\n#0.^#\n#...#\n#...#\n#####", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        for (int i = 0; i < 300 && b.v.z < 1; i++) PoolPhysics.Simulate(lv, 0.002f);
        Assert.Greater(b.v.z, 1, "pushed");
        Assert.AreEqual(3, b.transform.position.x, 0.1f, "pushed from the booster's center, not its edge");
    }

    [Test] public void Guide_PredictsTheHitAndPutsEverythingBack()
    {
        Make("#0...8...................................#");
        var cue = lv.balls[0];
        var start = cue.transform.position;
        var cuePath = new System.Collections.Generic.List<Vector3>();
        var hitPath = new System.Collections.Generic.List<Vector3>();
        PoolPhysics.Predict(lv, cue, Vector3.right, 10, Vector2.zero, 0, cuePath, hitPath);
        Assert.IsNotEmpty(hitPath, "the 8 gets hit");
        Assert.Greater(hitPath[hitPath.Count - 1].x, lv.balls[1].transform.position.x + 0.5f, "and rolls on");
        Assert.AreEqual(start, cue.transform.position);
        Assert.AreEqual(Vector3.zero, cue.v);
        Assert.AreEqual(0, lv.time);

        PoolPhysics.Predict(lv, cue, Vector3.right, 15, new Vector2(0, -0.5f), 0, cuePath, hitPath);
        Assert.Less(cuePath[cuePath.Count - 1].x, hitPath[0].x - 2 * Ball.Radius, "draw: the guide shows the cue coming back");
    }

    [Test] public void Ramp_FastBallJumpsOffTheTop()
    {
        const string text = "##########\n#0.S.....#\n##########\n---\n> 3 1 90\n= 4 1 1\n= 5 1 1\n= 6 1 1\n= 7 1 1\n= 8 1 1\n";
        float Peak(float speed)
        {
            lv = LevelBuilder.Build(text, "test");
            var b = lv.balls[0];
            PoolPhysics.Shoot(b, Vector3.right, speed, 0, 0);
            float top = 0;
            for (int i = 0; i < 150 && b.transform.position.x < 6.5f; i++)
            {
                PoolPhysics.Simulate(lv, 0.002f);
                top = Mathf.Max(top, b.transform.position.y);
            }
            Object.DestroyImmediate(lv.root.gameObject);
            return top - LevelBuilder.FloorStep - Ball.Radius;
        }
        Assert.Greater(Peak(25), 0.05f, "fast: flies over the upper floor");
        Assert.Less(Peak(9), 0.01f, "slow: rolls on");
        lv = LevelBuilder.Build(text, "test"); // for TearDown
    }

    [Test] public void Booster_CancelsSpin()
    {
        lv = LevelBuilder.Build("#####\n#...#\n#0.^#\n#...#\n#...#\n#####", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0.4f, -0.4f); // side spin + draw
        for (int i = 0; i < 150 && b.v.z < 1; i++) PoolPhysics.Simulate(lv, 0.01f); // draw slows it: center reached at ~0.63 s
        Assert.Greater(b.v.z, PoolPhysics.BoostSpeed * 0.8f, "pushed");
        Assert.AreEqual(0, b.w.y, 1e-3f, "no side spin");
        Assert.AreEqual(b.v.z / Ball.Radius, b.w.x, 0.5f, "plain rolling, no draw or follow");
        Assert.AreEqual(0, b.w.z, 0.5f);
    }

    [Test] public void Block_BouncesTheBall()
    {
        Make("#0...P...................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 10, 0, 0.4f);
        PoolPhysics.Simulate(lv, 0.6f);
        Assert.Less(b.v.x, 0);
        Assert.Less(b.transform.position.x, 4.5f - Ball.Radius + 1e-3f); // the block fills its whole tile
    }

    [Test] public void Objects_SitOnTilesAtFineGridPositions()
    {
        lv = LevelBuilder.Build("#####\n#.>.#\n#####\n---\n0 7 3\n", "test");
        var b = lv.balls.Single();
        Assert.AreEqual(new Vector3(7f / 3, Ball.Radius, 1), b.transform.position);
        Assert.AreEqual('>', lv.TileAt(b.transform.position), "the ball is on the booster, not replacing it");
    }

    [Test] public void Booster_PushesAtAnyAngle()
    {
        lv = LevelBuilder.Build("#######\n#.....#\n#.....#\n#0.>..#\n#.....#\n#.....#\n#######\n---\n> 3 3 45\n", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 6, 0, 0.4f);
        for (int i = 0; i < 100 && b.v.z < 1; i++) PoolPhysics.Simulate(lv, 0.005f); // until pushed at the center
        var dir = new Vector3(b.v.x, 0, b.v.z).normalized;
        Assert.Greater(Vector3.Dot(dir, LevelBuilder.AngleDir(45)), 0.97f);
    }

    // shooting along +x: right of the shot is -z, left is +z

    [Test] public void RightEnglish_LevelCue_GoesNearlyStraight_SquirtsLeft()
    {
        Make("#0.......................................#");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 10, 0.5f, 0);
        float deg = Mathf.Atan2(b.v.z, b.v.x) * Mathf.Rad2Deg;
        Assert.AreEqual(PoolPhysics.SquirtDeg * 0.5f, deg, 0.01f, "a little off the line, away from the english");
        Assert.Less(b.w.y, 0, "right english = clockwise seen from above");
    }

    [Test] public void RightEnglish_ThrowsObjectBallLeft()
    {
        Make("#0.8.....................................#");
        PoolPhysics.Shoot(lv.balls[0], Vector3.right, 8, 0.5f, 0);
        lv.balls[0].v = Vector3.right * 8; // no squirt: isolate the throw
        PoolPhysics.Simulate(lv, 0.35f); // stun, sliding: contact at ~0.26 s
        Assert.Greater(lv.balls[1].v.z, 0.05f);
    }

    [Test] public void CutShot_ThrowsObjectBallTowardTheCueDirection()
    {
        // cue hits the 8's lower half moving +x: line of centers points up-right, friction drags the 8 along +x a bit
        lv = LevelBuilder.Build("#######\n#.....#\n#.....#\n#.....#\n#######\n---\n0 3 6\n8 9 7\n", "test");
        var cue = lv.balls[0];
        var eight = lv.balls[1];
        PoolPhysics.Shoot(cue, Vector3.right, 8, 0, 0);
        float dz = eight.transform.position.z - cue.transform.position.z;
        PoolPhysics.Simulate(lv, 0.3f);
        // without friction the 8 would leave along the line of centers at contact (the cue keeps its z: offset = (sqrt(4R²-dz²), dz));
        // with throw its direction turns toward the cue's travel (+x)
        var centers = new Vector2(Mathf.Sqrt(4 * Ball.Radius * Ball.Radius - dz * dz), dz).normalized;
        var moved = new Vector2(eight.v.x, eight.v.z).normalized;
        Assert.Less(Vector2.SignedAngle(Vector2.right, moved), Vector2.SignedAngle(Vector2.right, centers) - 0.3f);
    }

    [Test] public void RaisedCue_RightEnglish_CurvesRight()
    {
        lv = LevelBuilder.Build("###########\n#.........#\n#.........#\n#.........#\n#.........#\n#.........#\n###########\n---\n0 3 9\n", "test");
        var b = lv.balls[0];
        PoolPhysics.Shoot(b, Vector3.right, 8, 0.5f, 0, 60);
        float startZ = b.transform.position.z;
        PoolPhysics.Simulate(lv, 0.5f);
        Assert.Less(b.v.z, 0, "heading right");
        Assert.Less(b.transform.position.z, startZ - 0.2f, "curved right despite squirting left at first");
    }

    [Test] public void Juice_SlowMotionOnlyHeadingIntoPocket()
    {
        Make("#........................................8O");
        var b = lv.balls[0];
        b.v = Vector3.right * 3;
        Assert.Greater(Juice.Approach(lv, b), 0, "rolling into the pocket");
        b.v = Vector3.left * 3;
        Assert.AreEqual(0, Juice.Approach(lv, b), "rolling away");
        b.v = Vector3.forward * 3;
        Assert.AreEqual(0, Juice.Approach(lv, b), "passing by");
    }
}
