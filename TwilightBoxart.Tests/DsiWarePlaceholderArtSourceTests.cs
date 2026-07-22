using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

[TestClass]
public class DsiWarePlaceholderArtSourceTests
{
    private static RomIdentity Identity(ConsoleType console, string serial) => new()
    {
        ConsoleType = console,
        Key = serial,
        Serial = serial,
        MatchMethod = MatchMethod.HeaderSerial,
    };

    [TestMethod]
    public void CanHandle_AcceptsDsiWarePrefixesAndNothingElse()
    {
        var source = new DsiWarePlaceholderArtSource();

        // K is DSiWare, H the system channels, Z the 3DS Virtual Console re-releases; the old
        // K/H-only check silently dropped the Z titles.
        Assert.IsTrue(source.CanHandle(Identity(ConsoleType.NintendoDsi, "K4DE")));
        Assert.IsTrue(source.CanHandle(Identity(ConsoleType.NintendoDs, "HNGP")));
        Assert.IsTrue(source.CanHandle(Identity(ConsoleType.NintendoDsi, "Z2EJ")));

        Assert.IsFalse(source.CanHandle(Identity(ConsoleType.NintendoDs, "AMCE")), "retail DS is not DSiWare");
        Assert.IsFalse(source.CanHandle(Identity(ConsoleType.GameBoyAdvance, "KYGE")), "wrong console");
        Assert.IsFalse(source.CanHandle(new RomIdentity
        {
            ConsoleType = ConsoleType.NintendoDsi,
            Key = "name-keyed",
            MatchMethod = MatchMethod.Filename,
        }), "no serial, no placeholder");
    }

    [TestMethod]
    public async Task TryFetch_ReturnsTheEmbeddedJpeg()
    {
        var blob = await new DsiWarePlaceholderArtSource().TryFetchAsync(Identity(ConsoleType.NintendoDsi, "K4DE"));

        Assert.IsNotNull(blob);
        Assert.AreEqual("image/jpeg", blob.ContentType);
        // JPEG magic: the embedded resource must actually be there and be an image.
        Assert.AreEqual(0xFF, blob.Data[0]);
        Assert.AreEqual(0xD8, blob.Data[1]);
    }
}
