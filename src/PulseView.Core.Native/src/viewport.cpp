#include "pulseview/core/viewport.h"

#include <algorithm>
#include <array>
#include <cmath>

namespace pulseview::core {
namespace {

constexpr std::array<std::uint8_t, 16> pattern = {
    0, 0, 1, 1, 0, 1, 0, 0,
    1, 1, 1, 0, 1, 0, 1, 0,
};

std::uint8_t pattern_level(std::span<const std::uint8_t> pattern_levels, long long bit_index)
{
    const auto size = static_cast<long long>(pattern_levels.size());
    const auto normalized = ((bit_index % size) + size) % size;
    return pattern_levels[static_cast<std::size_t>(normalized)] == 0 ? 0 : 1;
}

bool is_valid_request(const ViewportRequest& request)
{
    return std::isfinite(request.start_seconds)
        && std::isfinite(request.seconds_per_pixel)
        && std::isfinite(request.width_pixels)
        && request.seconds_per_pixel > 0.0
        && request.width_pixels > 0.0F;
}

} // namespace

std::vector<DigitalSpan> query_digital_spans(
    const ViewportRequest& request,
    std::span<const std::uint8_t> pattern_levels,
    double bit_seconds)
{
    if (!is_valid_request(request) || pattern_levels.empty() || !std::isfinite(bit_seconds) || bit_seconds <= 0.0) {
        return {};
    }

    const double start_seconds = std::max(0.0, request.start_seconds);
    const double seconds_per_pixel = std::clamp(request.seconds_per_pixel, 1.0e-9, 1.0);
    const float width_pixels = std::max(1.0F, request.width_pixels);
    const double visible_end = start_seconds + seconds_per_pixel * static_cast<double>(width_pixels);
    const auto first_bit = static_cast<long long>(std::floor(start_seconds / bit_seconds)) - 1;
    const auto last_bit = static_cast<long long>(std::ceil(visible_end / bit_seconds)) + 1;

    std::vector<DigitalSpan> spans;
    spans.reserve(static_cast<std::size_t>(std::max(0LL, last_bit - first_bit + 1)));

    std::uint8_t previous_level = pattern_level(pattern_levels, first_bit - 1);
    for (long long bit = first_bit; bit <= last_bit; ++bit) {
        const std::uint8_t level = pattern_level(pattern_levels, bit);
        const double start_time = static_cast<double>(bit) * bit_seconds;
        const double end_time = static_cast<double>(bit + 1) * bit_seconds;
        const float x0 = static_cast<float>((start_time - start_seconds) / seconds_per_pixel);
        const float x1 = static_cast<float>((end_time - start_seconds) / seconds_per_pixel);
        const float clipped_x0 = std::clamp(x0, 0.0F, width_pixels);
        const float clipped_x1 = std::clamp(x1, 0.0F, width_pixels);

        if (clipped_x1 >= 0.0F && clipped_x0 <= width_pixels && clipped_x1 > clipped_x0) {
            std::uint8_t edge_flags = digital_edge_none;
            if (bit > first_bit && x0 >= 0.0F && x0 <= width_pixels && level != previous_level) {
                edge_flags = level != 0 ? digital_edge_rising : digital_edge_falling;
            }

            spans.push_back(DigitalSpan {
                .x0 = clipped_x0,
                .x1 = clipped_x1,
                .level = level,
                .edge_flags = edge_flags,
            });
        }

        previous_level = level;
    }

    return spans;
}

std::vector<DigitalSpan> query_synthetic_digital_spans(const ViewportRequest& request)
{
    return query_digital_spans(request, pattern);
}

std::vector<DigitalSpan> query_sampled_digital_spans(
    const ViewportRequest& request,
    std::span<const std::uint8_t> samples,
    double sample_rate_hz)
{
    if (!is_valid_request(request) || samples.empty() || !std::isfinite(sample_rate_hz) || sample_rate_hz <= 0.0) {
        return {};
    }

    const double start_seconds = std::max(0.0, request.start_seconds);
    const double seconds_per_pixel = std::clamp(request.seconds_per_pixel, 1.0e-9, 1.0);
    const float width_pixels = std::max(1.0F, request.width_pixels);
    const double sample_period = 1.0 / sample_rate_hz;
    const double visible_end = start_seconds + seconds_per_pixel * static_cast<double>(width_pixels);

    auto first_sample = static_cast<long long>(std::floor(start_seconds * sample_rate_hz)) - 1;
    auto last_sample = static_cast<long long>(std::ceil(visible_end * sample_rate_hz)) + 1;
    first_sample = std::max(0LL, first_sample);
    last_sample = std::min(last_sample, static_cast<long long>(samples.size()) - 1);

    if (last_sample < first_sample) {
        return {};
    }

    std::vector<DigitalSpan> spans;
    spans.reserve(static_cast<std::size_t>(last_sample - first_sample + 1));

    std::uint8_t current_level = samples[static_cast<std::size_t>(first_sample)] == 0 ? 0 : 1;
    double run_start_time = static_cast<double>(first_sample) * sample_period;
    long long run_start_sample = first_sample;

    auto append_run = [&](long long run_end_sample, std::uint8_t level, std::uint8_t edge_flags) {
        const double run_end_time = static_cast<double>(run_end_sample + 1) * sample_period;
        const float x0 = static_cast<float>((run_start_time - start_seconds) / seconds_per_pixel);
        const float x1 = static_cast<float>((run_end_time - start_seconds) / seconds_per_pixel);
        const float clipped_x0 = std::clamp(x0, 0.0F, width_pixels);
        const float clipped_x1 = std::clamp(x1, 0.0F, width_pixels);
        if (clipped_x1 > clipped_x0) {
            spans.push_back(DigitalSpan {
                .x0 = clipped_x0,
                .x1 = clipped_x1,
                .level = level,
                .edge_flags = edge_flags,
            });
        }
    };

    std::uint8_t pending_edge = digital_edge_none;
    for (long long sample_index = first_sample + 1; sample_index <= last_sample; ++sample_index) {
        const std::uint8_t level = samples[static_cast<std::size_t>(sample_index)] == 0 ? 0 : 1;
        if (level == current_level) {
            continue;
        }

        append_run(sample_index - 1, current_level, pending_edge);
        pending_edge = level != 0 ? digital_edge_rising : digital_edge_falling;
        current_level = level;
        run_start_sample = sample_index;
        run_start_time = static_cast<double>(run_start_sample) * sample_period;
    }

    append_run(last_sample, current_level, pending_edge);
    return spans;
}

} // namespace pulseview::core
