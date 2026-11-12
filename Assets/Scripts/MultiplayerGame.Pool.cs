using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Online 8-ball ("Biliardo"): 2 players take turns on a regulation table (Resources/PoolTable.txt) with one shared cue ball.
// Rules.EightBall judges every shot; a foul gives the other player the cue ball in hand (placed with the mouse anywhere on the felt).
// Same lobby, network and physics as the minigolf match: the host simulates, the client only sends its shot / cue placement.
public partial class MultiplayerGame
{
    bool pool;                   // this match is 8-ball (or 9-ball, see nine)
    bool nine;                   // 9-ball: balls 1-9 in a diamond, hit the lowest first, the 9 wins (Rules.NineBall)
    int shotLowest;              // 9-ball: lowest ball on the table when the shot started
    Ball nineBall;
    Vector3 nineSpot;            // where the 9 is respotted
    int tableSkin;               // the host's table skin (GameManager.TableSkin), for everyone
    int turn;                    // player id who shoots next
    bool ballInHand;             // the player on turn places the cue ball first
    bool isBreak, shotRunning, shotCleared;
    int shotFirstHit = -1;
    float shotAt;
    readonly List<int> shotPotted = new(), shotLost = new();
    Ball poolCue;
    Vector3 poolCueStart;
    GameObject handGhost;
    bool handView; // camera switched to the top view for the ball in hand

    void StartPool()
    {
        started = true;
        round = 1;
        BuildLevel();
        if (nine) NineRack();
        PoolBalls();
        poolCueStart = poolCue.transform.position;
        poolCue.onCueHit = hit => { if (shotRunning && shotFirstHit < 0) shotFirstHit = hit.number; };
        foreach (var p in players.Values) p.group = 0;
        turn = players.Keys.ElementAt(Random.Range(0, players.Count)); // who breaks
        ballInHand = shotRunning = false;
        isBreak = true;
        var balls = level.balls;
        net.Send(-1, Net.Msg(Round, w =>
        {
            w.Write(round);
            w.Write(levelText);
            WritePlayers(w);
            w.Write(balls.Count);
            foreach (var b in balls)
            {
                var q = b.transform.position;
                w.Write((byte)b.number); w.Write((sbyte)b.owner); w.Write(q.x); w.Write(q.y); w.Write(q.z);
            }
        }));
        BeginRound();
        ToastAll($"Spacca {Tag(turn)}");
    }

    // 9-ball: the 8-ball rack becomes a diamond of 1-9 on the same apex, the 1 in front, the 9 in the middle, the rest at random
    void NineRack()
    {
        var rack = level.balls.Where(b => b.number != 0).ToList();
        var apex = rack.OrderBy(b => b.transform.position.x).First().transform.position;
        foreach (var b in rack) { level.balls.Remove(b); Destroy(b.gameObject); }
        var rest = Enumerable.Range(2, 7).OrderBy(_ => Random.value).ToList();
        int[] rows = { 1, 2, 3, 2, 1 };
        float s = Ball.Radius * 2 * 1.002f; // same tiny gap as LevelBuilder.Rack
        int next = 0;
        for (int k = 0; k < rows.Length; k++)
            for (int i = 0; i < rows[k]; i++)
            {
                int n = k == 0 ? 1 : k == 2 && i == 1 ? 9 : rest[next++];
                var b = LevelBuilder.SpawnBall(n, apex + new Vector3(k * s * 0.8660254f, 0, (i - (rows[k] - 1) / 2f) * s), level.root);
                level.balls.Add(b);
                if (n == 9) { nineBall = b; nineSpot = b.transform.position; }
            }
    }

    // the shared cue ball is everyone's cue (camera, cue stick, aim lines)
    void PoolBalls()
    {
        poolCue = level.balls.First(b => b.number == 0);
        foreach (var p in players.Values) p.cue = poolCue;
    }

    bool AllStill => level.balls.All(Still);

    bool PoolReady(int id) => playing && id == turn && !shotRunning && !ballInHand && poolCue && poolCue.gameObject.activeSelf && AllStill;

    void PoolShoot(int id, Vector3 dir, float speed, Vector2 tip, float elevation)
    {
        if (!PoolReady(id)) return;
        var group = players[id].group;
        shotLowest = level.balls.Where(b => b.number != 0 && b.gameObject.activeSelf).Select(b => b.number).DefaultIfEmpty(9).Min();
        shotCleared = group != 0 && !level.balls.Any(b => b.gameObject.activeSelf && Rules.GroupOf(b.number) == group);
        PoolPhysics.Shoot(poolCue, dir, speed, tip.x, tip.y, elevation);
        shotRunning = true;
        shotFirstHit = -1;
        shotAt = Time.time;
        shotPotted.Clear();
        shotLost.Clear();
    }

    void PoolHostStep()
    {
        PoolPhysics.Simulate(level, Mathf.Min(Time.deltaTime, 0.05f));
        foreach (var b in level.balls)
        {
            if (!b.gameObject.activeSelf || b.transform.position.y > -0.6f) continue;
            b.gameObject.SetActive(false);
            (b.pocket != null ? shotPotted : shotLost).Add(b.number);
        }
        if (shotRunning && Time.time - shotAt > 0.2f && AllStill) EvaluatePool();
        if (!playing) return; // game over
        myReady = PoolReady(me);
        SendState();
    }

    Player Opponent(int id) => players.Values.FirstOrDefault(p => p.id != id && !p.gone) ?? players[id];

    void EvaluatePool()
    {
        var shooter = players[turn];
        var other = Opponent(turn);
        var s = nine ? Rules.NineBall(shotLowest, shotFirstHit, shotPotted, shotLost)
            : Rules.EightBall(shooter.group, shotCleared, isBreak, shotFirstHit, shotPotted, shotLost);
        shotRunning = isBreak = false;
        if (s.respot) Respawn(nineBall, FreeSpot(nineSpot, nineBall));
        if (s.over) { EndPool(s.shooterWins ? shooter.id : other.id, $"{Tag(shooter.id)}: {s.message}"); return; }
        if (s.assign != 0)
        {
            shooter.group = s.assign;
            other.group = 3 - s.assign;
        }
        if (s.foul)
        {
            turn = other.id;
            ballInHand = true;
            if (!poolCue.gameObject.activeSelf) Respawn(poolCue, FreeSpot(poolCueStart)); // the other player moves it anyway
        }
        else if (!s.keepTurn) turn = other.id;
        ShowHud(); // before the toasts: it clears the screen
        if (s.foul) ToastAll($"{s.message}: palla in mano a {Tag(turn)}");
        if (s.assign != 0) ToastAll($"{Tag(shooter.id)} ha le {Rules.GroupName(s.assign)}");
    }

    // where a ball (the cue ball, or the respotted 9) may be put down: on plain felt, not touching another ball
    bool CueSpotOk(Vector3 q, Ball self = null) => level.Supports(q) && level.TileAt(q) == '.' &&
        !level.balls.Any(b => b != (self ? self : poolCue) && b.gameObject.activeSelf && new Vector2(b.transform.position.x - q.x, b.transform.position.z - q.z).magnitude < 2 * Ball.Radius + 0.01f);

    Vector3 FreeSpot(Vector3 p, Ball self = null)
    {
        for (int i = 0; i < 49; i++)
        {
            var q = Spread(p, i);
            if (CueSpotOk(q, self)) return q;
        }
        return p;
    }

    void HostPlaceCue(int id, Vector3 q)
    {
        if (id != turn || !ballInHand || shotRunning || !level.Supports(q)) return;
        q.y = level.GroundAt(q).Value + Ball.Radius;
        if (!CueSpotOk(q)) return;
        Respawn(poolCue, q);
        ballInHand = false;
        ShowHud();
    }

    // ball in hand on my turn: a see-through cue ball follows the mouse on the felt, left click puts it there
    void HandInput()
    {
        var mouse = Mouse.current;
        bool placing = turn == me && ballInHand;
        if (placing != handView) orbit.Top = handView = placing; // the whole table from above while placing, back after
        bool show = placing && mouse != null;
        Vector3 q = default;
        if (show)
        {
            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            show = new Plane(Vector3.up, new Vector3(0, Ball.Radius, 0)).Raycast(ray, out float d) && CueSpotOk(q = ray.GetPoint(d));
        }
        if (show && !handGhost)
        {
            handGhost = LevelBuilder.SpawnBall(0, Vector3.zero, transform).gameObject;
            Destroy(handGhost.GetComponent<Ball>());
            foreach (var r in handGhost.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = BallArt.Ghost(r.sharedMaterial);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        if (handGhost) handGhost.SetActive(show);
        if (!show) return;
        handGhost.transform.position = q;
        bool overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        if (!overUi && mouse.leftButton.wasPressedThisFrame)
        {
            if (net.IsHost) HostPlaceCue(me, q);
            else net.Send(0, Net.Msg(PlaceCue, w => { w.Write(q.x); w.Write(q.z); }));
        }
    }

    void PoolHud()
    {
        int Down(int group) => group == 0 ? 0 : 7 - level.balls.Count(b => b.gameObject.activeSelf && Rules.GroupOf(b.number) == group);
        string title = turn == me ? (ballInHand ? "Palla in mano: tocca a te" : "Tocca a te") : $"Tocca a {Tag(turn)}";
        string help = turn == me && ballInHand
            ? "Punta il mouse sul panno e clic sinistro = posa la bianca   ·   T = vista dall'alto   ·   Esc = menu"
            : $"Sinistro + mouse = mira   ·   Destro tenuto + mouse indietro = tiro   ·   {(nine ? "colpisci prima la palla più bassa, la 9 vince" : "imbuca il tuo gruppo, poi la nera")}   ·   Esc = menu";
        if (nine)
        {
            int lowest = level.balls.Where(b => b.number != 0 && b.gameObject.activeSelf).Select(b => b.number).DefaultIfEmpty(9).Min();
            ui.ShowMatchHud(title, players.Values.Where(p => !p.gone)
                .Select(p => (PlayerColor(p.id), Name(p.id) + (p.id == turn ? "  ◀" : ""), 0, false)).ToList(), $"prima la {lowest}", help);
            return;
        }
        ui.ShowMatchHud(title, players.Values.Where(p => !p.gone)
            .Select(p => (PlayerColor(p.id), $"{Name(p.id)} · {Rules.GroupName(p.group)}" + (p.id == turn ? "  ◀" : ""), Down(p.group), false)).ToList(), "imbucate", help);
    }

    void EndPool(int winner, string why)
    {
        net.Send(-1, Net.Msg(PoolEnd, w => { w.Write(winner); w.Write(why); }));
        ShowPoolEnd(winner, why);
    }

    void ShowPoolEnd(int winner, string why)
    {
        playing = false;
        CloseMenu(false);
        over = true;
        if (handGhost) handGhost.SetActive(false);
        if (cueCtl) cueCtl.enabled = false;
        if (orbit) orbit.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        var rows = players.Values.OrderByDescending(p => p.id == winner)
            .Select(p => (PlayerColor(p.id), nine ? Name(p.id) : $"{Name(p.id)} · {Rules.GroupName(p.group)}", 0,
                p.group == 0 ? 0 : 7 - level.balls.Count(b => b.gameObject.activeSelf && Rules.GroupOf(b.number) == p.group), float.NaN)).ToList();
        ui.ShowStandings($"{Name(winner)} vince!", why, rows, Leave);
    }
}
