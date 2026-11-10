using System.Collections.Generic;
using UnityEngine;

// Wind fan (U) settings + spinning blades and streaks of air flying out over the tiles it blows on (graphics only).
// push / reach are read by LevelBuilder.Module when the level is built; range and drop are set there.
public class WindModule : MonoBehaviour
{
    public float push = PoolPhysics.WindPush; // acceleration on the tile right ahead (units/s²); then (reach + 1 - k) / reach of it k tiles away
    public int reach = PoolPhysics.WindReach; // tiles of air ahead (stops earlier at a rail or a block)
    public float spin = 720;                  // blades, deg/s
    public int streaks = 8;
    public float flow = 4;                    // air speed, units/s
    public Transform blades;
    public Material airMat;

    [System.NonSerialized] public int range = -1; // tiles it really blows on (LevelBuilder.Module); -1 = not built in a level: reach
    [System.NonSerialized] public float drop;     // how far below the fan the felt it blows over is (a fan on a rail)

    readonly List<Transform> air = new();
    readonly List<Vector2> spot = new(); // x across, y height, in the fan's frame
    readonly List<float> phase = new();

    void Start() // play mode only: editor previews have no air frozen in mid-flight
    {
        if (!airMat) airMat = LevelBuilder.AirMaterial; // a prefab made before the streaks existed
        if (range < 0) range = reach;
        if (range == 0) return;
        for (int i = 0; i < streaks; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>()); // physics is PoolPhysics, never PhysX
            s.GetComponent<Renderer>().sharedMaterial = airMat;
            s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.transform.SetParent(transform, false);
            air.Add(s.transform);
            spot.Add(new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.08f, 0.6f)));
            phase.Add((float)i / streaks);
        }
        Update();
    }

    void Update()
    {
        if (blades) blades.Rotate(0, 0, spin * Time.deltaTime, Space.Self);
        for (int i = 0; i < air.Count; i++)
        {
            float k = (Time.time * flow / range + phase[i]) % 1; // 0 = leaving the fan, 1 = end of its air
            var t = air[i];
            t.localPosition = new Vector3(spot[i].x, spot[i].y - drop * k, 0.5f + k * range);
            float w = 0.04f * (1 - k * 0.7f); // thins out as it slows down
            t.localScale = new Vector3(w, w, 0.35f * (1 - k * 0.5f));
        }
    }
}
