namespace PulseView.App.NativeInterop;

internal static class NativeRenderingStatus
{
    internal static void ThrowIfFailed(int status)
    {
        if (status < 0) {
            throw new NativeException(GetLastError());
        }
    }

    internal static string GetLastError()
    {
        var message = NativeString.Read(NativeMethods.GetRenderingLastError);
        return string.IsNullOrWhiteSpace(message) ? "Native rendering call failed." : message;
    }
}
