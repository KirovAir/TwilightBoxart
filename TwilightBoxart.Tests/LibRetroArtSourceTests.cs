using System.Text;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

[TestClass]
public class LibRetroArtSourceTests
{
    [TestMethod]
    public void LibRetroArtSource_BuildsTheRepositoryUrlFromTheConsoleType()
    {
        Assert.AreEqual(
            "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy/master/Named_Boxarts/Tetris%20%28World%29.png",
            LibRetroArtSource.BuildUrl(ConsoleType.GameBoy, "Tetris (World)"));

        Assert.AreEqual(
            "https://raw.githubusercontent.com/libretro-thumbnails/Sega_-_Mega_Drive_-_Genesis/master/Named_Boxarts/Sonic.png",
            LibRetroArtSource.BuildUrl(ConsoleType.MegaDrive, "Sonic"));
    }

    /// <summary>
    /// Every repository name this can emit must be a repository that exists. These twelve strings were
    /// checked against the live libretro-thumbnails org; the DSi one is here because it did not.
    /// </summary>
    /// <remarks>
    /// <c>ConsoleType.NintendoDsi</c> was originally described as "Nintendo - Nintendo DSi (Digital)",
    /// which is DAT-o-MATIC's name for the DSiWare set. The thumbnails org has no such repository, so
    /// every DSi cover 404'd invisibly, because a missing cover is an ordinary outcome on this source.
    /// A wrong repository name cannot be caught by any amount of URL-shape testing, only by pinning the
    /// exact string.
    /// </remarks>
    [TestMethod]
    [DataRow(ConsoleType.NintendoDsi, "Nintendo_-_Nintendo_DSi")]
    [DataRow(ConsoleType.NintendoDs, "Nintendo_-_Nintendo_DS")]
    [DataRow(ConsoleType.GameBoy, "Nintendo_-_Game_Boy")]
    [DataRow(ConsoleType.GameBoyColor, "Nintendo_-_Game_Boy_Color")]
    [DataRow(ConsoleType.GameBoyAdvance, "Nintendo_-_Game_Boy_Advance")]
    [DataRow(ConsoleType.Nes, "Nintendo_-_Nintendo_Entertainment_System")]
    [DataRow(ConsoleType.Snes, "Nintendo_-_Super_Nintendo_Entertainment_System")]
    [DataRow(ConsoleType.Nintendo64, "Nintendo_-_Nintendo_64")]
    [DataRow(ConsoleType.FamicomDiskSystem, "Nintendo_-_Family_Computer_Disk_System")]
    [DataRow(ConsoleType.MegaDrive, "Sega_-_Mega_Drive_-_Genesis")]
    [DataRow(ConsoleType.MasterSystem, "Sega_-_Master_System_-_Mark_III")]
    [DataRow(ConsoleType.GameGear, "Sega_-_Game_Gear")]
    public void LibRetroArtSource_RepositoryName_MatchesTheLiveThumbnailsOrg(
        ConsoleType console, string expectedRepository)
    {
        Assert.AreEqual(
            $"https://raw.githubusercontent.com/libretro-thumbnails/{expectedRepository}/master/Named_Boxarts/Game.png",
            LibRetroArtSource.BuildUrl(console, "Game"));
    }

    [TestMethod]
    public void LibRetroArtSource_TargetsRawGithubUserContentDirectly()
    {
        var url = LibRetroArtSource.BuildUrl(ConsoleType.Snes, "Super Mario World (USA)");

        Assert.IsNotNull(url);
        StringAssert.StartsWith(url, "https://raw.githubusercontent.com/libretro-thumbnails/");

        // The 2020 client used github.com/<org>/<repo>/raw/..., which 302-redirects here: a wasted round
        // trip on every one of ~18,000 requests, and an empty ACAO on the redirect.
        Assert.IsFalse(url.Contains("//github.com/", StringComparison.Ordinal));
        Assert.IsFalse(url.Contains("/raw/master/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LibRetroArtSource_ReplacesEveryIllegalNameCharacter()
    {
        // libretro's own rule: & * / : ` < > ? \ | all become '_'.
        Assert.AreEqual(
            "Tom _ Jerry _ Star _ Slash _ Colon _ Tick _ Lt _ Gt _ Q _ Back _ Pipe",
            LibRetroArtSource.SanitizeName("Tom & Jerry * Star / Slash : Colon ` Tick < Lt > Gt ? Q \\ Back | Pipe"));
    }

    [TestMethod]
    public void LibRetroArtSource_LeavesLegalPunctuationAlone()
    {
        // No-Intro names are full of these; replacing them would miss every regional dump.
        const string name = "Legend of Zelda, The - A Link to the Past (USA) [!].png-ish #1 100% + 'quote'";

        Assert.AreEqual(name, LibRetroArtSource.SanitizeName(name));
    }

    [TestMethod]
    public void LibRetroArtSource_PercentEncodesTheFileSegment()
    {
        var url = LibRetroArtSource.BuildUrl(ConsoleType.Nes, "Mario & Luigi (USA, Europe)");

        // '&' is sanitised to '_' before encoding, so it can never be read as a query separator.
        Assert.AreEqual(
            "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System/master/Named_Boxarts/Mario%20_%20Luigi%20%28USA%2C%20Europe%29.png",
            url);
    }

    [TestMethod]
    public void LibRetroArtSource_ReturnsNullWhenItCannotAddressAThumbnail()
    {
        Assert.IsNull(LibRetroArtSource.BuildUrl(ConsoleType.Unknown, "Tetris (World)"));
        Assert.IsNull(LibRetroArtSource.BuildUrl(ConsoleType.GameBoy, null));
        Assert.IsNull(LibRetroArtSource.BuildUrl(ConsoleType.GameBoy, "   "));
    }

    [TestMethod]
    public void LibRetroArtSource_ResolvesAGitSymlinkBodyAgainstTheSameDirectory()
    {
        // libretro dedupes revision variants as git symlinks, and raw.githubusercontent serves the
        // symlink blob verbatim: a 200 whose body is the target file name.
        var url = LibRetroArtSource.BuildUrl(ConsoleType.Snes, "Donkey Kong Country (USA) (Rev 2)")!;
        var body = Encoding.UTF8.GetBytes("Donkey Kong Country (USA).png");

        Assert.AreEqual(
            "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Super_Nintendo_Entertainment_System/master/Named_Boxarts/Donkey%20Kong%20Country%20%28USA%29.png",
            LibRetroArtSource.ResolveSymlinkTarget(url, body));
    }

    [TestMethod]
    public void LibRetroArtSource_RefusesSymlinkBodiesThatAreNotABareFileName()
    {
        var url = LibRetroArtSource.BuildUrl(ConsoleType.Snes, "Game")!;

        // An HTML soft-404, traversal, binary that failed the sniffer, a multi-line body.
        Assert.IsNull(LibRetroArtSource.ResolveSymlinkTarget(url, Encoding.UTF8.GetBytes("<html>nope</html>")));
        Assert.IsNull(LibRetroArtSource.ResolveSymlinkTarget(url, Encoding.UTF8.GetBytes("../../../etc/evil.png")));
        Assert.IsNull(LibRetroArtSource.ResolveSymlinkTarget(url, [0x89, 0x50, 0x4E, 0x47]));
        Assert.IsNull(LibRetroArtSource.ResolveSymlinkTarget(url, Encoding.UTF8.GetBytes("a.png\nb.png")));
    }
}
