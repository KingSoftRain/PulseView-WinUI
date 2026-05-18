using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PulseView.App.NativeInterop;

public sealed class NativeWaveformRenderer : IDisposable
{
    private readonly NativeRendererHandle _handle;
    private bool _disposed;

    private NativeWaveformRenderer(NativeRendererHandle handle)
    {
        _handle = handle;
    }

    public static NativeWaveformRenderer Create()
    {
        var handle = NativeMethods.RendererCreate();
        if (handle == IntPtr.Zero) {
            throw new NativeException(NativeRenderingStatus.GetLastError());
        }

        return new NativeWaveformRenderer(new NativeRendererHandle(handle));
    }

    [SupportedOSPlatform("windows")]
    public void AttachSwapChainPanel(object swapChainPanel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(swapChainPanel);

        var panelUnknown = Marshal.GetIUnknownForObject(swapChainPanel);
        try {
            NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererAttachSwapChainPanel(_handle, panelUnknown));
        }
        finally {
            Marshal.Release(panelUnknown);
        }
    }

    public void Resize(int pixelWidth, int pixelHeight, float dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererResize(_handle, pixelWidth, pixelHeight, dpi));
    }

    public void SetViewport(double startSeconds, double secondsPerPixel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererSetViewport(_handle, startSeconds, secondsPerPixel));
    }

    public void SetChannelCounts(int digitalChannelCount, int analogChannelCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeRenderingStatus.ThrowIfFailed(
            NativeMethods.RendererSetChannelCounts(_handle, digitalChannelCount, analogChannelCount));
    }

    public void SetDigitalSpans(IReadOnlyList<NativeDigitalSpan> spans)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spans);

        var spanArray = spans as NativeDigitalSpan[] ?? spans.ToArray();
        NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererSetDigitalSpans(_handle, spanArray, spanArray.Length));
    }

    public void SetAnalogSegments(IReadOnlyList<NativeAnalogSegment> segments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(segments);

        var segmentArray = segments as NativeAnalogSegment[] ?? segments.ToArray();
        NativeRenderingStatus.ThrowIfFailed(
            NativeMethods.RendererSetAnalogSegments(_handle, segmentArray, segmentArray.Length));
    }

    public void SetWaveformData(
        int digitalChannelCount,
        int analogChannelCount,
        IReadOnlyList<NativeDigitalSpan> spans,
        IReadOnlyList<NativeAnalogSegment> segments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(segments);

        var spanArray = spans as NativeDigitalSpan[] ?? spans.ToArray();
        var segmentArray = segments as NativeAnalogSegment[] ?? segments.ToArray();
        NativeRenderingStatus.ThrowIfFailed(
            NativeMethods.RendererSetWaveformData(
                _handle,
                digitalChannelCount,
                analogChannelCount,
                spanArray,
                spanArray.Length,
                segmentArray,
                segmentArray.Length));
    }

    public void SetViewportWaveformData(
        double startSeconds,
        double secondsPerPixel,
        int digitalChannelCount,
        int analogChannelCount,
        IReadOnlyList<NativeDigitalSpan> spans,
        IReadOnlyList<NativeAnalogSegment> segments)
    {
        SetViewportWaveformData(
            startSeconds,
            secondsPerPixel,
            digitalChannelCount,
            analogChannelCount,
            0,
            spans,
            segments,
            []);
    }

    public void SetViewportWaveformData(
        double startSeconds,
        double secondsPerPixel,
        int digitalChannelCount,
        int analogChannelCount,
        int decoderRowCount,
        IReadOnlyList<NativeDigitalSpan> spans,
        IReadOnlyList<NativeAnalogSegment> segments,
        IReadOnlyList<NativeDecoderAnnotation> annotations)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(annotations);

        var spanArray = spans as NativeDigitalSpan[] ?? spans.ToArray();
        var segmentArray = segments as NativeAnalogSegment[] ?? segments.ToArray();
        var annotationArray = annotations as NativeDecoderAnnotation[] ?? annotations.ToArray();
        NativeRenderingStatus.ThrowIfFailed(
            NativeMethods.RendererSetViewportWaveformDataEx(
                _handle,
                startSeconds,
                secondsPerPixel,
                digitalChannelCount,
                analogChannelCount,
                decoderRowCount,
                spanArray,
                spanArray.Length,
                segmentArray,
                segmentArray.Length,
                annotationArray,
                annotationArray.Length));
    }

    public string GetDeviceInfo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requiredLength = NativeMethods.RendererGetDeviceInfo(_handle, [], 0);
        if (requiredLength <= 0) {
            return string.Empty;
        }

        var buffer = new char[requiredLength + 1];
        var copiedLength = NativeMethods.RendererGetDeviceInfo(_handle, buffer, buffer.Length);
        return new string(buffer, 0, Math.Min(copiedLength, requiredLength));
    }

    public void ClearDigitalSpans()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererClearDigitalSpans(_handle));
    }

    public void RenderDemo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeRenderingStatus.ThrowIfFailed(NativeMethods.RendererRenderDemo(_handle));
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _handle.Dispose();
        _disposed = true;
    }
}
