using TwilightBoxart.Data.Entities;
using TwilightBoxart.Core;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>
/// One budgeted directory of cached blobs. Bytes only - no bookkeeping.
/// </summary>
/// <remarks>
/// Two instances exist: "originals" holds upstream art content-addressed by
/// SHA-256 and gets the large budget, "renders" holds resized/bordered output keyed by
/// platform/key/source-hash/render-discriminator and gets a small one, because a render costs about
/// 5 ms to recreate.
///
/// PNGs belong on a filesystem, not in SQLite: they are large, immutable, served whole, and an
/// operator wants to be able to rsync the directory or point a tool at it. What does NOT belong here
/// is the accounting - which entry is coldest, how big each layer has grown - because answering that
/// from the filesystem means stat-ing the whole tree on every sweep. That lives in the
/// <see cref="Data.Entities.CacheEntry"/> table and is reached through <see cref="CacheIndex"/>,
/// which is the only thing that should be calling the mutating methods below.
///
/// The root lives outside wwwroot on purpose - the 2020 backend put its cache under wwwroot and
/// served it with UseStaticFiles, making every cached image enumerable.
/// </remarks>
public sealed class DiskCache
{
    public DiskCache(string name, CacheKind kind, string root, long budgetBytes)
    {
        Name = name;
        Kind = kind;
        Root = root;
        BudgetBytes = budgetBytes;
        Directory.CreateDirectory(root);
    }

    /// <summary>Layer name as it appears in <c>/v2/health</c> and in every cache key: "originals" / "renders".</summary>
    public string Name { get; }

    /// <summary>Which of the two layers this is, as stored on every row.</summary>
    public CacheKind Kind { get; }

    public string Root { get; }

    public long BudgetBytes { get; }

    private string FullPath(string relativePath) => Path.Combine(Root, relativePath);

    /// <summary>Returns null when the entry cannot be read - a miss, never an exception.</summary>
    public async Task<byte[]?> TryReadAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            return await File.ReadAllBytesAsync(FullPath(relativePath), ct);
        }
        catch (IOException)
        {
            // Missing file or directory, but also a sharing violation or a torn disk: all of it is
            // a miss, and the pipeline refetches.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task WriteAsync(string relativePath, ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        AtomicFile.WriteAsync(FullPath(relativePath), data, ct);

    public void Delete(string relativePath) => AtomicFile.TryDelete(FullPath(relativePath));

    /// <summary>
    /// Every blob currently on disk, with its size. Used once at startup to reconcile the entry
    /// table with reality: a manually deleted file, or a wiped volume, must not leave rows claiming
    /// disk that nothing is using.
    /// </summary>
    public IEnumerable<(string RelativePath, long SizeBytes)> Enumerate()
    {
        if (!Directory.Exists(Root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch (IOException)
            {
                continue;
            }

            yield return (Path.GetRelativePath(Root, file), info.Length);
        }
    }

    /// <summary>
    /// The <see cref="Data.Entities.CacheEntry.CacheKey"/> for a file in this layer: the layer name
    /// and the relative path, always forward-slashed so a key written on Windows and one written on
    /// Linux for the same file on the same volume are the same string.
    /// </summary>
    public string CacheKeyFor(string relativePath) =>
        $"{Name}/{relativePath.Replace('\\', '/')}";

    /// <summary>The inverse of <see cref="CacheKeyFor"/>, so the sweep can delete straight from a row.</summary>
    public string RelativePathFor(string cacheKey)
    {
        var prefix = Name + "/";
        var path = cacheKey.StartsWith(prefix, StringComparison.Ordinal) ? cacheKey[prefix.Length..] : cacheKey;
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
}
