# Native ABI

The managed application talks to native code through `PulseView.NativeBridge`.
The ABI is C-only: no C++ classes, STL containers, exceptions, or templates cross
the DLL boundary.

## Status Codes

Native APIs that perform work return `0` for success and a negative value for
failure.

| Code | Name | Meaning |
| --- | --- | --- |
| `0` | `pv_status_ok` | Operation completed. |
| `-1` | `pv_status_invalid_argument` | A handle, path, or buffer argument is invalid. |
| `-2` | `pv_status_file_not_found` | The requested capture file does not exist. |
| `-3` | `pv_status_not_regular_file` | The capture path is not a regular file. |
| `-4` | `pv_status_open_failed` | The capture file could not be opened for reading. |
| `-100` | `pv_status_unexpected_error` | An unexpected native error occurred. |

`pv_get_last_error()` returns a thread-local UTF-16 error message for the last
failed native call.

## Session API

```cpp
extern "C" PV_API pv_session_handle* pv_session_create();
extern "C" PV_API void pv_session_destroy(pv_session_handle* session);
extern "C" PV_API int pv_session_open_file(
    pv_session_handle* session,
    const wchar_t* path
);
extern "C" PV_API int pv_session_get_signal_count(
    pv_session_handle* session
);
extern "C" PV_API int pv_session_get_duration_seconds(
    pv_session_handle* session,
    double* duration_seconds
);
extern "C" PV_API int pv_session_query_digital_spans(
    pv_session_handle* session,
    double start_seconds,
    double seconds_per_pixel,
    float width_pixels,
    pv_digital_span* buffer,
    int buffer_length
);
```

The C# side wraps `pv_session_handle*` with `NativeSessionHandle`, a
`SafeHandle` implementation. ViewModels use `ISessionService`; they do not store
or manipulate native pointers.

`pv_session_query_digital_spans()` returns the number of spans required for the
current viewport. Passing a null buffer or zero buffer length is the sizing pass.
The C# layer marshals only these viewport primitives, not full sample buffers.
`pv_session_get_duration_seconds()` returns the currently opened finite sample
duration.

## Rendering API

`PulseView.Rendering.Native` uses the same C ABI rule set. The WinUI control
passes only the `SwapChainPanel` COM `IUnknown` pointer to native code; sample
data and renderer internals stay out of C#.

```cpp
extern "C" PV_RENDERING_API pv_renderer_handle* pv_renderer_create();
extern "C" PV_RENDERING_API void pv_renderer_destroy(pv_renderer_handle* renderer);
extern "C" PV_RENDERING_API int pv_renderer_attach_swap_chain_panel(
    pv_renderer_handle* renderer,
    void* swap_chain_panel_unknown
);
extern "C" PV_RENDERING_API int pv_renderer_resize(
    pv_renderer_handle* renderer,
    int pixel_width,
    int pixel_height,
    float dpi
);
extern "C" PV_RENDERING_API int pv_renderer_set_viewport(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel
);
extern "C" PV_RENDERING_API int pv_renderer_set_channel_counts(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count
);
extern "C" PV_RENDERING_API int pv_renderer_set_digital_spans(
    pv_renderer_handle* renderer,
    const pv_rendering_digital_span* spans,
    int span_count
);
extern "C" PV_RENDERING_API int pv_renderer_set_analog_segments(
    pv_renderer_handle* renderer,
    const pv_rendering_analog_segment* segments,
    int segment_count
);
extern "C" PV_RENDERING_API int pv_renderer_set_waveform_data(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count
);
extern "C" PV_RENDERING_API int pv_renderer_set_viewport_waveform_data(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count
);
extern "C" PV_RENDERING_API int pv_renderer_get_device_info(
    pv_renderer_handle* renderer,
    wchar_t* buffer,
    int buffer_length
);
extern "C" PV_RENDERING_API int pv_renderer_clear_digital_spans(
    pv_renderer_handle* renderer
);
extern "C" PV_RENDERING_API int pv_renderer_render_demo(
    pv_renderer_handle* renderer
);
extern "C" PV_RENDERING_API int pv_rendering_get_last_error(
    wchar_t* buffer,
    int buffer_length
);
```

The C# side wraps `pv_renderer_handle*` with `NativeRendererHandle`.
`pv_renderer_set_channel_counts()` establishes the track layout for the next
render. Digital spans and analog segments carry a channel index; analog segment
Y values are normalized to `[-1, 1]` inside their track.
Digital span edge flag bit `4` marks a dense overview span that should be drawn
as a filled digital activity block. Analog segment flag bit `1` marks a min/max
envelope rectangle instead of a line segment.
`pv_renderer_set_waveform_data()` is the preferred UI path because it updates
layout and both primitive arrays before drawing once.
`pv_renderer_set_viewport_waveform_data()` is the interactive path; it updates
the viewport and all waveform primitives before a single draw/present.
`pv_renderer_get_device_info()` reports the Direct3D adapter selected by the
renderer so the app can confirm hardware acceleration in diagnostics.
Setting zero digital spans means the current session viewport is empty.
Clearing digital spans returns the renderer to the built-in synthetic fallback
used before any capture is loaded.
