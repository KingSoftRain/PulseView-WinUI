using Microsoft.Win32.SafeHandles;

namespace PulseView.App.NativeInterop;

public sealed class NativeRendererHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeRendererHandle()
        : base(ownsHandle: true)
    {
    }

    internal NativeRendererHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.RendererDestroy(handle);
        return true;
    }
}
