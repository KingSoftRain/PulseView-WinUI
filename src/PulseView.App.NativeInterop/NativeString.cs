namespace PulseView.App.NativeInterop;

internal static class NativeString
{
    internal delegate int NativeStringReader(char[] buffer, int bufferLength);

    internal static string Read(NativeStringReader reader)
    {
        var buffer = new char[256];
        var length = reader(buffer, buffer.Length);
        if (length <= 0) {
            return string.Empty;
        }

        var safeLength = Math.Min(length, buffer.Length);
        return new string(buffer, 0, safeLength).TrimEnd('\0');
    }
}
