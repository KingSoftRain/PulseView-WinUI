using PulseView.App.NativeInterop;

namespace PulseView.App.ViewModels;

public interface ISessionService : IDisposable
{
    Task<SessionInfo> OpenFileAsync(string path, CancellationToken cancellationToken = default);
    NativeDigitalSpan[] QueryDigitalSpans(double startSeconds, double secondsPerPixel, float widthPixels);
}
