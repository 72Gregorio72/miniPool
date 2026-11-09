using UnityEngine;

// Bumper (Q) settings. Read by PoolPhysics / LevelBuilder.Module; default = PoolPhysics constant.
public class BumperModule : MonoBehaviour
{
    public float kick = PoolPhysics.BumperKick; // speed added on every hit (read when the level is built)
}
