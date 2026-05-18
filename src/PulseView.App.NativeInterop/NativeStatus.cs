namespace PulseView.App.NativeInterop;

internal static class NativeStatus
{
    internal static void ThrowIfFailed(int status)
    {
        if (status < 0) {
            throw new NativeException(GetLastError());
        }
    }

    internal static string GetLastError()
    {
        var message = NativeString.Read(NativeMethods.GetLastError);
        return string.IsNullOrWhiteSpace(message) ? "Native call failed." : message;
    }
}
