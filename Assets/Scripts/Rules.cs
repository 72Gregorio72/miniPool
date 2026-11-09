using System.Collections.Generic;
using System.Linq;

// Level rules, kept pure so they can be unit-tested.
// Goal: pot the 8. Object balls (1-7, 9-15) can be potted or lost freely, in any order.
// The level restarts only on a cue foul (no ball hit, cue potted, cue lost) or when the 8 is lost (off the table / spikes).
public static class Rules
{
    public struct Outcome
    {
        public bool restart, win;
        public string message;
    }

    // crown on a level: won in at most `par` shots (best = fewest shots of a winning run, 0 = never won; par 0 = not set)
    public static bool Crown(int best, int par) => par > 0 && best > 0 && best <= par;

    /// <param name="firstHit">first ball the cue touched, -1 if none</param>
    /// <param name="potted">balls that dropped into a pocket this shot</param>
    /// <param name="lost">balls that left the table without a pocket (fell off, spikes) this shot</param>
    /// <param name="noHitOk">multiplayer: a shot that hits nothing is fine (moving the cue into position)</param>
    public static Outcome Evaluate(int firstHit, IReadOnlyCollection<int> potted, IReadOnlyCollection<int> lost, bool noHitOk = false)
    {
        var o = new Outcome();
        if (potted.Contains(0)) o.message = "Bianca in buca";
        else if (lost.Contains(0)) o.message = "Bianca persa";
        else if (firstHit < 0 && !noHitOk) o.message = "Nessuna palla colpita";
        else if (lost.Contains(8)) o.message = "Nera persa";
        o.restart = o.message != null;
        o.win = !o.restart && potted.Contains(8);
        return o;
    }

    // multiplayer: the round ends when nobody is left playing, or when only one is left and someone already finished
    // (that one is last anyway, no need to pot). A player alone from the start still has to pot.
    public static bool RoundOver(int stillPlaying, int finished) => stillPlaying == 0 || (stillPlaying == 1 && finished > 0);

    // multiplayer start: slot i of n players, in steps around the 8, mirrored left/right so the layout is symmetric:
    // odd n = 0, +1, -1, +2, -2…   even n = +0.5, -0.5, +1.5, -1.5…
    public static float MirrorSlot(int i, int n) =>
        n % 2 == 1 ? (i + 1) / 2 * (i % 2 == 1 ? 1 : -1) : (i / 2 + 0.5f) * (i % 2 == 0 ? 1 : -1);

    // multiplayer: every player owns a cue and an 8 (owner >= 0). The 8s pass through each other (they all start on
    // the same spot) and an 8 is hit only by its own cue; everything else collides. Single player: owners are all -1.
    public static bool Collide(int numberA, int ownerA, int numberB, int ownerB)
    {
        bool eightA = numberA == 8, eightB = numberB == 8;
        if (eightA && eightB) return ownerA == ownerB;
        if ((eightA && numberB == 0) || (eightB && numberA == 0)) return ownerA == ownerB;
        return true;
    }

    // ---------- 8-ball (online "Biliardo" mode, 2 players, turns) ----------
    // groups: 1 = solids (1-7), 2 = stripes (9-15), 0 = none (the 8, or the table is still open)
    public static int GroupOf(int n) => n >= 1 && n <= 7 ? 1 : n >= 9 && n <= 15 ? 2 : 0;
    public static string GroupName(int g) => g == 1 ? "piene" : g == 2 ? "mezze" : "tavolo aperto";

    public struct PoolShot
    {
        public bool foul, keepTurn, over, shooterWins; // foul: the other player gets the cue ball in hand
        public int assign;                              // open table: the group the shooter gets (0 = still open)
        public string message;
    }

    // One shot. group = the shooter's group (0 = open table); cleared = their group was all down before this shot (now the 8 is theirs).
    // potted in the order they dropped. Simplified: no rail-after-contact rule, no called pocket, balls off the table stay out.
    public static PoolShot EightBall(int group, bool cleared, bool isBreak, int firstHit, IReadOnlyList<int> potted, IReadOnlyCollection<int> lost)
    {
        var s = new PoolShot();
        if (potted.Contains(0)) s.message = "Bianca in buca";
        else if (lost.Contains(0)) s.message = "Bianca fuori dal tavolo";
        else if (firstHit < 0) s.message = "Nessuna palla colpita";
        else if (group == 0 && firstHit == 8 && !isBreak) s.message = "Nera colpita per prima";
        else if (group != 0 && !cleared && GroupOf(firstHit) != group) s.message = "Colpita per prima una palla non tua";
        else if (group != 0 && cleared && firstHit != 8) s.message = "Dovevi colpire la nera";
        else if (lost.Any(n => n != 8)) s.message = "Palla fuori dal tavolo";
        s.foul = s.message != null;

        if (potted.Contains(8) || lost.Contains(8))
        {
            s.over = true;
            // shortcut: the 8 on a clean break wins outright (no re-rack / respot), fine for a casual game
            s.shooterWins = potted.Contains(8) && !s.foul && (isBreak || (group != 0 && cleared));
            s.message = s.shooterWins ? "Nera in buca: vittoria" : lost.Contains(8) ? "Nera fuori dal tavolo"
                : s.foul ? "Nera in buca con fallo (" + s.message + ")" : "Nera in buca troppo presto";
            return s;
        }
        if (s.foul) return s;
        var balls = potted.Where(n => n != 0 && n != 8).ToList();
        if (group == 0 && !isBreak && balls.Count > 0) s.assign = GroupOf(balls[0]);
        int mine = group != 0 ? group : s.assign;
        s.keepTurn = mine == 0 ? balls.Count > 0 : balls.Any(n => GroupOf(n) == mine);
        return s;
    }
}
