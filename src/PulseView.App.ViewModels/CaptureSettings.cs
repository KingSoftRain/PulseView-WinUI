namespace PulseView.App.ViewModels;

public sealed record CaptureSampleRateOption(string Label, int SamplesPerSecond)
{
    public override string ToString() => Label;
}

public sealed record CaptureSampleCountOption(string Label, int Samples)
{
    public override string ToString() => Label;
}

public enum ProtocolDecoderKind
{
    None,
    Uart,
}

public sealed record ProtocolDecoderOption(ProtocolDecoderKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record DecoderColorOption(string Label, byte Red, byte Green, byte Blue)
{
    public uint Rgb => ((uint)Red << 16) | ((uint)Green << 8) | Blue;

    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public override string ToString() => Label;
}

public sealed record ConfiguredDecoder(
    int Id,
    ProtocolDecoderOption Protocol,
    int ChannelIndex,
    int BaudRate,
    int DataBits,
    string StopBits,
    string Parity,
    DecoderColorOption Color)
{
    public string FrameFormat => $"{DataBits}{ParityCode}{StopBits}";

    public string DisplayName => $"{Protocol.Label}  D{ChannelIndex}  {BaudRate}  {FrameFormat}";

    private string ParityCode => Parity switch
    {
        "Odd" => "O",
        "Even" => "E",
        "Mark" => "M",
        "Space" => "S",
        _ => "N",
    };
}

public enum TriggerEdgeKind
{
    None,
    Rising,
    Falling,
    Either,
}

public sealed record TriggerEdgeOption(TriggerEdgeKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record DecodedAnnotation(
    double StartSeconds,
    double EndSeconds,
    string Decoder,
    string Text,
    string Detail)
{
    public string TimeRange => $"{StartSeconds * 1000.0:0.###}-{EndSeconds * 1000.0:0.###} ms";

    public string DisplayText => $"{TimeRange}  {Decoder}: {Text}  {Detail}";
}
