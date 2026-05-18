using System.Runtime.InteropServices;

namespace PulseView.App.NativeInterop;

[StructLayout(LayoutKind.Sequential)]
public readonly struct NativeAnalogSegment
{
    public readonly float X0;
    public readonly float Y0;
    public readonly float X1;
    public readonly float Y1;
    public readonly byte ChannelIndex;
    public readonly byte Flags;
    private readonly byte _reserved1;
    private readonly byte _reserved2;

    public NativeAnalogSegment(float x0, float y0, float x1, float y1, byte channelIndex, byte flags = 0)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        ChannelIndex = channelIndex;
        Flags = flags;
        _reserved1 = 0;
        _reserved2 = 0;
    }
}
