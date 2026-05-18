using System.Runtime.InteropServices;

namespace PulseView.App.NativeInterop;

[StructLayout(LayoutKind.Sequential)]
public readonly struct NativeDigitalSpan
{
    public readonly float X0;
    public readonly float X1;
    public readonly byte Level;
    public readonly byte EdgeFlags;
    public readonly byte ChannelIndex;
    private readonly byte _reserved1;

    public NativeDigitalSpan(float x0, float x1, byte level, byte edgeFlags, byte channelIndex = 0)
    {
        X0 = x0;
        X1 = x1;
        Level = level;
        EdgeFlags = edgeFlags;
        ChannelIndex = channelIndex;
        _reserved1 = 0;
    }
}
