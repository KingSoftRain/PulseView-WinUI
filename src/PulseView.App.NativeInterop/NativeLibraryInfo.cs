namespace PulseView.App.NativeInterop;

public static class NativeLibraryInfo
{
    public static string GetVersion()
    {
        return NativeString.Read(NativeMethods.GetVersion);
    }
}
