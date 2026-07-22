namespace TwilightBoxart.Desktop.Services;

/// <summary>
/// Produces the box art file name TWiLightMenu++ looks up: the ROM's own name with <c>.png</c> appended.
/// </summary>
public static class SafeName
{
    // FAT is the target, and the same name must be produced on every OS so a card scanned on a Mac and
    // rescanned on Windows does not write the cover twice under two spellings. So the invalid set is
    // fixed (FAT's, plus control characters), NOT Path.GetInvalidFileNameChars(), which differs per OS.
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Replaces characters a FAT card cannot store with <c>_</c>, collapsing runs of them.</summary>
    private static string Sanitize(string name)
    {
        var chars = new char[name.Length];
        var previousUnderscore = false;
        var length = 0;

        foreach (var ch in name)
        {
            var bad = ch < 0x20 || Array.IndexOf(Invalid, ch) >= 0;
            if (bad)
            {
                if (previousUnderscore)
                {
                    continue;
                }

                chars[length++] = '_';
                previousUnderscore = true;
            }
            else
            {
                chars[length++] = ch;
                previousUnderscore = false;
            }
        }

        var result = new string(chars, 0, length).Trim();
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>
    /// The output file name for a ROM: <c>&lt;name&gt;.png</c>, where the name includes the ROM's own
    /// extension exactly as the browser client writes it. The inner archive entry name is used when it is
    /// recognisably a ROM; otherwise the file on the card is the archive itself (No-Intro DSiWare blobs
    /// are entries named things like <c>00000000</c>), so that is what the menu will look art up by.
    /// </summary>
    public static string OutputFileName(string outerFileName, string? innerName, bool innerIsRom)
    {
        var basis = innerIsRom && !string.IsNullOrEmpty(innerName) ? innerName : outerFileName;
        return Sanitize(basis) + ".png";
    }
}
