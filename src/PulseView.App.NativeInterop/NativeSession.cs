namespace PulseView.App.NativeInterop;

public sealed class NativeSession : IDisposable
{
    private readonly NativeSessionHandle _handle;
    private bool _disposed;

    private NativeSession(NativeSessionHandle handle)
    {
        _handle = handle;
    }

    public int SignalCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var count = NativeMethods.SessionGetSignalCount(_handle);
            NativeStatus.ThrowIfFailed(count);
            return count;
        }
    }

    public double DurationSeconds
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            NativeStatus.ThrowIfFailed(NativeMethods.SessionGetDurationSeconds(_handle, out var durationSeconds));
            return durationSeconds;
        }
    }

    public static NativeSession Create()
    {
        var handle = NativeMethods.SessionCreate();
        if (handle == IntPtr.Zero) {
            throw new NativeException(NativeStatus.GetLastError());
        }

        return new NativeSession(new NativeSessionHandle(handle));
    }

    public void OpenFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Capture file path must not be empty.", nameof(path));
        }

        NativeStatus.ThrowIfFailed(NativeMethods.SessionOpenFile(_handle, path));
    }

    public NativeDigitalSpan[] QueryDigitalSpans(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requiredCount = NativeMethods.SessionQueryDigitalSpans(
            _handle,
            startSeconds,
            secondsPerPixel,
            widthPixels,
            null,
            0);
        NativeStatus.ThrowIfFailed(requiredCount);
        if (requiredCount == 0) {
            return [];
        }

        var spans = new NativeDigitalSpan[requiredCount];
        var returnedCount = NativeMethods.SessionQueryDigitalSpans(
            _handle,
            startSeconds,
            secondsPerPixel,
            widthPixels,
            spans,
            spans.Length);
        NativeStatus.ThrowIfFailed(returnedCount);

        return returnedCount == spans.Length ? spans : spans[..returnedCount];
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _handle.Dispose();
        _disposed = true;
    }
}
