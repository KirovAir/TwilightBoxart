using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Consoles;

namespace TwilightBoxart.Tests;

[TestClass]
public class GameTdbArtSourceTests
{
    [TestMethod]
    public void GameTdbArtSource_BuildsCoverUrlsInQualityOrder()
    {
        var urls = GameTdbArtSource.Variants
            .Select(variant => GameTdbArtSource.BuildUrl(variant, "US", "ASME"))
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "https://art.gametdb.com/ds/coverHQ/US/ASME.jpg",
                "https://art.gametdb.com/ds/coverM/US/ASME.jpg",
                // Plain "cover", not "coverS" - the 2020 client's third URL was a path GameTDB never
                // served. It is a .jpg like the other two: /ds/cover/US/ASME.jpg answers 200 and the
                // .png form answers 404, so asking for .png makes this rung unreachable.
                "https://art.gametdb.com/ds/cover/US/ASME.jpg",
            },
            urls);
    }

    [TestMethod]
    public void GameTdbArtSource_BuildsRegionalUrlsFromTheSharedRegionMap()
    {
        // The mapping itself lives in GameTdbRegion and is tested there; what matters here is that this
        // source routes through it, so V (Europe/Australia DSiWare) really does reach the English covers.
        Assert.AreEqual(
            "https://art.gametdb.com/ds/coverHQ/EN/KGUV.jpg",
            GameTdbArtSource.BuildUrl(GameTdbArtSource.Variants[0], GameTdbRegion.From('V'), "KGUV"));

        Assert.AreEqual(
            "https://art.gametdb.com/ds/coverHQ/JA/ASMJ.jpg",
            GameTdbArtSource.BuildUrl(GameTdbArtSource.Variants[0], GameTdbRegion.From('J'), "ASMJ"));
    }

    [TestMethod]
    public void GameTdbArtSource_RejectsTitleIdsThatAreNotFourAlphanumerics()
    {
        Assert.IsTrue(GameTdbArtSource.IsUsableTitleId("ASME"));
        Assert.IsTrue(GameTdbArtSource.IsUsableTitleId("KGUV"));

        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId(null));
        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId(""));
        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId("ASM"));
        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId("ASMEX"));
        // A garbage header must not become a path segment we send to someone else's server.
        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId("../."));
        Assert.IsFalse(GameTdbArtSource.IsUsableTitleId("A\0ME"));
    }
}
