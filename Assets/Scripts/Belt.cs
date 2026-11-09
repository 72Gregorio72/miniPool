using System.Collections.Generic;
using UnityEngine;

// Conveyor belt ('N') prefab script: chevrons sliding along it; speed is read by PoolPhysics (the carry).
public class Belt : MonoBehaviour
{
    public float speed = PoolPhysics.BeltSpeed; // how fast it carries a ball (units/s)
    public List<Transform> chevrons;

    void Update()
    {
        // chevrons travel forward (the tile's angle) and wrap around
        for (int i = 0; i < chevrons.Count; i++)
        {
            float z = Mathf.Repeat(Time.time * speed + (float)i / chevrons.Count, 1) - 0.5f;
            chevrons[i].position = transform.position + transform.forward * z * 0.8f + Vector3.up * 0.03f;
            chevrons[i].rotation = transform.rotation;
        }
    }
}
