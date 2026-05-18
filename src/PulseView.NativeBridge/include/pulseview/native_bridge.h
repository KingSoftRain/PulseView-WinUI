#pragma once

#if defined(_WIN32)
#if defined(PV_NATIVEBRIDGE_EXPORTS)
#define PV_API __declspec(dllexport)
#else
#define PV_API __declspec(dllimport)
#endif
#else
#define PV_API __attribute__((visibility("default")))
#endif

extern "C" {

struct pv_session_handle;

struct pv_digital_span {
    float x0;
    float x1;
    unsigned char level;
    unsigned char edge_flags;
    unsigned char channel_index;
    unsigned char reserved1;
};

enum pv_status {
    pv_status_ok = 0,
    pv_status_invalid_argument = -1,
    pv_status_file_not_found = -2,
    pv_status_not_regular_file = -3,
    pv_status_open_failed = -4,
    pv_status_unexpected_error = -100,
};

PV_API int pv_get_version(wchar_t* buffer, int buffer_length);
PV_API pv_session_handle* pv_session_create();
PV_API void pv_session_destroy(pv_session_handle* session);
PV_API int pv_session_open_file(pv_session_handle* session, const wchar_t* path);
PV_API int pv_session_get_signal_count(pv_session_handle* session);
PV_API int pv_session_get_duration_seconds(pv_session_handle* session, double* duration_seconds);
PV_API int pv_session_query_digital_spans(
    pv_session_handle* session,
    double start_seconds,
    double seconds_per_pixel,
    float width_pixels,
    pv_digital_span* buffer,
    int buffer_length);
PV_API int pv_get_last_error(wchar_t* buffer, int buffer_length);

}
