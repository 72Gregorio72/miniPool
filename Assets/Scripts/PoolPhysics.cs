using System.Collections.Generic;
using UnityEngine;

// Custom billiard physics on the table plane: sliding -> rolling transition, rolling resistance,
// spin (follow/draw/english), elastic ball-ball and lossy cushion impacts. PhysX can't do these well.
// Scale: 1 unit = 1/7 m (ball radius 0.25 ~ real 0.036 m), so accelerations are real values * 7.
// Floors: every cell has a height (LevelBuilder floors, ramps); a ball follows the ground under it and flies off a step down.
public static class PoolPhysics
{
    public const float G = 9.81f * 7f;
    public const float MuSlide = 0.2f;    // ball-cloth sliding friction
    public const float MuRoll = 0.01f;    // rolling resistance
    public const float SpinDecel = 10f;   // rad/s^2, vertical-axis spin friction
    public const float EBall = 0.95f;     // ball-ball restitution
    public const float ECushion = 0.75f;  // ball-cushion restitution
    public const float MuCushion = 0.2f;  // cushion friction (lets english change the rebound)
    const float R = Ball.Radius, MaxDt = 0.001f;

    // tipX/tipY: cue tip offset from the ball center in radii (-0.5..0.5); +X right english, +Y follow, -Y draw
    public const float SquirtDeg = 5f;     // squirt: degrees the cue ball leaves the aim line per unit of side tip (2.5° at max english), away from the english
    public const float MaxElevation = 70f; // cue butt raised up to this angle (massé)
    public const float MinElevation = -20f; // below 0 the cue tilts up and hits from below: jump shot
    public const float MuBall = 0.06f;     // ball-ball friction: throw

    // elevation: degrees the cue butt is raised. The cue pushes along its own axis at the tip offset, so a raised cue
    // with side english gives spin around an axis along the shot: the cloth then curves the ball toward the english (swerve, massé).
    // The downward part of the push goes into the cloth (no jump); a negative elevation (cue tilted up, hitting from below) launches the ball.
    public static void Shoot(Ball cue, Vector3 dir, float speed, float tipX, float tipY, float elevation = 0)
    {
        dir = Quaternion.Euler(0, -SquirtDeg * tipX, 0) * dir;
        float e = Mathf.Clamp(elevation, MinElevation, MaxElevation) * Mathf.Deg2Rad;
        var side = Vector3.Cross(Vector3.up, dir);                   // right of the shot
        var axis = dir * Mathf.Cos(e) - Vector3.up * Mathf.Sin(e);   // the cue's push direction
        var faceUp = Vector3.Cross(axis, side);                      // "up" on the ball as the cue sees it
        var r = (side * tipX + faceUp * tipY) * R;                   // where the tip hits, from the center
        cue.v = dir * (speed * Mathf.Cos(e));
        if (e < 0)
        {
            cue.v.y = -speed * Mathf.Sin(e);
            cue.airborne = true; // flies over balls (no ball contacts in the air), lands where it comes down
        }
        cue.w = Vector3.Cross(r, axis) * (speed * 2.5f / (R * R));  // solid sphere: I = 2/5 m R²
    }

    public struct CastHit
    {
        public float distance;
        public Ball ball;      // null = cushion (or nothing)
        public Vector3 normal; // at the contact, pointing back toward the moving ball
    }

    // Where a ball starting at `from` moving along `dir` first touches another ball or a cushion (for the aim guide).
    public static CastHit Cast(LevelBuilder.Level lv, Vector3 from, Vector3 dir, Ball ignore)
    {
        var hit = new CastHit { distance = 100f };
        foreach (var b in lv.balls)
        {
            if (b == ignore || !b.gameObject.activeSelf || b.falling) continue;
            var oc = b.transform.position - from;
            oc.y = 0;
            float along = Vector3.Dot(oc, dir), perp2 = oc.sqrMagnitude - along * along, r2 = 4 * R * R;
            if (along < 0 || perp2 > r2) continue;
            float t = along - Mathf.Sqrt(r2 - perp2);
            if (t >= 0 && t < hit.distance)
            {
                var n = from + dir * t - b.transform.position;
                n.y = 0;
                hit = new CastHit { distance = t, ball = b, normal = n.normalized };
            }
        }
        // ray against each wall polygon grown by the ball radius: offset edges + circles at the corners
        var o = new Vector2(from.x, from.z);
        var d = new Vector2(dir.x, dir.z);
        foreach (var w in lv.Sheet(from).walls)
            for (int i = 0; i < w.pts.Length; i++)
            {
                Vector2 a = w.pts[i], e = w.pts[(i + 1) % w.pts.Length] - a;
                var m = new Vector2(e.y, -e.x).normalized; // outward for counter-clockwise polygons
                float dm = Vector2.Dot(d, m);
                if (dm < -1e-6f)
                {
                    float t = Vector2.Dot(a + m * R - o, m) / dm;
                    float s = Vector2.Dot(o + d * t - a, e) / e.sqrMagnitude;
                    if (t >= 0 && t < hit.distance && s >= 0 && s <= 1) hit = new CastHit { distance = t, normal = new Vector3(m.x, 0, m.y) };
                }
                var ac = a - o;
                float along = Vector2.Dot(ac, d), perp2 = ac.sqrMagnitude - along * along;
                if (along > 0 && perp2 < R * R)
                {
                    float t = along - Mathf.Sqrt(R * R - perp2);
                    if (t >= 0 && t < hit.distance)
                    {
                        var n = (o + d * t - a).normalized;
                        hit = new CastHit { distance = t, normal = new Vector3(n.x, 0, n.y) };
                    }
                }
            }
        return hit;
    }

    // Aim guide: runs the shot ahead with the real physics (spin, massé, cushions, modules), then puts everything back.
    // cuePath = the cue ball until `after` seconds past its first ball contact (or until it stops); hitPath = that ball meanwhile.
    // Other balls keep their current velocity (on clients they're still: only positions arrive).
    // `seconds` caps a shot that hits nothing (most stop earlier): 2 s = 1000 steps at most, so aiming online with a full rack stays smooth.
    public static void Predict(LevelBuilder.Level lv, Ball cue, Vector3 dir, float speed, Vector2 tip, float elevation,
        System.Collections.Generic.List<Vector3> cuePath, System.Collections.Generic.List<Vector3> hitPath, float seconds = 2f, float after = 0.6f)
    {
        cuePath.Clear();
        hitPath.Clear();
        var balls = lv.balls;
        var saved = new (Vector3 p, Vector3 v, Vector3 w, bool falling, bool airborne, Vector2Int? cell, bool armed, LevelBuilder.Pocket pocket,
            float belt, bool onBelt, bool carried, System.Action<Ball> onHit)[balls.Count];
        for (int i = 0; i < balls.Count; i++)
        {
            var b = balls[i];
            saved[i] = (b.transform.position, b.v, b.w, b.falling, b.airborne, b.cell, b.boostArmed, b.pocket, b.beltSpeed, b.onBelt, b.carried, b.onCueHit);
            b.onCueHit = null;
        }
        float time = lv.time;
        Ball hit = null;
        cue.onCueHit = o => hit ??= o;

        Shoot(cue, dir, speed, tip.x, tip.y, elevation);
        cuePath.Add(cue.transform.position);
        const float dt = 0.002f; // shortcut: 2x the game step to halve the cost; drop to MaxDt if the guide drifts from real shots
        float hitAt = -1;
        for (int n = 1; n * dt < seconds; n++)
        {
            Step(lv, dt, true);
            if (hit != null && hitAt < 0) { hitAt = n * dt; hitPath.Add(hit.transform.position); }
            if (n % 8 == 0) { cuePath.Add(cue.transform.position); if (hit != null) hitPath.Add(hit.transform.position); }
            if (cue.falling || !cue.IsMoving && (hit == null || !hit.IsMoving) || hitAt >= 0 && n * dt - hitAt > after) break;
        }
        cuePath.Add(cue.transform.position);
        if (hit != null) hitPath.Add(hit.transform.position);

        for (int i = 0; i < balls.Count; i++)
        {
            var b = balls[i];
            var s = saved[i];
            b.transform.position = s.p;
            (b.v, b.w, b.falling, b.airborne, b.cell, b.boostArmed, b.pocket, b.beltSpeed, b.onBelt, b.carried, b.onCueHit) =
                (s.v, s.w, s.falling, s.airborne, s.cell, s.armed, s.pocket, s.belt, s.onBelt, s.carried, s.onHit);
        }
        lv.time = time;
        UpdateMoving(lv);
    }

    public static void Simulate(LevelBuilder.Level lv, float time)
    {
        int n = Mathf.CeilToInt(time / MaxDt);
        var start = new Vector3[lv.balls.Count];
        for (int i = 0; i < start.Length; i++) start[i] = lv.balls[i].transform.position;
        for (int i = 0; i < n; i++) Step(lv, time / n);

        for (int i = 0; i < lv.balls.Count; i++)
        {
            var b = lv.balls[i];
            // on a belt and actually going somewhere (not pushed against a wall): still part of the shot
            b.carried = b.onBelt && (b.transform.position - start[i]).magnitude > 0.3f * b.beltSpeed * time;
            // in a fan's air but already against a rail / ball that way: not pushed, so it stops (else the shot would never end)
            var p = b.transform.position;
            var air = lv.Sheet(p).WindAt(p).normalized;
            b.windBlocked = air != Vector3.zero && Cast(lv, p - air * 0.05f, air, b).distance < 0.08f;
            if (!b.gameObject.activeSelf || b.w == Vector3.zero) continue;
            b.transform.rotation = Quaternion.AngleAxis(b.w.magnitude * Mathf.Rad2Deg * time, b.w.normalized) * b.transform.rotation;
        }
        ShowMoving(lv, time);
    }

    public const float TimedPeriod = 2f, PlatformSpeed = 1.5f, SawSpeed = 1.5f; // timed walls: seconds up, then as long down; platforms, saws: tiles/s

    // gates, timed walls and platforms at lv.time (the host's clock on multiplayer clients)
    public static void UpdateMoving(LevelBuilder.Level lv)
    {
        bool pressed = false;
        foreach (var b in lv.balls)
            pressed |= b.gameObject.activeSelf && !b.falling && !b.airborne && Has(lv.Sheet(b.transform.position).ModulesAt(b.transform.position), 'K', out _);
        float time = lv.main.time;
        foreach (var sheet in lv.main.sheets) // a button opens the gates of every floor
        {
            foreach (var m in sheet.movers)
            {
                bool up = m.timed ? TimedUp(time, m.period, m.inverted) : !pressed;
                m.rose = up && !m.up;
                m.up = up;
            }
            LevelBuilder.Restack(sheet); // piles ride the walls they stand on
            foreach (var m in sheet.movers)
                m.wall.height = m.up ? m.floor + LevelBuilder.BlockHeight : float.NegativeInfinity;
            foreach (var pl in sheet.platforms)
            {
                int n = pl.path.Count, segs = pl.loop ? n : n - 1;
                if (segs == 0) continue;
                float s = time * pl.speed, u = pl.loop ? s % segs : Mathf.PingPong(s, segs);
                int i = Mathf.Min((int)u, segs - 1), sign = pl.loop || s % (2 * segs) < segs ? 1 : -1;
                Vector3 a = pl.path[i], b = pl.path[(i + 1) % n];
                pl.center = Vector3.Lerp(a, b, u - i);
                pl.velocity = (b - a) * (pl.speed * sign);
            }
        }
    }

    /// <summary>Timed walls and spikes: up for `period` seconds, then down as long; inverted = the opposite.</summary>
    public static bool TimedUp(float time, float period, bool inverted) => (time % (2 * period) < period) != inverted;

    // moves the visuals of the moving parts (walls slide instead of popping)
    public static void ShowMoving(LevelBuilder.Level main, float dt)
    {
        foreach (var lv in main.main.sheets) ShowSheet(lv, dt);
    }

    static void ShowSheet(LevelBuilder.Level lv, float dt)
    {
        foreach (var m in lv.movers)
        {
            // here and not in UpdateMoving, which the aim guide's prediction runs too
            // shortcut: a button only clicks if it moves a gate; track the pressed state if levels get buttons without gates
            if (m.up != m.shownUp)
            {
                m.shownUp = m.up;
                Sfx.Play(Sfx.Kind.Gate, m.timed ? 0.4f : 0.8f);
                if (!m.timed) Sfx.Play(Sfx.Kind.Button, 0.8f);
            }
            var p = m.visual.position;
            float target = m.floor + (m.up ? 0 : 0.03f - LevelBuilder.BlockHeight); // visual pivot on the felt
            m.visual.position = new Vector3(p.x, Mathf.MoveTowards(p.y, target, 6 * dt), p.z);
        }
        foreach (var ts in lv.timedSpikes)
        {
            if (!ts || !ts.tips) continue;
            bool up = TimedUp(lv.main.time, ts.period, ts.inverted);
            if (up != ts.shownUp) { ts.shownUp = up; Sfx.Play(Sfx.Kind.Gate, 0.25f); }
            var s = ts.tips.localScale;
            ts.tips.localScale = new Vector3(s.x, Mathf.MoveTowards(s.y, up ? 1 : LevelBuilder.SpikesDown, 8 * dt), s.z);
        }
        foreach (var pl in lv.platforms)
        {
            pl.visual.position = pl.center;
            if (pl.saw && pl.velocity != Vector3.zero) pl.visual.rotation = Quaternion.LookRotation(pl.velocity); // the blade faces its way
        }
    }

    static bool Resting(Ball b) => !b.IsMoving && !b.falling && !b.airborne;

    static void Potted(Ball b, Vector3 p)
    {
        Sfx.Play(Sfx.Kind.Pocket, 0.5f + b.v.magnitude / 10);
        Juice.Pot(p, b.number);
    }

    // predict: resting balls are skipped until something hits them (the aim guide re-runs this 20 times a second)
    // shortcut: in the guide, magnets / belts / platforms don't move a ball at rest; simulate them too if the guide misleads there
    static void Step(LevelBuilder.Level lv, float dt, bool predict = false)
    {
        lv.time += dt;
        UpdateMoving(lv);
        var balls = lv.balls;
        foreach (var sheet in lv.sheets)
            foreach (var m in sheet.movers)
                if (m.rose) Lift(sheet, m);
        var top = lv; // the whole level; inside the loops lv = the floor the ball is on
        foreach (var b in balls)
        {
            if (!b.gameObject.activeSelf || predict && Resting(b)) continue;
            var p = b.transform.position;
            lv = top.Sheet(p);
            b.onBelt = false;
            if (!b.falling && !b.airborne)
            {
                var ground = lv.GroundAt(p);
                float launch = RampLaunch(lv, b, p);
                if (ground == null)
                {
                    b.pocket = lv.PocketAt(p);
                    // off an edge it flies for a moment, so a fast ball bridges a gap of a few hundredths,
                    // e.g. a platform not quite at the end of its run
                    if (b.pocket == null) b.airborne = true;
                    else b.falling = true;
                    if (!predict && b.pocket != null) Potted(b, p);
                    b.v.y = launch;
                }
                else if (ground.Value < p.y - R - 0.05f || launch > 0) // rolled off a step down / off a ramp's top fast: flies
                {
                    b.airborne = true;
                    b.v.y = launch;
                }
                else
                {
                    p.y = ground.Value + R;
                    Modules(lv, b, ref p, dt, predict);
                }
            }

            if (b.falling || b.airborne) b.v.y -= G * dt;
            else Cloth(b, dt, Has(lv.ModulesAt(p), 'I', out _) ? 'I' : Has(lv.ModulesAt(p), 'M', out _) ? 'M' : '.');
            p += b.v * dt;

            if (b.airborne && b.v.y < 0)
            {
                lv = top.Sheet(p); // coming down, it lands on the first floor under it
                var ground = lv.GroundAt(p);
                if (ground != null && p.y - R <= ground.Value)
                {
                    b.airborne = false;
                    p.y = ground.Value + R; // shortcut: dead landing, no bounce; add one if jumps feel flat
                    if (!predict) Sfx.Play(Sfx.Kind.Land, -b.v.y / 8);
                    b.v.y = 0;
                }
                else if (ground == null && p.y <= R - 0.03f) // just below the floor-0 felt (~0.03 s off an edge): a 1-tile gap needs > 30 u/s
                {
                    b.airborne = false;
                    b.falling = true;
                    b.pocket = lv.PocketAt(p);
                    if (!predict && b.pocket != null) Potted(b, p);
                }
            }

            if (b.pocket != null) // keep it inside the round hole while it drops
            {
                var off = new Vector2(p.x, p.z) - b.pocket.center;
                float max = Mathf.Max(b.pocket.radius - R, 0);
                if (off.magnitude > max)
                {
                    off = off.normalized * max;
                    p.x = b.pocket.center.x + off.x;
                    p.z = b.pocket.center.y + off.y;
                    b.v.x *= 0.5f;
                    b.v.z *= 0.5f;
                }
            }
            b.transform.position = p;
        }

        for (int i = 0; i < balls.Count; i++)
        {
            var a = balls[i];
            if (!a.gameObject.activeSelf || a.falling) continue;
            // shortcut: flying balls pass over other balls (2D contacts only); do 3D contacts if jumps start hitting balls
            if (!a.airborne)
                for (int j = i + 1; j < balls.Count; j++)
                {
                    var c = balls[j];
                    if (c.gameObject.activeSelf && !c.falling && !c.airborne && Rules.Collide(a.number, a.owner, c.number, c.owner)
                        && Mathf.Abs(a.transform.position.y - c.transform.position.y) < R) // different floors: no contact
                    {
                        float hit = BallBall(a, c);
                        if (!predict) { Sfx.Play(Sfx.Kind.Ball, hit / 15); Juice.Hit(hit); }
                    }
                }
            if (predict && Resting(a)) continue;
            // shortcut: walls are vertical up to their height, no tops (but gates and timed walls, GroundAt);
            // a ball coming down on a rail or block slides off it
            var ap = a.transform.position;
            lv = top.Sheet(ap);
            float bottom = ap.y - R;
            bool rising = a.airborne && a.v.y > 0;
            foreach (var wall in lv.WallsNear(ap))
                if (bottom < wall.height - 1e-4f && ap.y + R > wall.bottom && !(rising && wall.bounds.Contains(new Vector2(ap.x, ap.z)))) // lifted by a rising wall: goes up through it
                {
                    float hit = Cushion(a, wall);
                    if (!predict && hit > 0)
                    {
                        Sfx.Play(wall.kick > 0 ? Sfx.Kind.Bumper : Sfx.Kind.Cushion, hit / 15 + (wall.kick > 0 ? 0.4f : 0));
                        Juice.Hit(hit);
                        if (wall.kick > 0) Juice.Punch(lv, LevelBuilder.Level.CellOf(new Vector3(wall.bounds.center.x, 0, wall.bounds.center.y)), new Vector3(1.1f, 1, 1.1f));
                    }
                }
        }
    }

    public const float BoostSpeed = 12f;  // booster: minimum speed it sends the ball off with
    public const float JumpRatio = 0.6f;  // fan: vertical speed per unit of horizontal speed (~31 deg)
    public const float BumperKick = 6f;   // bumper: speed added on every hit
    public const float BeltSpeed = 2.5f;  // conveyor: how fast it carries a ball
    public const float WindPush = 20f;                       // wind fan: acceleration 1 tile ahead (units/s²)
    public const int WindReach = 3;                         // wind fan: tiles of air ahead
    // a gate / timed wall rising under a ball throws it up a bit higher than its top: it lands on it
    static readonly float LiftSpeed = Mathf.Sqrt(2 * G * (LevelBuilder.BlockHeight + 0.25f));
    public const float MagnetRadius = 2.5f, MagnetPull = 20f, MagnetMinSpeed = 0.3f; // magnet: reach, pull, speed below which it lets go
    // ramp: rolling ball (5/7 of g) on a slope of FloorStep per tile; horizontal part of the acceleration
    static readonly float RampAccel = G * 5 / 7 * LevelBuilder.FloorStep / (1 + LevelBuilder.FloorStep * LevelBuilder.FloorStep);

    public const float RampJumpSpeed = 3f; // ramp: uphill speed above which the ball leaves the top flying instead of staying on the ground

    static void Lift(LevelBuilder.Level lv, LevelBuilder.Mover m)
    {
        foreach (var b in lv.balls)
        {
            var p = b.transform.position;
            if (!b.gameObject.activeSelf || b.falling || b.airborne || LevelBuilder.Level.CellOf(p) != m.cell || p.y - R < m.floor - 0.1f || lv.Sheet(p) != lv) continue; // on it or on its pile
            b.airborne = true;
            b.v.y = LiftSpeed;
        }
    }

    // a ball leaving a ramp through its top edge keeps the vertical speed of the climb (slope FloorStep per tile);
    // 0 = not leaving a ramp's top, too slow, or the next cell is the same ramp going on up
    static float RampLaunch(LevelBuilder.Level lv, Ball b, Vector3 p)
    {
        if (b.cell is not Vector2Int from) return 0;
        var c = LevelBuilder.Level.CellOf(p);
        if (c == from) return 0;
        var was = lv.Sheet(new Vector3(from.x, p.y, from.y)); // the floor of the cell it left (a ramp's top is one floor up)
        if (was.TileAt(new Vector3(from.x, 0, from.y)) != LevelBuilder.Ramp) return 0;
        float angle = was.AngleAt(from);
        var d = LevelBuilder.AngleDir(angle);
        float along = Vector3.Dot(new Vector3(b.v.x, 0, b.v.z), d);
        if (along < RampJumpSpeed || Vector2.Dot(c - from, new Vector2(d.x, d.z)) < 0.5f) return 0;
        if (lv.TileAt(p) == LevelBuilder.Ramp && Mathf.Approximately(lv.AngleAt(c), angle)) return 0;
        return along * LevelBuilder.FloorStep;
    }

    // whether the modules a ball feels (Level.ModulesAt) include c, and that one's angle
    static bool Has(List<(char c, float angle)> felt, char c, out float angle)
    {
        foreach (var f in felt)
            if (f.c == c) { angle = f.angle; return true; }
        angle = 0;
        return false;
    }

    // tile effects; boosters, fans, portals and manholes fire once when the ball enters the tile, the others while it is on it
    static void Modules(LevelBuilder.Level lv, Ball b, ref Vector3 p, float dt, bool predict)
    {
        foreach (var saw in lv.platforms)
            if (saw.saw && SawHits(saw, p))
            {
                Kill(lv, b, ref p, LevelBuilder.Level.CellOf(saw.center), predict);
                return;
            }
        var felt = lv.ModulesAt(p); // a module on a bridge's deck or up a pile: nothing for a ball below it; on a platform: its riders
        var c = LevelBuilder.Level.CellOf(p);
        var plat = lv.PlatformAt(p);
        if (Has(felt, 'X', out _) || SpikesUp(lv, felt, c))
        {
            Kill(lv, b, ref p, plat != null ? LevelBuilder.Level.CellOf(plat.path[0]) : c, predict);
            return;
        }
        if (plat != null)
        {
            p += plat.velocity * dt; // rides along
            b.cell = LevelBuilder.Level.CellOf(p);
            return;
        }
        if (!b.windBlocked) b.v += lv.WindAt(p) * dt;
        if (lv.TileAt(p) == LevelBuilder.Ramp) b.v -= LevelBuilder.AngleDir(lv.AngleAt(c)) * (RampAccel * dt); // uphill = its angle
        if (Has(felt, 'N', out float beltAngle))
        {
            var belt = lv.ModuleAt<Belt>(c);
            b.beltSpeed = belt ? belt.speed : BeltSpeed;
            p += LevelBuilder.AngleDir(beltAngle) * (b.beltSpeed * dt);
            b.onBelt = true;
        }
        foreach (var m in lv.magnets)
        {
            var d = new Vector3(m.cell.x - p.x, 0, m.cell.y - p.z);
            float dist = d.magnitude, speed = b.v.magnitude;
            if (dist > m.radius || dist < 1e-4f || speed < MagnetMinSpeed) continue;
            if (dist < 0.12f && speed < 1.5f) // close and slow: it snaps on and stays
            {
                p = new Vector3(m.cell.x, p.y, m.cell.y);
                b.v = b.w = Vector3.zero;
                if (!predict) { Sfx.Play(Sfx.Kind.Magnet, 0.8f); Juice.Punch(lv, m.cell, new Vector3(1.15f, 0.85f, 1.15f)); } // once: snapped on, it sits at dist 0 and is skipped above
                continue;
            }
            // pull proportional to the ball's speed; below MagnetMinSpeed it stops pulling so a slow ball settles and the turn ends
            b.v += d / dist * (m.pull * (1 - dist / m.radius) * (speed / 2f) * dt);
            if (dist < 0.6f) b.v *= 1 - 4 * dt;                              // damped near it, so the ball settles
        }

        bool entered = b.cell.HasValue && b.cell.Value != c;
        b.cell = c;
        var h = new Vector3(b.v.x, 0, b.v.z);
        var boost = Vector3.zero;
        foreach (var f in felt)
            if (LevelBuilder.BoostChars.IndexOf(f.c) >= 0) boost = LevelBuilder.AngleDir(f.angle);
        if (entered) b.boostArmed = boost != Vector3.zero;
        // booster: armed on entering, fires once the ball reaches the center (closest point of its path to it)
        if (b.boostArmed && Vector3.Dot(new Vector3(p.x - c.x, 0, p.z - c.y), h) >= 0)
        {
            b.boostArmed = false;
            var booster = lv.ModuleAt<BoosterModule>(c);
            b.v = boost * Mathf.Max(h.magnitude, booster ? booster.speed : BoostSpeed);
            b.w = new Vector3(b.v.z / R, 0, -b.v.x / R); // spin cancelled: plain rolling along the push
            if (!predict) { Sfx.Play(Sfx.Kind.Boost, 0.5f + b.v.magnitude / 30); Juice.Punch(lv, c, new Vector3(1.15f, 1, 1.15f)); }
            return;
        }
        if (!entered || boost != Vector3.zero) return;

        bool blue = Has(felt, LevelBuilder.PortalBlue, out _), orange = Has(felt, LevelBuilder.PortalOrange, out _);
        if (Has(felt, 'J', out _) && h.magnitude > 0.5f)
        {
            var fan = lv.ModuleAt<Fan>(c);
            b.v.y = (fan ? fan.jumpRatio : JumpRatio) * h.magnitude;
            b.airborne = true;
            if (!predict) { Sfx.Play(Sfx.Kind.Fan, 0.5f + h.magnitude / 15); Juice.Punch(lv, c, new Vector3(1.1f, 1.25f, 1.1f)); }
        }
        else if (Has(felt, 'W', out _))
        {
            if (!predict) { Sfx.Play(Sfx.Kind.Manhole, 0.4f + h.magnitude / 10); Juice.Punch(lv, c, new Vector3(1.1f, 0.6f, 1.1f)); }
            p = new Vector3(c.x, p.y, c.y); // caught in the manhole
            b.v = b.w = Vector3.zero;
        }
        else if (blue || orange)
        {
            LevelBuilder.Level dst = null; // the other portal may be on another floor
            foreach (var s in lv.main.sheets)
                if ((blue ? s.portalOrange : s.portalBlue).HasValue) dst = s;
            var to = dst == null ? null : blue ? dst.portalOrange : dst.portalBlue;
            if (to.HasValue) p = Teleport(lv, b, p, c, to.Value, dst);
            if (to.HasValue && !predict)
            {
                Sfx.Play(Sfx.Kind.Portal, 0.8f);
                Juice.Punch(lv, c, Vector3.one * 1.15f);
                Juice.Punch(dst, to.Value, Vector3.one * 1.15f);
            }
        }
    }

    // timed spikes on the ball's cell that are up now
    static bool SpikesUp(LevelBuilder.Level lv, List<(char c, float angle)> felt, Vector2Int c)
    {
        bool purple = Has(felt, LevelBuilder.TimedSpikes, out _), green = Has(felt, LevelBuilder.TimedSpikesInv, out _);
        if (!purple && !green) return false;
        var set = lv.ModuleAt<TimedSpikesModule>(c);
        float period = set ? set.period : TimedPeriod;
        return purple && TimedUp(lv.main.time, period, false) || green && TimedUp(lv.main.time, period, true);
    }

    // dies (spikes, saw): drops below the counting line, scored as off the table
    static void Kill(LevelBuilder.Level lv, Ball b, ref Vector3 p, Vector2Int punch, bool predict)
    {
        b.falling = true;
        if (!predict)
        {
            Sfx.Play(Sfx.Kind.Spikes, 1);
            Juice.Shatter(p, b.v, p.y - R, b.number);
            Juice.Punch(lv, punch, new Vector3(1.1f, 1.3f, 1.1f));
        }
        b.v = b.w = Vector3.zero;
        p.y = -1;
    }

    // a ball on the saw's floor touching its blade: a thin disc, SawRadius each way along its way
    static bool SawHits(LevelBuilder.Platform saw, Vector3 p)
    {
        if (Mathf.Abs(p.y - R - saw.center.y) > 0.3f) return false;
        var d = saw.velocity != Vector3.zero ? saw.velocity.normalized : saw.visual.forward;
        var off = p - saw.center;
        off.y = 0;
        float along = Mathf.Clamp(Vector3.Dot(off, d), -LevelBuilder.SawRadius, LevelBuilder.SawRadius);
        return (off - d * along).sqrMagnitude < (R + 0.03f) * (R + 0.03f);
    }

    // Through a portal: position, velocity and spin are turned by the angle between the two portals, so a ball that
    // enters off-center and at a slant leaves the other one off-center and at the same slant relative to its facing.
    // lv = the floor of the portal on `from`, dst = the floor of the one on `to` (null = the same)
    public static Vector3 Teleport(LevelBuilder.Level lv, Ball b, Vector3 p, Vector2Int from, Vector2Int to, LevelBuilder.Level dst = null)
    {
        dst ??= lv;
        bool blue = lv.portalBlue == from;
        LevelBuilder.Layer lFrom = lv.LayerOf(from, blue ? LevelBuilder.PortalBlue : LevelBuilder.PortalOrange),
            lTo = dst.LayerOf(to, blue ? LevelBuilder.PortalOrange : LevelBuilder.PortalBlue); // portals on a pile
        var turn = Quaternion.Euler(0, (lTo?.angle ?? dst.AngleAt(to)) - (lFrom?.angle ?? lv.AngleAt(from)), 0);
        var off = turn * new Vector3(p.x - from.x, 0, p.z - from.y);
        var q = new Vector3(to.x + Mathf.Clamp(off.x, -0.45f, 0.45f), 100, to.y + Mathf.Clamp(off.z, -0.45f, 0.45f)); // from above: a portal on a deck sends onto it
        q.y = (lTo?.y ?? dst.GroundAt(q) ?? dst.BaseY(to)) + R;
        b.v = turn * b.v;
        b.w = turn * b.w;
        b.cell = to; // arriving doesn't count as entering: no ping-pong between the portals
        return q;
    }

    static void Cloth(Ball b, float dt, char tile)
    {
        var v = b.v;
        var w = b.w;
        // ice: no grip (spin and speed stay, barely slows); sand: heavy drag
        float roll = tile == 'I' ? MuRoll * 0.3f : tile == 'M' ? MuRoll * 25 : MuRoll;
        if (tile == 'I')
        {
            float s0 = v.magnitude, ns0 = Mathf.Max(0, s0 - roll * G * dt);
            if (ns0 < 0.01f) { b.v = b.w = Vector3.zero; return; }
            b.v = v * (ns0 / s0);
            b.w.y = Mathf.MoveTowards(w.y, 0, SpinDecel * dt);
            return;
        }
        var slip = new Vector2(v.x + R * w.z, v.z - R * w.x); // contact point velocity on the cloth
        if (slip.sqrMagnitude > 1e-8f)
        {
            // friction changes the slip 3.5x faster than the velocity (solid sphere), so cap at exact rolling
            float k = Mathf.Min(MuSlide * G * dt, slip.magnitude / 3.5f);
            var f = -slip.normalized * k;
            v.x += f.x; v.z += f.y;
            w.x -= 2.5f / R * f.y;
            w.z += 2.5f / R * f.x;
        }
        else
        {
            float s = v.magnitude, ns = Mathf.Max(0, s - roll * G * dt);
            if (ns < 0.01f) { b.v = b.w = Vector3.zero; return; }
            v *= ns / s;
            w.x = v.z / R;
            w.z = -v.x / R;
        }
        w.y = Mathf.MoveTowards(w.y, 0, SpinDecel * dt);
        b.v = v;
        b.w = w;
    }

    // returns the impact speed (0 = no impact), for the sound
    static float BallBall(Ball a, Ball c)
    {
        var d = c.transform.position - a.transform.position;
        d.y = 0;
        float dist = d.magnitude;
        if (dist >= 2 * R || dist < 1e-6f) return 0;
        var n = d / dist;
        var push = n * (2 * R - dist) / 2;
        a.transform.position -= push;
        c.transform.position += push;

        float approach = Vector3.Dot(a.v - c.v, n);
        if (approach <= 0) return 0;
        float jn = (1 + EBall) / 2 * approach;
        var j = n * jn; // equal masses; spin is kept, so follow/draw happen naturally
        a.v -= j;
        c.v += j;

        // throw: friction at the contact (from a cut angle or english) pushes the object ball off the line of centers
        var rel = a.v + Vector3.Cross(a.w, n * R) - c.v - Vector3.Cross(c.w, -n * R); // contact slip, a relative to c
        var t = rel - Vector3.Dot(rel, n) * n;
        t.y = 0;
        float slip = t.magnitude;
        if (slip > 1e-6f)
        {
            // contact slip changes 7x the impulse (2 balls x (1 + 5/2)), so cap where the surfaces stop sliding
            var jt = t / slip * Mathf.Min(MuBall * jn, slip / 7);
            var dw = Vector3.Cross(n * R, -jt) * (2.5f / (R * R)); // same spin change on both (impulse -jt at +nR, +jt at -nR)
            a.v -= jt;
            c.v += jt;
            a.w += dw;
            c.w += dw;
        }
        a.onCueHit?.Invoke(c);
        c.onCueHit?.Invoke(a);
        return approach;
    }

    // returns the impact speed (0 = no impact), for the sound
    static float Cushion(Ball b, LevelBuilder.Wall wall)
    {
        var p = b.transform.position;
        var p2 = new Vector2(p.x, p.z);
        var bb = wall.bounds;
        if (p2.x < bb.xMin - R || p2.x > bb.xMax + R || p2.y < bb.yMin - R || p2.y > bb.yMax + R) return 0;
        var q = Poly.Closest(wall.pts, p2, out bool inside);
        var d = p2 - q;
        if (d.sqrMagnitude < 1e-12f || (!inside && d.sqrMagnitude >= R * R)) return 0;
        var n2 = inside ? -d.normalized : d.normalized; // inside: leave through the nearest edge
        var n = new Vector3(n2.x, 0, n2.y);
        b.transform.position = new Vector3(q.x, p.y, q.y) + n * R;

        float vn = Vector3.Dot(b.v, n);
        if (vn >= 0) return 0;
        b.v -= (1 + ECushion) * vn * n;
        b.v += n * wall.kick;

        // cushion friction on the contact point: side spin (english) bends the rebound
        var surface = new Vector3(-R * b.w.y * n.z, 0, R * b.w.y * n.x);
        var tangentV = b.v - Vector3.Dot(b.v, n) * n;
        var slip = tangentV + surface;
        if (slip.sqrMagnitude > 1e-8f)
        {
            float jt = Mathf.Min(MuCushion * (1 + ECushion) * -vn, slip.magnitude / 3.5f);
            var dv = -slip.normalized * jt;
            b.v += dv;
            b.w.y += 2.5f / R * (n.x * dv.z - n.z * dv.x);
        }

        // cushion contact above the center kills the roll into the rail: the ball leaves sliding and slows
        var railAxis = new Vector3(n.z, 0, -n.x);
        b.w -= Vector3.Dot(b.w, railAxis) * railAxis;
        return -vn;
    }
}
