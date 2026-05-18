#include "pulseview/native_bridge.h"
#include "pulseview/core/viewport.h"

#include <algorithm>
#include <cassert>
#include <cwchar>
#include <fstream>
#include <string>
#include <vector>

int main()
{
    wchar_t version_buffer[64] = {};
    const int version_length = pv_get_version(version_buffer, 64);

    assert(version_length > 0);
    assert(std::wstring(version_buffer) == L"PulseView.NativeBridge 0.1.0");

    pv_session_handle* session = pv_session_create();
    assert(session != nullptr);
    assert(pv_session_get_signal_count(session) == 0);

    assert(pv_session_open_file(session, L"missing-file.sr") == pv_status_file_not_found);

    wchar_t error_buffer[128] = {};
    assert(pv_get_last_error(error_buffer, 128) > 0);
    assert(std::wstring(error_buffer) == L"Capture file was not found.");

    {
        std::ofstream stream("native-open-file-test.sr", std::ios::binary);
        stream << "pulseview test capture";
    }

    assert(pv_session_open_file(session, L"native-open-file-test.sr") == pv_status_ok);
    assert(pv_get_last_error(error_buffer, 128) == 0);
    assert(pv_session_get_signal_count(session) == 1);
    double duration_seconds = 0.0;
    assert(pv_session_get_duration_seconds(session, &duration_seconds) == pv_status_ok);
    assert(duration_seconds > 0.0);

    const int required_span_count = pv_session_query_digital_spans(session, 0.0, 10.0e-6, 640.0F, nullptr, 0);
    assert(required_span_count > 0);

    std::vector<pv_digital_span> bridge_spans(static_cast<std::size_t>(required_span_count));
    assert(pv_session_query_digital_spans(
        session,
        0.0,
        10.0e-6,
        640.0F,
        bridge_spans.data(),
        required_span_count) == required_span_count);
    assert(bridge_spans.front().x0 >= 0.0F);
    assert(bridge_spans.back().x1 <= 640.0F);

    pv_session_destroy(session);

    const auto spans = pulseview::core::query_synthetic_digital_spans(
        pulseview::core::ViewportRequest {
            .start_seconds = 0.0,
            .seconds_per_pixel = 10.0e-6,
            .width_pixels = 640.0F,
        });
    assert(!spans.empty());
    assert(spans.front().x0 >= 0.0F);
    assert(spans.back().x1 <= 640.0F);

    const auto has_edge = std::any_of(spans.begin(), spans.end(), [](const pulseview::core::DigitalSpan& span) {
        return span.edge_flags != pulseview::core::digital_edge_none;
    });
    assert(has_edge);

    return 0;
}
