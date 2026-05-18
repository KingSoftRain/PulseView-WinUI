using System.Runtime.InteropServices;

namespace PulseView.App.NativeInterop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NativeDecoderAnnotation
{
    public const int TextCapacity = 32;

    public float X0;
    public float X1;
    public byte RowIndex;
    public byte ColorRed;
    public byte ColorGreen;
    public byte ColorBlue;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TextCapacity)]
    public string Text;

    public NativeDecoderAnnotation(float x0, float x1, byte rowIndex, string text, uint colorRgb = 0xFACC15)
    {
        X0 = x0;
        X1 = x1;
        RowIndex = rowIndex;
        ColorRed = (byte)((colorRgb >> 16) & 0xFF);
        ColorGreen = (byte)((colorRgb >> 8) & 0xFF);
        ColorBlue = (byte)(colorRgb & 0xFF);
        Text = text.Length < TextCapacity ? text : text[..(TextCapacity - 1)];
    }
}
