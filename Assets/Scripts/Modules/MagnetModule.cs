using UnityEngine;

// Magnet (Z) settings. Read by PoolPhysics / LevelBuilder.Module; default = PoolPhysics constant.
public class MagnetModule : MonoBehaviour
{
    public float radius = PoolPhysics.MagnetRadius, pull = PoolPhysics.MagnetPull; // reach (units), pull next to it (read when built)
}
