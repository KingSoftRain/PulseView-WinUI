namespace PulseView.App.NativeInterop;

public static class NativeRenderingInfo
{
    public static string GetVersion()
    {
        return NativeString.Read(NativeMethods.GetRenderingVersion);
    }
}
