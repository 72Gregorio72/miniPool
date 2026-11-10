using UnityEngine;

// Saw (@) settings + spinning blade (graphics only). speed is read by LevelBuilder.Module when the level is built.
public class SawModule : MonoBehaviour
{
    public float speed = PoolPhysics.SawSpeed; // tiles/s along its track (read when built)
    public float spin = 1080;                  // blade, deg/s
    public Transform blade;

    void Update()
    {
        if (blade) blade.Rotate(spin * Time.deltaTime, 0, 0, Space.Self);
    }
}
