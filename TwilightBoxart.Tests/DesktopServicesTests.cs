using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;
using TwilightBoxart.Desktop.Services;

namespace TwilightBoxart.Tests;

[TestClass]
public class DesktopServicesTests
{
    // ── SafeName.OutputFileName ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void OutputFileName_AppendsPngToTheRomsOwnName()
    {
        Assert.AreEqual("Some Game (USA).nds.png", SafeName.OutputFileName("Some Game (USA).nds", null, false));
    }

    [TestMethod]
    public void OutputFileName_SanitizesFatInvalidCharacters()
    {
        // The invalid set is FAT's, fixed on every OS: ':' and '?' are legal in Linux file names, so a
        // per-OS Path.GetInvalidFileNameChars() would spell the same cover two ways on one card.
        Assert.AreEqual("Mario_ Kart _DS_.nds.png", SafeName.OutputFileName("Mario: Kart <DS>?.nds", null, false));
    }

    [TestMethod]
    public void OutputFileName_SanitizesControlCharacters()
    {
        Assert.AreEqual("a_b.nds.png", SafeName.OutputFileName("a\tb.nds", null, false));
    }

    [TestMethod]
    public void OutputFileName_CollapsesARunOfInvalidCharactersToOneUnderscore()
    {
        Assert.AreEqual("a_b.nds.png", SafeName.OutputFileName("a<>|b.nds", null, false));
    }

    [TestMethod]
    public void OutputFileName_WhitespaceOnlyNameFallsBackToUnderscore()
    {
        Assert.AreEqual("_.png", SafeName.OutputFileName("   ", null, false));
    }

    [TestMethod]
    public void OutputFileName_UsesTheInnerEntryNameWhenItIsARom()
    {
        Assert.AreEqual("Game (Europe).nds.png", SafeName.OutputFileName("archive.zip", "Game (Europe).nds", true));
    }

    [TestMethod]
    public void OutputFileName_KeepsTheArchiveNameForCdnBlobEntries()
    {
        // A No-Intro DSiWare zip's entry is named "00000000"; the file on the card is the archive
        // itself, so that is the name the menu looks art up by.
        Assert.AreEqual("Flipnote (Europe).zip.png", SafeName.OutputFileName("Flipnote (Europe).zip", "00000000", false));
    }

    [TestMethod]
    public void OutputFileName_EmptyInnerNameFallsBackToTheOuterName()
    {
        Assert.AreEqual("archive.zip.png", SafeName.OutputFileName("archive.zip", "", true));
    }

    // ── ScanService.CollectFiles ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void CollectFiles_FindsRomsRecursivelyAndSkipsTheBoxartDir()
    {
        var root = MakeTempRoot();
        var boxart = Path.Combine(root, "_nds", "TWiLightMenu", "boxart");
        WriteFile(root, "top.nds");
        WriteFile(Path.Combine(root, "games", "nested"), "deep.gba");
        WriteFile(boxart, "output.nds");

        var names = CollectNames(root, boxart);

        CollectionAssert.Contains(names, "top.nds");
        CollectionAssert.Contains(names, "deep.gba");
        CollectionAssert.DoesNotContain(names, "output.nds");
    }

    [TestMethod]
    public void CollectFiles_KeepsASiblingOfTheBoxartDir()
    {
        // Regression: a prefix match without the trailing separator also swallowed "boxart-old".
        var root = MakeTempRoot();
        var boxart = Path.Combine(root, "_nds", "TWiLightMenu", "boxart");
        WriteFile(boxart, "inside.nds");
        WriteFile(Path.Combine(root, "_nds", "TWiLightMenu", "boxart-old"), "keep.nds");

        var names = CollectNames(root, boxart);

        CollectionAssert.Contains(names, "keep.nds");
        CollectionAssert.DoesNotContain(names, "inside.nds");
    }

    [TestMethod]
    public void CollectFiles_BoxartDirGivenWithATrailingSeparator_IsStillSkipped()
    {
        var root = MakeTempRoot();
        var boxart = Path.Combine(root, "_nds", "TWiLightMenu", "boxart");
        WriteFile(boxart, "inside.nds");

        var files = ScanService.CollectFiles(root, boxart + Path.DirectorySeparatorChar);

        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void CollectFiles_SkipsAppleDoubleForksAndNonRoms()
    {
        var root = MakeTempRoot();
        WriteFile(root, "._game.nds");
        WriteFile(root, "notes.txt");
        WriteFile(root, "game.nds");

        var files = ScanService.CollectFiles(root, Path.Combine(root, "_nds", "TWiLightMenu", "boxart"));

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual("game.nds", Path.GetFileName(files[0]));
    }

    // ── ScanService.RunAsync ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_AFailedWriteDoesNotAbortTheRestOfTheScan()
    {
        var root = MakeTempRoot();
        var boxart = ScanService.BoxartDirectory(root);
        WriteRom(root, "first.nds");
        WriteRom(root, "second.nds");

        // A directory squatting on the first output path makes its atomic commit throw - the same
        // failure shape as a card that fills up mid-scan.
        Directory.CreateDirectory(Path.Combine(boxart, "first.nds.png"));

        var service = new ScanService(new RomProbeService(), NullLogger<ScanService>.Instance);
        var request = new ScanRequest(root, boxart, RenderOptions.Default, Overwrite: true, Concurrency: 2);
        var progress = new CapturingProgress();
        using var backend = new AlwaysMatchingBackend();

        await service.RunAsync(backend, request, progress, CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(boxart, "second.nds.png")), "the other cover must still be written");
        Assert.IsNotNull(progress.Last);
        StringAssert.StartsWith(progress.Last.Status, "Done.", "the scan must finish, not fail");
    }

    /// <summary>Identifies everything and serves one fake PNG, so RunAsync tests never leave the disk.</summary>
    private sealed class AlwaysMatchingBackend : IArtBackend
    {
        public string Describe => "test backend";

        public Task<IReadOnlyList<RomIdentity>> IdentifyAsync(
            IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RomIdentity>>([.. fingerprints.Select(f => new RomIdentity
            {
                ConsoleType = ConsoleType.NintendoDs,
                Key = "AAAA",
                MatchMethod = MatchMethod.Crc32,
                Tag = f.Tag,
            })]);

        public Task<byte[]?> GetArtAsync(RomIdentity identity, RenderOptions options, CancellationToken ct) =>
            Task.FromResult<byte[]?>([0x89, 0x50, 0x4E, 0x47]);

        public Task<IReadOnlySet<string>> GetScannableExtensionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<string>>(SupportedFiles.Scannable);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingProgress : IProgress<ScanUpdate>
    {
        public ScanUpdate? Last { get; private set; }

        public void Report(ScanUpdate value)
        {
            if (value.Status is not null)
            {
                Last = value;
            }
        }
    }

    /// <summary>A junk file big enough for the loose probe to hash and header-sniff.</summary>
    private static void WriteRom(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), new byte[4096]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static List<string?> CollectNames(string root, string boxartDir) =>
        [.. ScanService.CollectFiles(root, boxartDir).Select(Path.GetFileName)];

    private readonly List<string> _tempDirectories = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A straggling handle is not worth failing the run over.
            }
        }

        _tempDirectories.Clear();
    }

    private string MakeTempRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twldesk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private static void WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), [0x42]);
    }
}
