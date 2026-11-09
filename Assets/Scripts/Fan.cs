using System.Collections.Generic;
using UnityEngine;

// Fan ('J') prefab script: spinning blades and streaks of air rising out of it; jumpRatio is read by PoolPhysics (the jump).
public class Fan : MonoBehaviour
{
    public float jumpRatio = PoolPhysics.JumpRatio; // vertical speed per unit of horizontal speed
    public int streaks = 6;
    public float spin = 900, top = 1.6f, rise = 2.2f; // blades deg/s; air: height reached, speed (units/s)
    public Transform blades;
    public Material airMat;

    readonly List<Transform> air = new();
    readonly List<float> phase = new();

    void Start() // play mode only: editor previews have no air frozen in mid-flight
    {
        for (int i = 0; i < streaks; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>()); // physics is PoolPhysics, never PhysX
            s.GetComponent<Renderer>().sharedMaterial = airMat;
            s.transform.SetParent(transform, false);
            var p = Random.insideUnitCircle * 0.28f;
            s.transform.localPosition = new Vector3(p.x, 0, p.y);
            air.Add(s.transform);
            phase.Add((float)i / streaks);
        }
        Update();
    }

    void Update()
    {
        if (blades) blades.Rotate(0, spin * Time.deltaTime, 0, Space.Self);
        for (int i = 0; i < air.Count; i++)
        {
            float k = (Time.time * rise / top + phase[i]) % 1; // 0 = leaving the fan, 1 = top
            var t = air[i];
            var p = t.localPosition;
            t.localPosition = new Vector3(p.x, 0.1f + k * top, p.z);
            float w = 0.06f * (1 - k); // thins out as it rises
            t.localScale = new Vector3(w, 0.3f * (1 - k * 0.5f), w);
        }
    }
}
