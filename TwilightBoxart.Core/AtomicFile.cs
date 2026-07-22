namespace TwilightBoxart.Core;

/// <summary>
/// Write-then-rename file writes. Every persistent write in this project goes through here.
/// </summary>
/// <remarks>
/// Two temp-file strategies, because there are genuinely two kinds of write. ONE-SHOT
/// (<see cref="WriteAsync"/>) uses a UNIQUE temp name, so two requests racing on the same cache entry
/// cannot clobber each other's partial file. MULTI-STEP (<see cref="TempPathFor"/> then
/// <see cref="Commit"/>) uses a STABLE sibling name, for callers that build a file over many
/// operations and need to know its path throughout, notably SQLite, which creates <c>-wal</c> and
/// <c>-shm</c> files alongside whatever it is writing. Temp files are siblings of the destination in
/// both cases: <see cref="File.Move(string, string, bool)"/> is only atomic within one volume, and
/// %TEMP% is frequently on another.
/// </remarks>
public static class AtomicFile
{
    /// <summary>
    /// Stable temp path for a multi-step write. Hold the returned path, write to it, then
    /// <see cref="Commit"/>. For a single write prefer <see cref="WriteAsync"/>, which manages its own
    /// unique temp and is safe against concurrent writers.
    /// </summary>
    public static string TempPathFor(string finalPath) => finalPath + ".tmp";

    /// <summary>Moves a completed temp file over the final path, replacing whatever was there.</summary>
    public static void Commit(string tempPath, string finalPath)
    {
        EnsureDirectory(finalPath);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureDirectory(path);

        var temp = UniqueTempFor(path);
        try
        {
            await File.WriteAllBytesAsync(temp, data, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Deletes a file, ignoring failures. Only ever called on cleanup paths.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Losing a temp file to a race or a read-only mount is not worth failing a request over;
            // SweepStaleTemporaries cleans up whatever survives.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes temp files left behind by a hard kill. Called once at startup per cache root: without
    /// it, an unclean shutdown slowly leaks disk that no eviction pass would ever reclaim, because
    /// temp files are not cache entries.
    /// </summary>
    public static int SweepStaleTemporaries(string root, TimeSpan olderThan)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private static string UniqueTempFor(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
