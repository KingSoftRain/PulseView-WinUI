#pragma once

#if defined(_WIN32)
#if defined(PV_RENDERING_EXPORTS)
#define PV_RENDERING_API __declspec(dllexport)
#else
#define PV_RENDERING_API __declspec(dllimport)
#endif
#else
#define PV_RENDERING_API __attribute__((visibility("default")))
#endif

extern "C" {

struct pv_renderer_handle;

struct pv_rendering_digital_span {
    float x0;
    float x1;
    unsigned char level;
    unsigned char edge_flags;
    unsigned char channel_index;
    unsigned char reserved1;
};

struct pv_rendering_analog_segment {
    float x0;
    float y0;
    float x1;
    float y1;
    unsigned char channel_index;
    unsigned char flags;
    unsigned char reserved1;
    unsigned char reserved2;
};

struct pv_rendering_decoder_annotation {
    float x0;
    float x1;
    unsigned char row_index;
    unsigned char reserved1;
    unsigned char reserved2;
    unsigned char reserved3;
    wchar_t text[32];
};

PV_RENDERING_API int pv_rendering_get_version(wchar_t* buffer, int buffer_length);
PV_RENDERING_API int pv_rendering_get_last_error(wchar_t* buffer, int buffer_length);

PV_RENDERING_API pv_renderer_handle* pv_renderer_create();
PV_RENDERING_API void pv_renderer_destroy(pv_renderer_handle* renderer);
PV_RENDERING_API int pv_renderer_attach_swap_chain_panel(pv_renderer_handle* renderer, void* swap_chain_panel_unknown);
PV_RENDERING_API int pv_renderer_resize(pv_renderer_handle* renderer, int pixel_width, int pixel_height, float dpi);
PV_RENDERING_API int pv_renderer_set_viewport(pv_renderer_handle* renderer, double start_seconds, double seconds_per_pixel);
PV_RENDERING_API int pv_renderer_set_channel_counts(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count);
PV_RENDERING_API int pv_renderer_set_digital_spans(
    pv_renderer_handle* renderer,
    const pv_rendering_digital_span* spans,
    int span_count);
PV_RENDERING_API int pv_renderer_set_analog_segments(
    pv_renderer_handle* renderer,
    const pv_rendering_analog_segment* segments,
    int segment_count);
PV_RENDERING_API int pv_renderer_set_waveform_data(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count);
PV_RENDERING_API int pv_renderer_set_viewport_waveform_data(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count);
PV_RENDERING_API int pv_renderer_set_viewport_waveform_data_ex(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel,
    int digital_channel_count,
    int analog_channel_count,
    int decoder_row_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count,
    const pv_rendering_decoder_annotation* annotations,
    int annotation_count);
PV_RENDERING_API int pv_renderer_get_device_info(
    pv_renderer_handle* renderer,
    wchar_t* buffer,
    int buffer_length);
PV_RENDERING_API int pv_renderer_clear_digital_spans(pv_renderer_handle* renderer);
PV_RENDERING_API int pv_renderer_render_demo(pv_renderer_handle* renderer);

}
