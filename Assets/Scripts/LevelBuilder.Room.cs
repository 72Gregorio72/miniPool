using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The room around the table, one look per world (Worlds.Theme.hall), sized on the level: walls RoomMargin tiles from it.
// Walls, ceiling and everything hung on them are one-sided faces looking into the room: from outside (camera zoomed out,
// top view) they are invisible, so the room never hides the table. Built by the game only (not by editor previews).
public static partial class LevelBuilder
{
    // 1 unit = 1/7 m: ~1.7 m of floor around the table (room for the cue), 2.7 m ceiling, shades ~90 cm above the felt
    const float RoomMargin = 12, RoomHeight = 19, LampY = 6.3f;
    static Worlds.Hall hall; // SetTheme

    static readonly Color[] Planks = { new(0.55f, 0.33f, 0.17f), new(0.48f, 0.28f, 0.14f), new(0.6f, 0.38f, 0.2f) };
    static readonly Color RugColor = new(0.45f, 0.08f, 0.1f), RugBorder = new(0.85f, 0.65f, 0.25f);
    static readonly Color[] Wallpaper = { new(0.1f, 0.26f, 0.2f), new(0.08f, 0.22f, 0.17f) };
    static readonly Color Panel = new(0.3f, 0.15f, 0.08f), PanelInset = new(0.36f, 0.19f, 0.1f), RailWood = new(0.5f, 0.28f, 0.13f);
    static readonly Color CeilingColor = new(0.2f, 0.13f, 0.09f), CueLight = new(0.88f, 0.72f, 0.45f), CueDark = new(0.15f, 0.08f, 0.05f);
    static readonly Color[][] Paintings =
    {
        new[] { new Color(0.5f, 0.65f, 0.75f), new Color(0.35f, 0.5f, 0.28f), new Color(0.22f, 0.35f, 0.2f) },   // hills
        new[] { new Color(0.95f, 0.6f, 0.35f), new Color(0.2f, 0.35f, 0.55f), new Color(0.12f, 0.2f, 0.35f) },  // sea at sunset
        new[] { new Color(0.85f, 0.8f, 0.65f), new Color(0.6f, 0.45f, 0.3f), new Color(0.35f, 0.22f, 0.15f) },  // desert
    };

    /// <summary>Builds the current theme's room around the level (call after Build, after SetTheme).</summary>
    public static void Room(Level lv)
    {
        var b = lv.bounds;
        if (hall == Worlds.Hall.Open)
        {
            Visual("RoomFloor", PrimitiveType.Cube, b.center + Vector3.down * (TableHeight + 0.05f), new Vector3(120, 0.1f, 120), Quaternion.identity, RoomColor, lv.root);
            return;
        }
        ClassicRoom(lv);
    }

    // classic pool room: plank floor, red rug, green striped wallpaper over wood paneling, paintings, a cue rack, pendant lamps
    static void ClassicRoom(Level lv)
    {
        var b = lv.bounds;
        float y0 = -TableHeight, y1 = y0 + RoomHeight;
        float x0 = b.min.x - RoomMargin, x1 = b.max.x + RoomMargin, z0 = b.min.z - RoomMargin, z1 = b.max.z + RoomMargin;
        var meshes = new Dictionary<Color, MeshBuilder>();
        void Quad(Color c, Vector3 p, Vector3 u, Vector3 v, Vector3 facing)
        {
            if (!meshes.TryGetValue(c, out var m)) meshes[c] = m = new MeshBuilder();
            m.Face(new[] { p, p + u, p + u + v, p + v }, facing);
        }

        // floor: planks along x, seams staggered row by row
        const float plank = 1.4f, plankLength = 9;
        for (int row = 0; z0 + row * plank < z1; row++)
        {
            float za = z0 + row * plank, zb = Mathf.Min(za + plank, z1);
            for (float xa = x0 - (row * 3.7f % plankLength), k = 0; xa < x1; xa += plankLength, k++)
            {
                float a = Mathf.Max(xa, x0), e = Mathf.Min(xa + plankLength, x1);
                Quad(Planks[(row * 7 + (int)k * 3) % Planks.Length], new Vector3(a, y0, za), new Vector3(e - a, 0, 0), new Vector3(0, 0, zb - za), Vector3.up);
            }
        }
        // rug under the table, gold border
        var rug = new Vector3(b.size.x + 8, 0, b.size.z + 8);
        Quad(RugBorder, new Vector3(b.center.x - rug.x / 2, y0 + 0.02f, b.center.z - rug.z / 2), new Vector3(rug.x, 0, 0), new Vector3(0, 0, rug.z), Vector3.up);
        rug -= new Vector3(1.2f, 0, 1.2f);
        Quad(RugColor, new Vector3(b.center.x - rug.x / 2, y0 + 0.04f, b.center.z - rug.z / 2), new Vector3(rug.x, 0, 0), new Vector3(0, 0, rug.z), Vector3.up);
        // ceiling
        Quad(CeilingColor, new Vector3(x0, y1, z0), new Vector3(x1 - x0, 0, 0), new Vector3(0, 0, z1 - z0), Vector3.down);

        // walls, counter-clockwise from above: start corner, direction along the wall, inward normal
        var walls = new[]
        {
            (start: new Vector3(x0, y0, z0), along: Vector3.right, length: x1 - x0, inward: Vector3.forward),
            (start: new Vector3(x1, y0, z0), along: Vector3.forward, length: z1 - z0, inward: Vector3.left),
            (start: new Vector3(x1, y0, z1), along: Vector3.left, length: x1 - x0, inward: Vector3.back),
            (start: new Vector3(x0, y0, z1), along: Vector3.back, length: z1 - z0, inward: Vector3.right),
        };
        int painting = 0;
        for (int i = 0; i < walls.Length; i++)
        {
            var (start, along, length, inward) = walls[i];
            // rectangle on the wall: s along it, h above the floor, sitting `inset` in front of the wall
            void Rect(Color c, float s0, float s1, float h0, float h1, float inset) =>
                Quad(c, start + along * s0 + Vector3.up * h0 + inward * inset, along * (s1 - s0), Vector3.up * (h1 - h0), inward);

            const float stripe = 1.5f, wainscot = 6.3f;
            for (float s = 0, k = 0; s < length; s += stripe, k++) Rect(Wallpaper[(int)k % 2], s, Mathf.Min(s + stripe, length), wainscot, RoomHeight, 0);
            Rect(Panel, 0, length, 0, wainscot, 0);
            const float panel = 4.5f;
            int panels = Mathf.Max(1, Mathf.FloorToInt(length / panel));
            float pw = length / panels;
            for (int p = 0; p < panels; p++) Rect(PanelInset, p * pw + 0.5f, (p + 1) * pw - 0.5f, 1.4f, wainscot - 0.8f, 0.04f);
            Rect(DarkWood, 0, length, 0, 0.9f, 0.08f);                       // baseboard
            Rect(RailWood, 0, length, wainscot, wainscot + 0.5f, 0.12f);     // chair rail
            Quad(RailWood, start + along * 0 + Vector3.up * (wainscot + 0.5f), along * length, inward * 0.12f, Vector3.up);
            Rect(RailWood, 0, length, RoomHeight - 0.7f, RoomHeight, 0.1f);  // crown molding

            if (i == 1) // cue rack in the middle of the wall behind the table (default camera looks toward +x)
            {
                float c = length / 2;
                Rect(DarkWood, c - 2, c + 2, 1.6f, 12.5f, 0.1f);
                for (int q = 0; q < 6; q++)
                {
                    float s = c - 1.5f + q * 0.6f;
                    Rect(CueDark, s - 0.09f, s + 0.09f, 2.2f, 6.2f, 0.16f);
                    Rect(CueLight, s - 0.07f, s + 0.07f, 6.2f, 11.6f, 0.16f);
                }
                Rect(RailWood, c - 2.2f, c + 2.2f, 2, 2.6f, 0.2f);
                Rect(RailWood, c - 2.2f, c + 2.2f, 10.6f, 11.2f, 0.2f);
                continue;
            }
            // paintings: gold frame, three bands of color
            int count = Mathf.Max(1, Mathf.FloorToInt(length / 14));
            for (int p = 0; p < count; p++)
            {
                float c = length * (p + 0.5f) / count, h = 11.5f;
                var colors = Paintings[painting++ % Paintings.Length];
                Rect(RugBorder, c - 2.3f, c + 2.3f, h - 1.8f, h + 1.8f, 0.1f);
                Rect(colors[0], c - 1.9f, c + 1.9f, h - 0.2f, h + 1.4f, 0.14f);
                Rect(colors[1], c - 1.9f, c + 1.9f, h - 0.8f, h - 0.2f, 0.14f);
                Rect(colors[2], c - 1.9f, c + 1.9f, h - 1.4f, h - 0.8f, 0.14f);
            }
        }

        foreach (var (color, m) in meshes)
            if (m.Build("Room", Mat(color), lv.root) is { } go) go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // pendant lamps along the table's long side, each with a warm spot light
        bool alongX = b.size.x >= b.size.z;
        float span = alongX ? b.size.x : b.size.z;
        int lamps = Mathf.Max(1, Mathf.RoundToInt(span / 6));
        var holder = new GameObject("Lamps");
        holder.transform.SetParent(lv.root, false);
        holder.AddComponent<LampHider>();
        var glow = Mat("LampGlow", LampGlowColor);
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", LampGlowColor * 1.5f);
        for (int i = 0; i < lamps; i++)
        {
            float t = span * ((i + 0.5f) / lamps - 0.5f);
            var at = b.center + (alongX ? Vector3.right : Vector3.forward) * t;
            Visual("Lamp", PrimitiveType.Cylinder, new Vector3(at.x, LampY + 0.8f, at.z), new Vector3(2.4f, 1.6f, 2.4f), Quaternion.identity, LampShadeColor, holder.transform);
            Visual("LampRod", PrimitiveType.Cube, new Vector3(at.x, (LampY + 1.6f + y1) / 2, at.z), new Vector3(0.08f, y1 - LampY - 1.6f, 0.08f), Quaternion.identity, BrassColor, holder.transform);
            var light = new GameObject("LampLight").AddComponent<Light>();
            light.transform.SetParent(holder.transform, false);
            light.transform.SetPositionAndRotation(new Vector3(at.x, LampY - 0.05f, at.z), Quaternion.Euler(90, 0, 0));
            light.type = LightType.Spot;
            light.spotAngle = 110;
            light.innerSpotAngle = 60;
            light.range = 18;
            light.intensity = LampIntensity;
            light.color = LampGlowColor;
            light.shadows = LightShadows.None;
        }
        foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    const float LampIntensity = 3;

    // the shades hang between a high camera (top view, zoomed out) and the table: hide them while the camera is above them
    class LampHider : MonoBehaviour
    {
        Renderer[] rends;
        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            rends ??= GetComponentsInChildren<Renderer>();
            bool show = cam.transform.position.y < transform.position.y + LampY - 0.3f;
            if (rends.Length > 0 && rends[0].enabled != show) foreach (var r in rends) r.enabled = show;
        }
    }
}
