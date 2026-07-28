using System.ComponentModel.DataAnnotations;
using System.Reflection;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Tests;

/// <summary>
/// Guards the two things the web app shows a user: which systems it supports, and what each one is
/// called.
/// </summary>
/// <remarks>
/// Both used to be hand-copied into the browser client and both drifted for years without anything
/// noticing, because a scanner that skips an extension and a card that simply has no such ROM on it
/// look identical from the outside. Index.cshtml now renders them from here, so a console added to
/// the enum without an extension or without a label is a wrong page rather than a compile error.
/// These tests are what makes it neither.
/// </remarks>
[TestClass]
public class ConsoleFileTypesTests
{
    [TestMethod]
    public void EveryConsole_HasAtLeastOneExtension()
    {
        var empty = SupportedFiles.ByConsole
            .Where(entry => entry.Extensions.Count == 0)
            .Select(entry => entry.Console)
            .ToList();

        Assert.AreEqual(0, empty.Count,
            "These consoles would render as a supported system with no file type next to it:" +
            Environment.NewLine + string.Join(Environment.NewLine, empty.Select(c => $"  {c}")));
    }

    /// <summary>
    /// Looks for the attribute rather than comparing Name() to the enum name, because three consoles
    /// (WonderSwan, ColecoVision, Intellivision) are legitimately spelled the same either way.
    /// </summary>
    [TestMethod]
    public void EveryConsole_HasItsOwnDisplayName()
    {
        var unlabelled = Enum.GetValues<ConsoleType>()
            .Where(console => console != ConsoleType.Unknown)
            .Where(console => typeof(ConsoleType).GetField(console.ToString())!
                .GetCustomAttribute<DisplayAttribute>() is null)
            .ToList();

        Assert.AreEqual(0, unlabelled.Count,
            "These consoles have no [Display(Name = ...)], so the raw enum name is what a user sees:" +
            Environment.NewLine + string.Join(Environment.NewLine, unlabelled.Select(c => $"  {c}")));
    }

    /// <summary>
    /// The projection is what the page and the scanner both read, so an extension missing from it is
    /// a file the browser client walks past while the backend would have identified it fine.
    /// </summary>
    [TestMethod]
    public void ByConsole_CoversEveryRomExtension()
    {
        var listed = SupportedFiles.ByConsole.SelectMany(entry => entry.Extensions).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            SupportedFiles.Rom.Order(StringComparer.Ordinal).ToArray(),
            listed.Order(StringComparer.Ordinal).ToArray(),
            "SupportedFiles.ByConsole and SupportedFiles.Rom must describe the same set of files.");
    }
}
