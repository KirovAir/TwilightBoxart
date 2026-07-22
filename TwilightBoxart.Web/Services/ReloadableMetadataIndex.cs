using TwilightBoxart.Core.Identify;

namespace TwilightBoxart.Web.Services;

/// <summary>
/// The one <see cref="IMetadataIndex"/> the host ever hands out, delegating to whatever index file
/// is current. Exists because the index can now be rebuilt while the server runs: consumers hold
/// this wrapper for the process lifetime and <see cref="Swap"/> re-points it at a freshly built
/// file underneath them.
/// </summary>
public sealed class ReloadableMetadataIndex(string path, ILogger<ReloadableMetadataIndex> logger)
    : IMetadataIndex, IDisposable
{
    private volatile IMetadataIndex _inner = SqliteMetadataIndex.Open(path, logger);

    /// <summary>
    /// Replaces the index with a freshly built file. The old connection is closed BEFORE the move,
    /// because Windows will not replace a file something still holds open; lookups that land in the
    /// swap window miss against a null index for a few milliseconds, which the ladder already treats
    /// as an ordinary degraded state rather than an error. If the move fails the previous file is
    /// still in place, so it is reopened rather than leaving the server serving misses until restart.
    /// </summary>
    public void Swap(string builtFile)
    {
        var old = _inner;
        _inner = new NullMetadataIndex("index update in progress");
        (old as IDisposable)?.Dispose();

        try
        {
            File.Move(builtFile, path, overwrite: true);
        }
        catch
        {
            _inner = SqliteMetadataIndex.Open(path, logger);
            throw;
        }

        _inner = SqliteMetadataIndex.Open(path, logger);
        logger.LogInformation("Index swapped in: version {Version}, {Rows} rows", Version, RowCount);
    }

    // A lookup that read _inner just before Swap disposed it throws ObjectDisposedException out of
    // the reader pool. That thread gets the same answer the swap window already promises everyone
    // else: a miss, not a 500.

    public bool TryByCrc32(uint crc32, out IndexEntry entry)
    {
        try
        {
            return _inner.TryByCrc32(crc32, out entry);
        }
        catch (ObjectDisposedException)
        {
            entry = null!;
            return false;
        }
    }

    public bool TryBySha1(string sha1, out IndexEntry entry)
    {
        try
        {
            return _inner.TryBySha1(sha1, out entry);
        }
        catch (ObjectDisposedException)
        {
            entry = null!;
            return false;
        }
    }

    public bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry)
    {
        try
        {
            return _inner.TryBySerial(console, serial, out entry);
        }
        catch (ObjectDisposedException)
        {
            entry = null!;
            return false;
        }
    }

    public IndexEntry? SearchByName(ConsoleType console, string name)
    {
        try
        {
            return _inner.SearchByName(console, name);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public string Version => _inner.Version;

    public int RowCount => _inner.RowCount;

    /// <summary>Why lookups are degraded, when they are. Null while a real index is open.</summary>
    public string? UnavailableReason => (_inner as NullMetadataIndex)?.Reason;

    /// <summary>
    /// Idempotent: the container disposal-tracks both this registration and the
    /// <see cref="IMetadataIndex"/> forward of it, so Dispose runs twice at shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_inner is not IDisposable disposable)
        {
            return;
        }

        _inner = new NullMetadataIndex("index closed");
        disposable.Dispose();
    }
}
