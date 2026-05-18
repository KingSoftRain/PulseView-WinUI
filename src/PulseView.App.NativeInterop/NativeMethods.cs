using System.Runtime.InteropServices;

namespace PulseView.App.NativeInterop;

internal static partial class NativeMethods
{
    private const string NativeBridge = "PulseView.NativeBridge";
    private const string RenderingNative = "PulseView.Rendering.Native";

    [DllImport(NativeBridge, EntryPoint = "pv_get_version", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetVersion([Out] char[] buffer, int bufferLength);

    [DllImport(NativeBridge, EntryPoint = "pv_session_create", ExactSpelling = true)]
    internal static extern IntPtr SessionCreate();

    [DllImport(NativeBridge, EntryPoint = "pv_session_destroy", ExactSpelling = true)]
    internal static extern void SessionDestroy(IntPtr session);

    [DllImport(NativeBridge, EntryPoint = "pv_session_open_file", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SessionOpenFile(NativeSessionHandle session, string path);

    [DllImport(NativeBridge, EntryPoint = "pv_session_get_signal_count", ExactSpelling = true)]
    internal static extern int SessionGetSignalCount(NativeSessionHandle session);

    [DllImport(NativeBridge, EntryPoint = "pv_session_get_duration_seconds", ExactSpelling = true)]
    internal static extern int SessionGetDurationSeconds(NativeSessionHandle session, out double durationSeconds);

    [DllImport(NativeBridge, EntryPoint = "pv_session_query_digital_spans", ExactSpelling = true)]
    internal static extern int SessionQueryDigitalSpans(
        NativeSessionHandle session,
        double startSeconds,
        double secondsPerPixel,
        float widthPixels,
        [Out] NativeDigitalSpan[]? buffer,
        int bufferLength);

    [DllImport(NativeBridge, EntryPoint = "pv_get_last_error", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetLastError([Out] char[] buffer, int bufferLength);

    [DllImport(RenderingNative, EntryPoint = "pv_rendering_get_version", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetRenderingVersion([Out] char[] buffer, int bufferLength);

    [DllImport(RenderingNative, EntryPoint = "pv_rendering_get_last_error", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetRenderingLastError([Out] char[] buffer, int bufferLength);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_create", ExactSpelling = true)]
    internal static extern IntPtr RendererCreate();

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_destroy", ExactSpelling = true)]
    internal static extern void RendererDestroy(IntPtr renderer);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_attach_swap_chain_panel", ExactSpelling = true)]
    internal static extern int RendererAttachSwapChainPanel(NativeRendererHandle renderer, IntPtr swapChainPanelUnknown);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_resize", ExactSpelling = true)]
    internal static extern int RendererResize(NativeRendererHandle renderer, int pixelWidth, int pixelHeight, float dpi);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_viewport", ExactSpelling = true)]
    internal static extern int RendererSetViewport(NativeRendererHandle renderer, double startSeconds, double secondsPerPixel);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_channel_counts", ExactSpelling = true)]
    internal static extern int RendererSetChannelCounts(NativeRendererHandle renderer, int digitalChannelCount, int analogChannelCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_digital_spans", ExactSpelling = true)]
    internal static extern int RendererSetDigitalSpans(
        NativeRendererHandle renderer,
        [In] NativeDigitalSpan[]? spans,
        int spanCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_analog_segments", ExactSpelling = true)]
    internal static extern int RendererSetAnalogSegments(
        NativeRendererHandle renderer,
        [In] NativeAnalogSegment[]? segments,
        int segmentCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_waveform_data", ExactSpelling = true)]
    internal static extern int RendererSetWaveformData(
        NativeRendererHandle renderer,
        int digitalChannelCount,
        int analogChannelCount,
        [In] NativeDigitalSpan[]? spans,
        int spanCount,
        [In] NativeAnalogSegment[]? segments,
        int segmentCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_viewport_waveform_data", ExactSpelling = true)]
    internal static extern int RendererSetViewportWaveformData(
        NativeRendererHandle renderer,
        double startSeconds,
        double secondsPerPixel,
        int digitalChannelCount,
        int analogChannelCount,
        [In] NativeDigitalSpan[]? spans,
        int spanCount,
        [In] NativeAnalogSegment[]? segments,
        int segmentCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_set_viewport_waveform_data_ex", ExactSpelling = true)]
    internal static extern int RendererSetViewportWaveformDataEx(
        NativeRendererHandle renderer,
        double startSeconds,
        double secondsPerPixel,
        int digitalChannelCount,
        int analogChannelCount,
        int decoderRowCount,
        [In] NativeDigitalSpan[]? spans,
        int spanCount,
        [In] NativeAnalogSegment[]? segments,
        int segmentCount,
        [In] NativeDecoderAnnotation[]? annotations,
        int annotationCount);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_get_device_info", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int RendererGetDeviceInfo(NativeRendererHandle renderer, [Out] char[] buffer, int bufferLength);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_clear_digital_spans", ExactSpelling = true)]
    internal static extern int RendererClearDigitalSpans(NativeRendererHandle renderer);

    [DllImport(RenderingNative, EntryPoint = "pv_renderer_render_demo", ExactSpelling = true)]
    internal static extern int RendererRenderDemo(NativeRendererHandle renderer);
}
