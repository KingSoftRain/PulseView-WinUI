using Microsoft.Win32.SafeHandles;

namespace PulseView.App.NativeInterop;

public sealed class NativeSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeSessionHandle()
        : base(ownsHandle: true)
    {
    }

    internal NativeSessionHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.SessionDestroy(handle);
        return true;
    }
}
