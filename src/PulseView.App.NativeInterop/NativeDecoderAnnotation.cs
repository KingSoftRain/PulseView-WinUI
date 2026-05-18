using System.Runtime.InteropServices;

namespace PulseView.App.NativeInterop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NativeDecoderAnnotation
{
    public const int TextCapacity = 32;

    public float X0;
    public float X1;
    public byte RowIndex;
    private byte _reserved1;
    private byte _reserved2;
    private byte _reserved3;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TextCapacity)]
    public string Text;

    public NativeDecoderAnnotation(float x0, float x1, byte rowIndex, string text)
    {
        X0 = x0;
        X1 = x1;
        RowIndex = rowIndex;
        _reserved1 = 0;
        _reserved2 = 0;
        _reserved3 = 0;
        Text = text.Length < TextCapacity ? text : text[..(TextCapacity - 1)];
    }
}
