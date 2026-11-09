using NUnit.Framework;

public class WorldsTests
{
    [Test] public void FifteenBallsPerWorld()
    {
        Assert.AreEqual((0, 1), (Worlds.WorldOf(1), Worlds.BallOf(1)));
        Assert.AreEqual((0, 15), (Worlds.WorldOf(15), Worlds.BallOf(15)));
        Assert.AreEqual((1, 1), (Worlds.WorldOf(16), Worlds.BallOf(16)));
        for (int lv = 1; lv <= 60; lv++) Assert.AreEqual(lv, Worlds.Level(Worlds.WorldOf(lv), Worlds.BallOf(lv)));
    }
}
