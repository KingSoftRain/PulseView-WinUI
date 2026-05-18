using PulseView.App.NativeInterop;

namespace PulseView.App.ViewModels;

public sealed class SessionService : ISessionService
{
    private readonly object _gate = new();
    private NativeSession? _session;

    public async Task<SessionInfo> OpenFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = NativeSession.Create();
            try {
                session.OpenFile(path);
                var info = new SessionInfo(path, session.SignalCount, session.DurationSeconds);

                NativeSession? previousSession;
                lock (_gate) {
                    previousSession = _session;
                    _session = session;
                }

                previousSession?.Dispose();
                session = null;

                return info;
            }
            finally {
                session?.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public NativeDigitalSpan[] QueryDigitalSpans(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        lock (_gate) {
            return _session?.QueryDigitalSpans(startSeconds, secondsPerPixel, widthPixels) ?? [];
        }
    }

    public void Dispose()
    {
        NativeSession? session;
        lock (_gate) {
            session = _session;
            _session = null;
        }

        session?.Dispose();
    }
}
