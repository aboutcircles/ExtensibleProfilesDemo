namespace Circles.Profiles.Sdk.Utils;

/// <summary>
/// Read-only wrapper that enforces a hard byte limit while optionally "leasing"
/// another IDisposable (e.g., HttpResponseMessage) until this stream is disposed.
/// </summary>
public sealed class LimitedReadStream(Stream inner, long limit, IDisposable? lease = null) : Stream
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly long _limit = limit;
    private long _remaining = limit;

    private void ThrowIfExceeded()
    {
        bool exceeded = _remaining <= 0;
        if (exceeded)
        {
            throw new IOException($"IPFS response exceeds {_limit:N0} bytes");
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfExceeded();
        int allowed = (int)Math.Min(count, _remaining);
        int n = _inner.Read(buffer, offset, allowed);
        _remaining -= n;
        return n;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfExceeded();
        int allowed = (int)Math.Min(buffer.Length, _remaining);
        int n = await _inner.ReadAsync(buffer[..allowed], cancellationToken);
        _remaining -= n;
        return n;
    }

    /* ---- boiler-plate passthroughs ---- */
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long o, SeekOrigin k) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            lease?.Dispose();
        }

        base.Dispose(disposing);
    }
}