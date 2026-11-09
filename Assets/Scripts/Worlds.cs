using UnityEngine;

// 15 levels per world, one per numbered ball; each world has its own look.
public static class Worlds
{
    public const int LevelsPerWorld = 15;

    // the room around the table (LevelBuilder.Room): Open = just a floor fading into the sky
    public enum Hall { Open, Classic }

    public struct Theme
    {
        public string name;
        public Color felt, cushion, wood, darkWood, room, sky, accent;
        public Hall hall;
    }

    public static readonly Theme[] Themes =
    {
        new() { name = "Classico", felt = new(0.1f, 0.58f, 0.38f), cushion = new(0.07f, 0.46f, 0.3f), wood = new(0.66f, 0.36f, 0.17f),
            darkWood = new(0.36f, 0.19f, 0.1f), room = new(0.25f, 0.22f, 0.2f), sky = new(0.16f, 0.13f, 0.12f), accent = new(1f, 0.78f, 0.3f),
            hall = Hall.Classic },
        // world 2 (the new modules): teal felt, so the blue portal and the ice stand out (production/levels/mondo-2/layout.md)
        new() { name = "Laguna", felt = new(0.1f, 0.45f, 0.48f), cushion = new(0.07f, 0.33f, 0.36f), wood = new(0.93f, 0.88f, 0.78f),
            darkWood = new(0.5f, 0.4f, 0.3f), room = new(0.96f, 0.8f, 0.55f), sky = new(0.55f, 0.82f, 0.95f), accent = new(1f, 0.62f, 0.25f) },
        new() { name = "Oceano", felt = new(0.1f, 0.42f, 0.78f), cushion = new(0.07f, 0.32f, 0.62f), wood = new(0.93f, 0.88f, 0.78f),
            darkWood = new(0.55f, 0.47f, 0.38f), room = new(0.85f, 0.78f, 0.6f), sky = new(0.45f, 0.75f, 0.92f), accent = new(0.3f, 0.85f, 1f) },
        new() { name = "Tramonto", felt = new(0.85f, 0.3f, 0.32f), cushion = new(0.68f, 0.2f, 0.25f), wood = new(0.3f, 0.16f, 0.25f),
            darkWood = new(0.18f, 0.09f, 0.15f), room = new(0.4f, 0.2f, 0.35f), sky = new(0.98f, 0.6f, 0.4f), accent = new(1f, 0.55f, 0.35f) },
        new() { name = "Ghiaccio", felt = new(0.55f, 0.85f, 0.92f), cushion = new(0.4f, 0.72f, 0.82f), wood = new(0.92f, 0.95f, 0.98f),
            darkWood = new(0.62f, 0.7f, 0.78f), room = new(0.88f, 0.93f, 0.97f), sky = new(0.78f, 0.88f, 0.96f), accent = new(0.55f, 0.9f, 1f) },
        new() { name = "Notte", felt = new(0.42f, 0.2f, 0.75f), cushion = new(0.32f, 0.14f, 0.6f), wood = new(0.12f, 0.12f, 0.16f),
            darkWood = new(0.07f, 0.07f, 0.1f), room = new(0.1f, 0.08f, 0.16f), sky = new(0.05f, 0.04f, 0.1f), accent = new(0.85f, 0.45f, 1f) },
    };

    public static int WorldOf(int level) => (level - 1) / LevelsPerWorld;          // 0-based
    public static int BallOf(int level) => (level - 1) % LevelsPerWorld + 1;       // 1..15
    public static int Level(int world, int ball) => world * LevelsPerWorld + ball;
    public static Theme ThemeOf(int world) => Themes[world % Themes.Length];
}
