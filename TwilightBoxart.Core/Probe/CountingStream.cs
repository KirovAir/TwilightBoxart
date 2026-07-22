namespace TwilightBoxart.Core.Probe;

/// <summary>
/// A pass-through read wrapper that tallies the bytes a consumer actually pulled. Used where the
/// reader is a third-party library and so cannot report its own I/O: the point of the probe layer is
/// that identification is nearly free, and a claim like that is only worth making if it is measured.
/// Reads only - the probe layer never writes.
/// </summary>
internal sealed class CountingStream(Stream inner) : Stream
{
    /// <summary>Bytes handed to the consumer since this wrapper was created.</summary>
    public long BytesRead { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        BytesRead += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        BytesRead += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await inner.ReadAsync(buffer, ct);
        BytesRead += read;
        return read;
    }

    public override int ReadByte()
    {
        var value = inner.ReadByte();
        if (value >= 0)
        {
            BytesRead++;
        }

        return value;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush() => inner.Flush();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Deliberately does not dispose the inner stream: ownership stays with whoever opened the file.
    protected override void Dispose(bool disposing)
    {
    }
}
