#pragma once

#include <cstdint>
#include <span>
#include <vector>

namespace pulseview::core {

enum DigitalEdgeFlags : std::uint8_t {
    digital_edge_none = 0,
    digital_edge_rising = 1,
    digital_edge_falling = 2,
};

struct DigitalSpan {
    float x0;
    float x1;
    std::uint8_t level;
    std::uint8_t edge_flags;
    std::uint8_t channel_index = 0;
};

struct ViewportRequest {
    double start_seconds;
    double seconds_per_pixel;
    float width_pixels;
};

[[nodiscard]] std::vector<DigitalSpan> query_digital_spans(
    const ViewportRequest& request,
    std::span<const std::uint8_t> pattern_levels,
    double bit_seconds = 200.0e-6);

[[nodiscard]] std::vector<DigitalSpan> query_sampled_digital_spans(
    const ViewportRequest& request,
    std::span<const std::uint8_t> samples,
    double sample_rate_hz);

[[nodiscard]] std::vector<DigitalSpan> query_synthetic_digital_spans(const ViewportRequest& request);

} // namespace pulseview::core
