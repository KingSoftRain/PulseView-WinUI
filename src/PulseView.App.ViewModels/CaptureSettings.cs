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
