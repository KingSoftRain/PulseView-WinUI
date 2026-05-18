using System.Diagnostics;
using PulseView.App.NativeInterop;

namespace PulseView.App.ViewModels;

internal sealed class DemoCaptureSource
{
    private const double TwoPi = Math.PI * 2.0;
    private const double UartBaudRate = 115_200.0;
    private const double UartBitSeconds = 1.0 / UartBaudRate;
    private const double UartFrameSeconds = UartBitSeconds * 10.0;
    private const double AnalogEnvelopeThreshold = 64.0;
    private const double AnalogFinePixelStep = 0.5F;
    private const double AnalogPointMinimumPixelSpacing = 7.0;
    private const double DigitalPointMinimumPixelSpacing = 7.0;
    private const byte DenseDigitalSpanFlag = 4;
    private const byte DigitalPointFlag = 8;
    private const byte AnalogEnvelopeFlag = 1;
    private const byte AnalogPointFlag = 2;
    private static readonly byte[] UartMessage = "Hello SLogic!\r\n"u8.ToArray();
    private static readonly double UartRepeatSeconds = UartMessage.Length * UartFrameSeconds + 2.0 * UartBitSeconds;
    private readonly Stopwatch _stopwatch = new();
    private double _capturedDurationSeconds;
    private int _activeDigitalChannelCount = 8;
    private double _sampleRateHz = 10_000_000.0;

    public int DigitalChannelCount => _activeDigitalChannelCount;

    public int AnalogChannelCount => 2;

    public int SignalCount => DigitalChannelCount + AnalogChannelCount;

    public bool IsRunning => _stopwatch.IsRunning;

    public double DurationSeconds => _capturedDurationSeconds + (_stopwatch.IsRunning ? _stopwatch.Elapsed.TotalSeconds : 0.0);

    public void Configure(int activeDigitalChannelCount, int sampleRateHz)
    {
        _activeDigitalChannelCount = Math.Clamp(activeDigitalChannelCount, 1, 8);
        _sampleRateHz = Math.Clamp(sampleRateHz, 1_000.0, 80_000_000.0);
    }

    public void Start()
    {
        _capturedDurationSeconds = 0.0;
        _stopwatch.Restart();
    }

    public void Stop()
    {
        if (!_stopwatch.IsRunning) {
            return;
        }

        _capturedDurationSeconds = DurationSeconds;
        _stopwatch.Reset();
    }

    public void StopAt(double durationSeconds)
    {
        _capturedDurationSeconds = Math.Max(0.0, durationSeconds);
        _stopwatch.Reset();
    }

    public void Reset()
    {
        _stopwatch.Reset();
        _capturedDurationSeconds = 0.0;
    }

    public NativeDigitalSpan[] QueryDigitalSpans(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        if (!IsValidViewport(startSeconds, secondsPerPixel, widthPixels)) {
            return [];
        }

        var visibleEnd = Math.Min(DurationSeconds, startSeconds + secondsPerPixel * widthPixels);
        if (visibleEnd <= startSeconds) {
            return [];
        }

        var spans = new List<NativeDigitalSpan>(_activeDigitalChannelCount * 64);
        for (var channel = 0; channel < _activeDigitalChannelCount; channel++) {
            AddDigitalChannel(spans, channel, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
        }

        AddDigitalSamplePoints(spans, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
        return spans.ToArray();
    }

    public NativeAnalogSegment[] QueryAnalogSegments(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        if (!IsValidViewport(startSeconds, secondsPerPixel, widthPixels)) {
            return [];
        }

        var visibleEnd = Math.Min(DurationSeconds, startSeconds + secondsPerPixel * widthPixels);
        if (visibleEnd <= startSeconds) {
            return [];
        }

        var maxVisibleX = (float)Math.Clamp((visibleEnd - startSeconds) / secondsPerPixel, 0.0, widthPixels);
        var samplesPerPixel = _sampleRateHz * secondsPerPixel;
        if (samplesPerPixel >= AnalogEnvelopeThreshold) {
            return QueryAnalogEnvelopeSegments(startSeconds, visibleEnd, secondsPerPixel, maxVisibleX);
        }

        const float pixelStep = (float)AnalogFinePixelStep;
        var segments = new List<NativeAnalogSegment>(AnalogChannelCount * Math.Max(8, (int)(maxVisibleX / pixelStep)));

        for (var channel = 0; channel < AnalogChannelCount; channel++) {
            for (var x0 = 0.0F; x0 < maxVisibleX; x0 += pixelStep) {
                var x1 = Math.Min(maxVisibleX, x0 + pixelStep);
                var t0 = startSeconds + x0 * secondsPerPixel;
                var t1 = startSeconds + x1 * secondsPerPixel;
                segments.Add(new NativeAnalogSegment(
                    x0,
                    AnalogLevel(channel, t0),
                    x1,
                    AnalogLevel(channel, t1),
                    (byte)channel));
            }
        }

        AddAnalogSamplePoints(segments, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
        return segments.ToArray();
    }

    public IReadOnlyList<DecodedAnnotation> QueryUartAnnotations(
        double startSeconds,
        double endSeconds,
        int decoderChannel,
        int baudRate,
        int maxAnnotations)
    {
        if (decoderChannel != 0 || baudRate != (int)UartBaudRate || endSeconds <= startSeconds || maxAnnotations <= 0) {
            return [];
        }

        var annotations = new List<DecodedAnnotation>(Math.Min(maxAnnotations, 64));
        var firstCycle = Math.Max(0, (int)Math.Floor(startSeconds / UartRepeatSeconds) - 1);
        for (var cycle = firstCycle; annotations.Count < maxAnnotations; cycle++) {
            var cycleStart = cycle * UartRepeatSeconds;
            if (cycleStart > endSeconds) {
                break;
            }

            for (var index = 0; index < UartMessage.Length && annotations.Count < maxAnnotations; index++) {
                var byteStart = cycleStart + index * UartFrameSeconds;
                var byteEnd = byteStart + UartFrameSeconds;
                if (byteEnd < startSeconds) {
                    continue;
                }

                if (byteStart > endSeconds) {
                    break;
                }

                var value = UartMessage[index];
                var text = value switch
                {
                    0x0D => "CR",
                    0x0A => "LF",
                    >= 0x20 and <= 0x7e => ((char)value).ToString(),
                    _ => $"0x{value:X2}",
                };
                annotations.Add(new DecodedAnnotation(
                    byteStart,
                    byteEnd,
                    "UART",
                    text,
                    $"D0, 115200 8N1, 0x{value:X2}"));
            }
        }

        return annotations;
    }

    private NativeAnalogSegment[] QueryAnalogEnvelopeSegments(
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float maxVisibleX)
    {
        var segments = new List<NativeAnalogSegment>(AnalogChannelCount * Math.Max(8, (int)Math.Ceiling(maxVisibleX)));

        for (var channel = 0; channel < AnalogChannelCount; channel++) {
            for (var x0 = 0.0F; x0 < maxVisibleX; x0 += 1.0F) {
                var x1 = Math.Min(maxVisibleX, x0 + 1.0F);
                var t0 = startSeconds + x0 * secondsPerPixel;
                var t1 = Math.Min(visibleEnd, startSeconds + x1 * secondsPerPixel);
                var (minimum, maximum) = AnalogRange(channel, t0, t1);
                segments.Add(new NativeAnalogSegment(
                    x0,
                    minimum,
                    x1,
                    maximum,
                    (byte)channel,
                    AnalogEnvelopeFlag));
            }
        }

        return segments.ToArray();
    }

    private void AddAnalogSamplePoints(
        List<NativeAnalogSegment> segments,
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float widthPixels)
    {
        var pointSpacingPixels = 1.0 / (_sampleRateHz * secondsPerPixel);
        if (!double.IsFinite(pointSpacingPixels) || pointSpacingPixels < AnalogPointMinimumPixelSpacing) {
            return;
        }

        var firstSampleIndex = Math.Max(0L, (long)Math.Ceiling(startSeconds * _sampleRateHz));
        var lastSampleIndex = (long)Math.Floor(visibleEnd * _sampleRateHz);
        if (lastSampleIndex < firstSampleIndex) {
            return;
        }

        for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++) {
            var sampleSeconds = sampleIndex / _sampleRateHz;
            var x = (float)Math.Clamp((sampleSeconds - startSeconds) / secondsPerPixel, 0.0, widthPixels);
            for (var channel = 0; channel < AnalogChannelCount; channel++) {
                var y = AnalogLevel(channel, sampleSeconds);
                segments.Add(new NativeAnalogSegment(x, y, x, y, (byte)channel, AnalogPointFlag));
            }
        }
    }

    private static void AddDigitalChannel(
        List<NativeDigitalSpan> spans,
        int channel,
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float widthPixels)
    {
        if (channel == 0) {
            AddUartDigitalChannel(spans, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
            return;
        }

        var halfPeriod = 80.0e-6 * Math.Pow(1.55, channel);
        var phase = channel * 17.0e-6;
        var visibleTransitions = (visibleEnd - startSeconds) / halfPeriod;
        if (visibleTransitions > widthPixels * 2.0) {
            AddDenseDigitalChannel(spans, channel, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
            return;
        }

        var segmentStart = Math.Floor((startSeconds + phase) / halfPeriod) * halfPeriod - phase - halfPeriod;

        while (segmentStart < visibleEnd) {
            var segmentEnd = segmentStart + halfPeriod;
            var clippedStart = Math.Max(segmentStart, startSeconds);
            var clippedEnd = Math.Min(segmentEnd, visibleEnd);

            if (clippedEnd > clippedStart) {
                var level = DigitalLevel(channel, segmentStart + halfPeriod * 0.5);
                var previousLevel = DigitalLevel(channel, segmentStart - halfPeriod * 0.5);
                var edgeFlags = (byte)0;
                if (segmentStart > startSeconds && segmentStart <= visibleEnd && level != previousLevel) {
                    edgeFlags = level != 0 ? (byte)1 : (byte)2;
                }

                spans.Add(new NativeDigitalSpan(
                    (float)Math.Clamp((clippedStart - startSeconds) / secondsPerPixel, 0.0, widthPixels),
                    (float)Math.Clamp((clippedEnd - startSeconds) / secondsPerPixel, 0.0, widthPixels),
                    level,
                    edgeFlags,
                    (byte)channel));
            }

            segmentStart = segmentEnd;
        }
    }

    private static void AddUartDigitalChannel(
        List<NativeDigitalSpan> spans,
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float widthPixels)
    {
        var visibleBits = (visibleEnd - startSeconds) / UartBitSeconds;
        if (visibleBits > widthPixels * 2.0) {
            AddDenseDigitalChannel(spans, 0, startSeconds, visibleEnd, secondsPerPixel, widthPixels);
            return;
        }

        var bitStart = Math.Floor(startSeconds / UartBitSeconds) * UartBitSeconds - UartBitSeconds;
        while (bitStart < visibleEnd) {
            var bitEnd = bitStart + UartBitSeconds;
            var clippedStart = Math.Max(bitStart, startSeconds);
            var clippedEnd = Math.Min(bitEnd, visibleEnd);
            if (clippedEnd > clippedStart) {
                var level = UartLevel(bitStart + UartBitSeconds * 0.5);
                var previousLevel = UartLevel(bitStart - UartBitSeconds * 0.5);
                var edgeFlags = (byte)0;
                if (bitStart > startSeconds && bitStart <= visibleEnd && level != previousLevel) {
                    edgeFlags = level != 0 ? (byte)1 : (byte)2;
                }

                spans.Add(new NativeDigitalSpan(
                    (float)Math.Clamp((clippedStart - startSeconds) / secondsPerPixel, 0.0, widthPixels),
                    (float)Math.Clamp((clippedEnd - startSeconds) / secondsPerPixel, 0.0, widthPixels),
                    level,
                    edgeFlags,
                    0));
            }

            bitStart = bitEnd;
        }
    }

    private void AddDigitalSamplePoints(
        List<NativeDigitalSpan> spans,
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float widthPixels)
    {
        var pointSpacingPixels = 1.0 / (_sampleRateHz * secondsPerPixel);
        if (!double.IsFinite(pointSpacingPixels) || pointSpacingPixels < DigitalPointMinimumPixelSpacing) {
            return;
        }

        var firstSampleIndex = Math.Max(0L, (long)Math.Ceiling(startSeconds * _sampleRateHz));
        var lastSampleIndex = (long)Math.Floor(visibleEnd * _sampleRateHz);
        if (lastSampleIndex < firstSampleIndex) {
            return;
        }

        for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++) {
            var sampleSeconds = sampleIndex / _sampleRateHz;
            var x = (float)Math.Clamp((sampleSeconds - startSeconds) / secondsPerPixel, 0.0, widthPixels);
            for (var channel = 0; channel < _activeDigitalChannelCount; channel++) {
                spans.Add(new NativeDigitalSpan(x, x, DigitalLevel(channel, sampleSeconds), DigitalPointFlag, (byte)channel));
            }
        }
    }

    private static void AddDenseDigitalChannel(
        List<NativeDigitalSpan> spans,
        int channel,
        double startSeconds,
        double visibleEnd,
        double secondsPerPixel,
        float widthPixels)
    {
        var maxVisibleX = (float)Math.Clamp((visibleEnd - startSeconds) / secondsPerPixel, 0.0, widthPixels);
        if (maxVisibleX > 0.0F) {
            spans.Add(new NativeDigitalSpan(0.0F, maxVisibleX, 1, DenseDigitalSpanFlag, (byte)channel));
        }
    }

    private static byte DigitalLevel(int channel, double seconds)
    {
        if (channel == 0) {
            return UartLevel(seconds);
        }

        var halfPeriod = 80.0e-6 * Math.Pow(1.55, channel);
        var phase = channel * 17.0e-6;
        var index = Math.Floor((seconds + phase) / halfPeriod);
        return ((long)index & 1L) == 0 ? (byte)0 : (byte)1;
    }

    private static byte UartLevel(double seconds)
    {
        if (seconds < 0.0) {
            return 1;
        }

        var offset = seconds % UartRepeatSeconds;
        if (offset >= UartMessage.Length * UartFrameSeconds) {
            return 1;
        }

        var byteIndex = (int)(offset / UartFrameSeconds);
        var bitOffset = offset - byteIndex * UartFrameSeconds;
        var bitIndex = (int)(bitOffset / UartBitSeconds);
        if (bitIndex <= 0) {
            return 0;
        }

        if (bitIndex >= 9) {
            return 1;
        }

        return (byte)((UartMessage[byteIndex] >> (bitIndex - 1)) & 1);
    }

    private static float AnalogLevel(int channel, double seconds)
    {
        var value = channel == 0
            ? 0.72 * Math.Sin(TwoPi * 730.0 * seconds) + 0.18 * Math.Sin(TwoPi * 91.0 * seconds)
            : 0.58 * Math.Sin(TwoPi * 190.0 * seconds + 0.9) + 0.26 * Saw(seconds * 55.0);
        return (float)Math.Clamp(value, -1.0, 1.0);
    }

    private static (float Minimum, float Maximum) AnalogRange(int channel, double startSeconds, double endSeconds)
    {
        const int sampleCount = 12;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;

        for (var sample = 0; sample < sampleCount; sample++) {
            var fraction = sampleCount == 1 ? 0.0 : (double)sample / (sampleCount - 1);
            var value = AnalogLevel(channel, startSeconds + (endSeconds - startSeconds) * fraction);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        return (minimum, maximum);
    }

    private static double Saw(double value)
    {
        return 2.0 * (value - Math.Floor(value)) - 1.0;
    }

    private static bool IsValidViewport(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        return double.IsFinite(startSeconds)
            && double.IsFinite(secondsPerPixel)
            && float.IsFinite(widthPixels)
            && startSeconds >= 0.0
            && secondsPerPixel > 0.0
            && widthPixels > 0.0F;
    }
}
