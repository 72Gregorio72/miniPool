using NUnit.Framework;

public class RulesTests
{
    static readonly int[] None = { };
    static Rules.Outcome Eval(int firstHit, int[] potted, int[] lost) => Rules.Evaluate(firstHit, potted, lost);

    [Test] public void EightPotted_Wins_EvenAsFirstShot() => Assert.IsTrue(Eval(3, new[] { 8 }, None).win);

    [Test] public void Crown_OnlyAtOrUnderPar()
    {
        Assert.IsTrue(Rules.Crown(3, 3));
        Assert.IsTrue(Rules.Crown(2, 3));
        Assert.IsFalse(Rules.Crown(4, 3), "over par");
        Assert.IsFalse(Rules.Crown(0, 3), "never won");
        Assert.IsFalse(Rules.Crown(1, 0), "no par set");
    }

    [Test] public void ParLine_ReadAndIgnoredByObjects()
    {
        const string text = "#0#\n---\n8 3 0\ntiri 4\n";
        Assert.AreEqual(4, LevelBuilder.ParOf(text));
        Assert.AreEqual(0, LevelBuilder.ParOf("#0#\n"));
        Assert.AreEqual(1, LevelBuilder.ParseObjects(text).Count);
    }

    [Test] public void FoulsLine_ReadNextToPar()
    {
        const string text = "#0#\n---\n8 3 0\ntiri 4\nfalli 2\n";
        Assert.AreEqual(2, LevelBuilder.FoulsOf(text));
        Assert.AreEqual(4, LevelBuilder.ParOf(text));
        Assert.AreEqual(0, LevelBuilder.FoulsOf("#0#\n"));
    }

    [Test] public void SpendFoul_ForgivesUntilNoneLeft()
    {
        int left = 2;
        Assert.IsTrue(Rules.SpendFoul(ref left));
        Assert.IsTrue(Rules.SpendFoul(ref left));
        Assert.AreEqual(0, left);
        Assert.IsFalse(Rules.SpendFoul(ref left), "none left: restart");
        Assert.AreEqual(0, left);
    }

    [Test] public void AnyBallHitFirst_IsFine()
    {
        var o = Eval(8, None, None);
        Assert.IsFalse(o.restart || o.win);
    }

    [Test] public void ObjectBallsLostOrPotted_NoRestart()
    {
        var o = Eval(2, new[] { 1, 12 }, new[] { 5, 9 });
        Assert.IsFalse(o.restart || o.win);
    }

    [Test] public void CueFouls_Restart()
    {
        Assert.IsTrue(Eval(-1, None, None).restart, "no ball hit");
        Assert.IsTrue(Eval(2, new[] { 0 }, None).restart, "cue potted");
        Assert.IsTrue(Eval(2, None, new[] { 0 }).restart, "cue off the table / spikes");
    }

    [Test] public void LevelBall_IsTheTarget()
    {
        Assert.IsTrue(Rules.Evaluate(BallArt.Grey, new[] { 3 }, None, target: 3).win, "level ball potted, first hit a grey ball");
        Assert.IsFalse(Rules.Evaluate(3, new[] { 8 }, None, target: 3).win, "the 8 is just another ball here");
        Assert.IsTrue(Rules.Evaluate(1, None, new[] { 3 }, target: 3).restart, "level ball lost");
        Assert.IsFalse(Rules.Evaluate(BallArt.Grey, new[] { BallArt.Grey }, new[] { BallArt.Grey }, target: 3).restart, "grey balls can go anywhere");
    }

    [Test] public void Multiplayer_NoBallHit_IsFine()
    {
        var o = Rules.Evaluate(-1, None, None, noHitOk: true);
        Assert.IsFalse(o.restart || o.win);
        Assert.IsTrue(Rules.Evaluate(-1, new[] { 0 }, None, noHitOk: true).restart, "cue potted is still a foul");
    }

    [Test] public void Multiplayer_StartsMirrored()
    {
        Assert.AreEqual(new[] { 0.5f, -0.5f }, new[] { Rules.MirrorSlot(0, 2), Rules.MirrorSlot(1, 2) });
        Assert.AreEqual(new[] { 0f, 1f, -1f }, new[] { Rules.MirrorSlot(0, 3), Rules.MirrorSlot(1, 3), Rules.MirrorSlot(2, 3) });
        for (int n = 1; n <= 8; n++)
        {
            float sum = 0;
            for (int i = 0; i < n; i++) sum += Rules.MirrorSlot(i, n);
            Assert.AreEqual(0, sum, 1e-4f, $"{n} players: symmetric");
        }
    }

    [Test] public void Multiplayer_LastOneLeftEndsTheRound()
    {
        Assert.IsTrue(Rules.RoundOver(1, 2), "one left, others finished: last, round over");
        Assert.IsTrue(Rules.RoundOver(0, 3), "everyone finished");
        Assert.IsFalse(Rules.RoundOver(2, 1), "two still playing");
        Assert.IsFalse(Rules.RoundOver(1, 0), "alone from the start (or everyone else left): still has to pot");
    }

    static Rules.PoolShot Pool(int group, bool cleared, int firstHit, int[] potted, int[] lost = null, bool isBreak = false) =>
        Rules.EightBall(group, cleared, isBreak, firstHit, potted, lost ?? None);

    [Test] public void EightBall_Break()
    {
        var s = Pool(0, false, 1, new[] { 3, 12 }, isBreak: true);
        Assert.IsTrue(!s.foul && s.keepTurn && s.assign == 0, "a pot on the break keeps the turn, table stays open");
        Assert.IsFalse(Pool(0, false, 1, None, isBreak: true).keepTurn);
        Assert.IsTrue(Pool(0, false, 1, new[] { 8 }, isBreak: true).shooterWins);
    }

    [Test] public void EightBall_OpenTableAssignsTheFirstPotted()
    {
        var s = Pool(0, false, 12, new[] { 12, 3 });
        Assert.AreEqual(2, s.assign);
        Assert.IsTrue(s.keepTurn);
        Assert.IsTrue(Pool(0, false, 8, None).foul, "the 8 first on an open table");
    }

    [Test] public void EightBall_TurnAndFouls()
    {
        Assert.IsTrue(Pool(1, false, 2, new[] { 2 }).keepTurn);
        var s = Pool(1, false, 2, new[] { 10 });
        Assert.IsTrue(!s.foul && !s.keepTurn, "only the opponent's ball went in: turn passes, no foul");
        Assert.IsTrue(Pool(1, false, 10, new[] { 2 }).foul, "hit the opponent's ball first");
        Assert.IsTrue(Pool(1, false, 2, new[] { 0 }).foul, "scratch");
        Assert.IsTrue(Pool(1, false, -1, None).foul, "nothing hit");
        Assert.IsTrue(Pool(1, false, 2, None, new[] { 5 }).foul, "ball off the table");
        Assert.IsTrue(Pool(1, true, 3, None).foul, "group cleared: the 8 must be hit first");
    }

    [Test] public void EightBall_TheEight()
    {
        Assert.IsTrue(Pool(1, true, 8, new[] { 8 }).shooterWins);
        var early = Pool(1, false, 2, new[] { 2, 8 });
        Assert.IsTrue(early.over && !early.shooterWins, "the 8 before the group is cleared loses");
        var scratch = Pool(1, true, 8, new[] { 8, 0 });
        Assert.IsTrue(scratch.over && !scratch.shooterWins, "the 8 with a scratch loses");
        Assert.IsFalse(Pool(1, true, 8, None, new[] { 8 }).shooterWins, "the 8 off the table loses");
    }

    [Test] public void EightLost_Restarts() => Assert.IsTrue(Eval(8, None, new[] { 8 }).restart);

    [Test] public void EightPottedWithCue_RestartsNotWins()
    {
        var o = Eval(8, new[] { 8, 0 }, None);
        Assert.IsTrue(o.restart && !o.win);
    }

    [Test] public void Multiplayer_EightsIgnoreOthersAndForeignCues()
    {
        Assert.IsFalse(Rules.Collide(8, 0, 8, 1), "8s pass through each other");
        Assert.IsFalse(Rules.Collide(0, 1, 8, 0), "a cue cannot hit another player's 8");
        Assert.IsTrue(Rules.Collide(8, 1, 0, 1), "own cue hits own 8");
        Assert.IsTrue(Rules.Collide(0, 0, 0, 1), "cues hit each other");
        Assert.IsTrue(Rules.Collide(8, 0, 3, -1), "8 hits object balls");
        Assert.IsTrue(Rules.Collide(0, -1, 8, -1), "single player unchanged");
    }

    [Test] public void LevelText_RoundTripsThroughCells()
    {
        const string text = "##O\n#.0\n  #\n";
        var cells = LevelBuilder.ToCells(text, new UnityEngine.Vector2Int(5, -3));
        Assert.AreEqual('O', cells[new UnityEngine.Vector2Int(7, -1)]); // top-right
        Assert.IsFalse(cells.ContainsKey(new UnityEngine.Vector2Int(5, -3))); // spaces are empty
        Assert.AreEqual(text, LevelBuilder.ToText(cells, out var min));
        Assert.AreEqual(new UnityEngine.Vector2Int(5, -3), min);
    }

    [Test] public void LevelText_ObjectsRoundTripOnTheFineGrid()
    {
        const string text = "###\n#>#\n#.#\n---\n8 4 0\n0 3 4\n";
        var objs = new System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, char>();
        var cells = LevelBuilder.ToCells(text, new UnityEngine.Vector2Int(2, 1), objs);
        Assert.AreEqual('>', cells[new UnityEngine.Vector2Int(3, 2)], "the booster tile stays under the ball");
        Assert.AreEqual('0', objs[new UnityEngine.Vector2Int(3 + 6, 4 + 3)]);
        Assert.AreEqual(text, LevelBuilder.ToText(cells, out _, objs));
    }

    [Test] public void LevelText_GridBallsBecomeObjects()
    {
        var objs = new System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, char>();
        var cells = LevelBuilder.ToCells("#0#\n", UnityEngine.Vector2Int.zero, objs);
        Assert.AreEqual('.', cells[new UnityEngine.Vector2Int(1, 0)]);
        Assert.AreEqual('0', objs[new UnityEngine.Vector2Int(3, 0)]);
    }

    [Test] public void LevelText_BoosterAnglesRoundTrip()
    {
        var objs = new System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, char>();
        var angles = new System.Collections.Generic.Dictionary<UnityEngine.Vector2Int, int>();
        var cells = LevelBuilder.ToCells("#^#\n#V#\n---\n> 1 1 45\n", UnityEngine.Vector2Int.zero, objs, angles);
        Assert.AreEqual(45, angles[new UnityEngine.Vector2Int(1, 1)], "angle line wins over the char");
        Assert.AreEqual(180, angles[new UnityEngine.Vector2Int(1, 0)], "legacy V = 180");
        Assert.AreEqual("#>#\n#>#\n---\n> 1 0 180\n> 1 1 45\n", LevelBuilder.ToText(cells, out _, objs, angles));
    }
}
