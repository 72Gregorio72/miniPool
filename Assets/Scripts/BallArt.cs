using System.Collections.Generic;
using UnityEngine;

// Procedural pool-ball textures (equirectangular, for a UV sphere): color or stripe band + numbered white discs.
public static class BallArt
{
    const int W = 512, H = 256, DiscRadius = 34, Pixel = 4, Mark = 28;

    public static readonly Color[] Colors =
    {
        new(0.97f, 0.96f, 0.9f), new(1f, 0.78f, 0f), new(0f, 0.22f, 0.8f), new(0.85f, 0.08f, 0.08f), new(0.35f, 0.05f, 0.55f),
        new(1f, 0.45f, 0f), new(0f, 0.45f, 0.2f), new(0.45f, 0.08f, 0.08f), new(0.03f, 0.03f, 0.03f),
    };

    // 5x7 digits, one 5-bit row each, top row first, bit 4 = leftmost column
    static readonly int[][] Font =
    {
        new[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, new[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
        new[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, new[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
        new[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, new[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
        new[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, new[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
        new[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, new[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
    };

    static readonly Dictionary<int, Material> cache = new();

    // cue-ball skins: body color + equator band (band = body for a plain ball)
    public struct Skin
    {
        public string name;
        public Color body, band;
        public int world;    // 0 = free; N = unlocked by beating every level of world N (1-based)
        public bool crowns;  // ...each one with the crown (Rules.Crown)
    }

    public static bool Unlocked(int s) => Skins[s].world == 0 || GameManager.WorldDone(Skins[s].world - 1, Skins[s].crowns);

    public static string Requirement(int s) =>
        Skins[s].crowns ? $"Corona in tutti i livelli del Mondo {Skins[s].world}" : $"Finisci il Mondo {Skins[s].world}";

    public static readonly Skin[] Skins =
    {
        new() { name = "Classica", body = Colors[0], band = Colors[0] },
        new() { name = "Perla", body = new(0.95f, 0.92f, 1f), band = new(0.72f, 0.58f, 0.95f) },
        new() { name = "Oceano", body = new(0.88f, 0.96f, 1f), band = new(0.1f, 0.45f, 0.9f) },
        new() { name = "Menta", body = new(0.8f, 1f, 0.9f), band = new(0.1f, 0.7f, 0.5f) },
        new() { name = "Ciliegia", body = new(1f, 0.35f, 0.42f), band = new(1f, 0.95f, 0.95f) },
        new() { name = "Oro", body = new(1f, 0.8f, 0.25f), band = new(1f, 0.8f, 0.25f) },
        new() { name = "Lava", body = new(0.16f, 0.13f, 0.13f), band = new(1f, 0.42f, 0.05f) },
        new() { name = "Notte", body = new(0.12f, 0.12f, 0.22f), band = new(0.62f, 0.42f, 1f) },
        new() { name = "Smeraldo", body = new(0.1f, 0.58f, 0.38f), band = new(1f, 0.78f, 0.3f), world = 1 },
        new() { name = "Reale", body = new(0.42f, 0.1f, 0.68f), band = new(1f, 0.8f, 0.2f), world = 1, crowns = true },
    };

    public static int SelectedSkin
    {
        get { int s = Mathf.Clamp(PlayerPrefs.GetInt("MiniPool.skin", 0), 0, Skins.Length - 1); return Unlocked(s) ? s : 0; }
        set { PlayerPrefs.SetInt("MiniPool.skin", value); PlayerPrefs.Save(); }
    }

    // grey ball (`*` in the level): plain, no number, only there to move the cue around without a foul
    public const int Grey = 16;
    static readonly Color GreyColor = new(0.55f, 0.55f, 0.57f);

    // main color of ball n (stripes: their band); shortcut: online every cue ball gets the local skin, pass the owner's if it shows
    public static Color ColorOf(int n) => n == 0 ? Skins[SelectedSkin].body : n == Grey ? GreyColor : Colors[n > 8 ? n - 8 : n];

    // n = 0 is the cue ball with the chosen skin
    public static Material Material(int n)
    {
        if (n == 0) return CueMaterial(SelectedSkin);
        if (n == Grey) return Cached(n, () => Texture(0, GreyColor, GreyColor));
        var color = Colors[n <= 8 ? n : n - 8];
        return Cached(n, () => Texture(n, n > 8 ? Colors[0] : color, color));
    }

    public static Material CueMaterial(int skin) => Cached(-1 - skin, () => Texture(0, Skins[skin].body, Skins[skin].band));

    // multiplayer player colors (also the triangle marks on their balls)
    public static readonly Color[] PlayerColors =
    {
        new(0.95f, 0.25f, 0.25f), new(0.2f, 0.5f, 1f), new(0.2f, 0.8f, 0.35f), new(1f, 0.82f, 0.1f),
        new(0.7f, 0.35f, 1f), new(1f, 0.55f, 0.1f), new(0.1f, 0.85f, 0.9f), new(1f, 0.45f, 0.75f),
    };

    // a player's cue (with their skin) or 8, with triangles in their color; mark = -1: no triangles
    public static Material PlayerMaterial(int n, int skin, int mark)
    {
        if (mark < 0) return n == 0 ? CueMaterial(skin) : Material(n);
        var body = n == 0 ? Skins[skin].body : Colors[8];
        var band = n == 0 ? Skins[skin].band : Colors[8];
        return Cached(1000 + n * 100 + skin * 10 + mark, () => Texture(n, body, band, PlayerColors[mark]));
    }

    static readonly Dictionary<Material, Material> ghosts = new();

    // see-through copy (URP Lit, transparent): the other players' 8s in multiplayer
    public static Material Ghost(Material src)
    {
        if (ghosts.TryGetValue(src, out var g) && g) return g;
        g = new Material(src);
        g.SetFloat("_Surface", 1); // transparent
        g.SetFloat("_Blend", 0);   // alpha
        g.SetOverrideTag("RenderType", "Transparent");
        g.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        g.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        g.SetFloat("_ZWrite", 0);
        g.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        g.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        var c = g.color;
        c.a = 0.3f;
        g.color = c;
        return ghosts[src] = g;
    }

    static Material Cached(int key, System.Func<Texture2D> texture)
    {
        if (cache.TryGetValue(key, out var m) && m) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { mainTexture = texture() };
        m.SetFloat("_Smoothness", 0.5f); // stylized: a soft highlight, not a mirror
        return cache[key] = m;
    }

    static Texture2D Texture(int n, Color body, Color bandColor, Color? mark = null)
    {
        var white = Colors[0];
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool band = y > H * 0.3f && y < H * 0.7f;
                px[y * W + x] = band ? bandColor : body;
            }

        if (n > 0)
            foreach (int cx in new[] { W / 4, 3 * W / 4 })
            {
                for (int y = -DiscRadius; y <= DiscRadius; y++)
                    for (int x = -DiscRadius; x <= DiscRadius; x++)
                        if (x * x + y * y <= DiscRadius * DiscRadius) px[(H / 2 + y) * W + cx + x] = white;
                DrawNumber(px, n, cx, H / 2);
            }

        // owner marks: 4 triangles around the equator, between the number discs, plus one on each pole cap
        if (mark is Color m)
            foreach (var (cx, cy) in new[] { (W / 8, H / 2), (3 * W / 8, H / 2), (5 * W / 8, H / 2), (7 * W / 8, H / 2), (W / 4, H / 8), (3 * W / 4, 7 * H / 8) })
                for (int y = -Mark; y <= Mark; y++)
                {
                    int half = (Mark - y) / 2; // apex up
                    for (int x = -half; x <= half; x++) px[(cy + y) * W + cx + x] = m;
                }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Ball" + n, anisoLevel = 4 };
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static void DrawNumber(Color[] px, int n, int cx, int cy)
    {
        string s = n.ToString();
        int glyphW = 5 * Pixel, gap = Pixel, total = s.Length * glyphW + (s.Length - 1) * gap;
        int left = cx - total / 2, top = cy + 7 * Pixel / 2;
        for (int i = 0; i < s.Length; i++)
        {
            var g = Font[s[i] - '0'];
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                {
                    if ((g[row] >> (4 - col) & 1) == 0) continue;
                    for (int dy = 0; dy < Pixel; dy++)
                        for (int dx = 0; dx < Pixel; dx++)
                            px[(top - row * Pixel - dy) * W + left + i * (glyphW + gap) + col * Pixel + dx] = Color.black;
                }
        }
    }
}
