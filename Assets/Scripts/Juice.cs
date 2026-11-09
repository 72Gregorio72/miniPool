using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Feedback only, no gameplay: cubes when a ball is potted, the ball shattering on spikes (PhysX cubes),
// module punches, camera shake on hard hits, slow motion while the black rolls into a pocket.
// Nothing here runs in edit mode (EditMode tests and the level editor run the physics too).
public static class Juice
{
    public const float ShakeFrom = 6;    // impact speed (units/s) where shake starts
    public const float SlowRadius = 1.2f; // black this close to a pocket and heading in = slow motion
    public const float SlowScale = 0.35f;
    const float ShardLife = 2.5f;         // seconds before the spike shards shrink away

    static float shake; // current amplitude, decays in OrbitCamera
    static Runner runner;

    static Runner Run => runner ? runner : runner = new GameObject("Juice") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Runner>();

    public static void Hit(float speed) => shake = Mathf.Max(shake, Mathf.Clamp01((speed - ShakeFrom) / 15) * 0.12f);

    // called by OrbitCamera after it places the camera
    public static Vector3 ShakeOffset(float dt)
    {
        if (shake < 0.001f) return Vector3.zero;
        shake = Mathf.MoveTowards(shake, 0, dt * 0.6f);
        return Random.insideUnitSphere * shake;
    }

    // black on the table and about to drop in: 0..1 as it gets closer (0 = no slow motion)
    public static float Approach(LevelBuilder.Level lv, Ball b)
    {
        if (!b.gameObject.activeSelf || b.falling || b.airborne) return 0;
        var p = new Vector2(b.transform.position.x, b.transform.position.z);
        var v = new Vector2(b.v.x, b.v.z);
        if (v.sqrMagnitude < 0.01f) return 0;
        float best = 0;
        foreach (var pk in lv.pockets)
        {
            var to = pk.center - p;
            float d = to.magnitude;
            // heading inside the hole: the ray passes within the radius
            if (d > SlowRadius || Vector2.Dot(to, v) <= 0 || Mathf.Abs(to.x * v.y - to.y * v.x) / v.magnitude > pk.radius) continue;
            best = Mathf.Max(best, 1 - d / SlowRadius);
        }
        return best;
    }

    // the module on that cell grows by `scale` and springs back (bumper pushing out, manhole squashing, ...)
    public static void Punch(LevelBuilder.Level lv, Vector2Int cell, Vector3 scale)
    {
        if (!Application.isPlaying || !lv.modules.TryGetValue(cell, out var go) || !go) return;
        if (!go.TryGetComponent(out Puncher p)) { p = go.AddComponent<Puncher>(); p.rest = go.transform.localScale; }
        p.peak = scale;
        p.t = 0;
    }

    public static void Pot(Vector3 at, Color color)
    {
        if (Application.isPlaying) Run.StartCoroutine(Burst(at, color));
    }

    static IEnumerator Burst(Vector3 at, Color color)
    {
        var bits = Enumerable.Range(0, 24).Select(_ =>
        {
            var c = Cube(at, 0.08f, color);
            var dir = Random.insideUnitCircle;
            return (t: c.transform, v: new Vector3(dir.x * 2.5f, Random.Range(3f, 6f), dir.y * 2.5f), spin: Random.insideUnitSphere * 720);
        }).ToArray();

        const float life = 1.2f;
        for (float t = 0; t < life; t += Time.unscaledDeltaTime)
        {
            float dt = Time.unscaledDeltaTime, k = 1 - t / life;
            for (int i = 0; i < bits.Length; i++)
            {
                bits[i].v.y -= 12 * dt;
                bits[i].t.position += bits[i].v * dt;
                bits[i].t.Rotate(bits[i].spin * dt);
                bits[i].t.localScale = Vector3.one * 0.08f * k;
            }
            yield return null;
        }
        foreach (var b in bits) Object.Destroy(b.t.gameObject);
    }

    // the ball breaks into PhysX cubes that bounce on the floor and on each other
    public static void Shatter(Vector3 at, Vector3 velocity, float floorY, Color color)
    {
        if (Application.isPlaying) Run.StartCoroutine(Shards(at, velocity, floorY, color));
    }

    static IEnumerator Shards(Vector3 at, Vector3 velocity, float floorY, Color color)
    {
        // shortcut: the level has no colliders, so the shards land on an invisible 3x3-tile slab at the spike's height
        // (they fall through rails and over voids); build real table colliders if shards should bounce off rails
        Physics.gravity = new Vector3(0, -PoolPhysics.G, 0); // game scale (1 unit = 1/7 m); nothing else uses PhysX
        var floor = new GameObject("ShardFloor").AddComponent<BoxCollider>();
        floor.size = new Vector3(3, 1, 3);
        floor.transform.position = new Vector3(Mathf.Round(at.x), floorY - 0.5f, Mathf.Round(at.z));

        const int n = 5;
        float size = Ball.Radius * 2 / n;
        var shards = new List<Rigidbody>();
        var flat = new Vector3(velocity.x, 0, velocity.z);
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        {
            var off = (new Vector3(x, y, z) - Vector3.one * (n - 1) / 2f) * size;
            if (off.magnitude > Ball.Radius) continue; // a sphere of cubes
            var rb = Cube(at + off, size * 0.9f, color).AddComponent<Rigidbody>(); // CreatePrimitive's BoxCollider stays
            rb.mass = 0.02f;
            rb.linearVelocity = flat * 0.3f + off.normalized * Random.Range(1.5f, 3f) + Vector3.up * Random.Range(1.5f, 3.5f);
            rb.angularVelocity = Random.insideUnitSphere * 15;
            shards.Add(rb);
        }

        yield return new WaitForSeconds(ShardLife);
        for (float t = 0; t < 0.5f; t += Time.deltaTime)
        {
            foreach (var s in shards) if (s) s.transform.localScale = Vector3.one * size * 0.9f * (1 - t / 0.5f);
            yield return null;
        }
        foreach (var s in shards) if (s) Object.Destroy(s.gameObject);
        Object.Destroy(floor.gameObject);
    }

    static GameObject Cube(Vector3 at, float size, Color color)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.GetComponent<Renderer>().sharedMaterial = LevelBuilder.Mat(color);
        c.transform.SetPositionAndRotation(at, Random.rotation);
        c.transform.localScale = Vector3.one * size;
        return c;
    }

    class Runner : MonoBehaviour { }

    class Puncher : MonoBehaviour
    {
        public Vector3 rest, peak = Vector3.one;
        public float t = 1;
        const float Duration = 0.25f;

        void Update()
        {
            if (t >= 1) return;
            t = Mathf.Min(t + Time.deltaTime / Duration, 1);
            // quick out, springy back: peaks right away, overshoots a little under the rest size, settles
            float k = Mathf.Sin(t * Mathf.PI * 2) * (1 - t) * 0.3f + (1 - t) * (1 - t);
            transform.localScale = Vector3.Scale(rest, Vector3.LerpUnclamped(Vector3.one, peak, k));
        }
    }
}
