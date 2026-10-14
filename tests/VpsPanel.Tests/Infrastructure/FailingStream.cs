namespace VpsPanel.Tests.Infrastructure;

/// <summary>
/// A read stream that first returns a few bytes and then throws the given exception.
/// </summary>
internal sealed class FailingStream(Exception error) : Stream
{
    private bool _firstReadDone;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_firstReadDone)
            throw error;
        _firstReadDone = true;
        "part"u8.CopyTo(buffer.Span);
        return ValueTask.FromResult(4);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}