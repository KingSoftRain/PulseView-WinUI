#include "pulseview/native_bridge.h"

#include "pulseview/core/session.h"
#include "pulseview/core/version.h"
#include "pulseview/core/viewport.h"

#include <algorithm>
#include <filesystem>
#include <memory>
#include <new>
#include <string>
#include <string_view>

struct pv_session_handle {
    explicit pv_session_handle(std::unique_ptr<pulseview::core::Session> session_value)
        : session(std::move(session_value))
    {
    }

    std::unique_ptr<pulseview::core::Session> session;
};

namespace {

thread_local std::wstring last_error;

void set_last_error(std::wstring message)
{
    last_error = std::move(message);
}

void clear_last_error()
{
    last_error.clear();
}

int copy_to_buffer(std::wstring_view value, wchar_t* buffer, int buffer_length)
{
    if (buffer == nullptr || buffer_length <= 0) {
        return static_cast<int>(value.size());
    }

    const auto capacity = static_cast<std::size_t>(buffer_length);
    const auto chars_to_copy = std::min(value.size(), capacity - 1);
    std::copy_n(value.data(), chars_to_copy, buffer);
    buffer[chars_to_copy] = L'\0';

    return static_cast<int>(chars_to_copy);
}

int status_for_open_file_result(pulseview::core::OpenFileStatus status)
{
    switch (status) {
    case pulseview::core::OpenFileStatus::ok:
        clear_last_error();
        return pv_status_ok;
    case pulseview::core::OpenFileStatus::file_not_found:
        set_last_error(L"Capture file was not found.");
        return pv_status_file_not_found;
    case pulseview::core::OpenFileStatus::not_regular_file:
        set_last_error(L"Capture path is not a regular file.");
        return pv_status_not_regular_file;
    case pulseview::core::OpenFileStatus::open_failed:
        set_last_error(L"Capture file could not be opened for reading.");
        return pv_status_open_failed;
    default:
        set_last_error(L"Unexpected file open result.");
        return pv_status_unexpected_error;
    }
}

} // namespace

extern "C" PV_API int pv_get_version(wchar_t* buffer, int buffer_length)
{
    return copy_to_buffer(pulseview::core::version(), buffer, buffer_length);
}

extern "C" PV_API pv_session_handle* pv_session_create()
{
    try {
        auto session = std::make_unique<pulseview::core::Session>();
        clear_last_error();
        return new pv_session_handle(std::move(session));
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to create a PulseView session.");
        return nullptr;
    }
    catch (...) {
        set_last_error(L"Unexpected native error while creating a PulseView session.");
        return nullptr;
    }
}

extern "C" PV_API void pv_session_destroy(pv_session_handle* session)
{
    delete session;
}

extern "C" PV_API int pv_session_open_file(pv_session_handle* session, const wchar_t* path)
{
    if (session == nullptr || session->session == nullptr) {
        set_last_error(L"Session handle is null.");
        return pv_status_invalid_argument;
    }

    if (path == nullptr || path[0] == L'\0') {
        set_last_error(L"Capture file path is empty.");
        return pv_status_invalid_argument;
    }

    try {
        return status_for_open_file_result(session->session->open_file(std::filesystem::path(path)));
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to open the capture file.");
        return pv_status_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected native error while opening the capture file.");
        return pv_status_unexpected_error;
    }
}

extern "C" PV_API int pv_session_get_signal_count(pv_session_handle* session)
{
    if (session == nullptr || session->session == nullptr) {
        set_last_error(L"Session handle is null.");
        return pv_status_invalid_argument;
    }

    clear_last_error();
    return session->session->signal_count();
}

extern "C" PV_API int pv_session_get_duration_seconds(pv_session_handle* session, double* duration_seconds)
{
    if (session == nullptr || session->session == nullptr || duration_seconds == nullptr) {
        set_last_error(L"Session handle and duration output pointer must not be null.");
        return pv_status_invalid_argument;
    }

    *duration_seconds = session->session->duration_seconds();
    clear_last_error();
    return pv_status_ok;
}

extern "C" PV_API int pv_session_query_digital_spans(
    pv_session_handle* session,
    double start_seconds,
    double seconds_per_pixel,
    float width_pixels,
    pv_digital_span* buffer,
    int buffer_length)
{
    if (session == nullptr || session->session == nullptr) {
        set_last_error(L"Session handle is null.");
        return pv_status_invalid_argument;
    }

    if (buffer_length < 0) {
        set_last_error(L"Digital span buffer length must not be negative.");
        return pv_status_invalid_argument;
    }

    try {
        const auto spans = session->session->query_digital_spans(
            pulseview::core::ViewportRequest {
                .start_seconds = start_seconds,
                .seconds_per_pixel = seconds_per_pixel,
                .width_pixels = width_pixels,
            });
        const auto required_count = static_cast<int>(spans.size());
        if (buffer == nullptr || buffer_length == 0) {
            clear_last_error();
            return required_count;
        }

        const auto count_to_copy = std::min(required_count, buffer_length);
        for (int index = 0; index < count_to_copy; ++index) {
            const auto& source = spans[static_cast<std::size_t>(index)];
            buffer[index] = pv_digital_span {
                .x0 = source.x0,
                .x1 = source.x1,
                .level = source.level,
                .edge_flags = source.edge_flags,
                .channel_index = source.channel_index,
                .reserved1 = 0,
            };
        }

        clear_last_error();
        return required_count;
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to query digital waveform spans.");
        return pv_status_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected native error while querying digital waveform spans.");
        return pv_status_unexpected_error;
    }
}

extern "C" PV_API int pv_get_last_error(wchar_t* buffer, int buffer_length)
{
    return copy_to_buffer(last_error, buffer, buffer_length);
}
