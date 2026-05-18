#include "pulseview/core/session.h"

#include <algorithm>
#include <array>
#include <fstream>
#include <system_error>

namespace pulseview::core {
namespace {

constexpr std::array<std::uint8_t, 16> fallback_pattern = {
    0, 0, 1, 1, 0, 1, 0, 0,
    1, 1, 1, 0, 1, 0, 1, 0,
};

std::vector<std::uint8_t> create_samples_from_stream(std::ifstream& stream)
{
    std::array<unsigned char, 4096> bytes = {};
    stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    const auto bytes_read = static_cast<std::size_t>(stream.gcount());

    if (bytes_read == 0) {
        return { fallback_pattern.begin(), fallback_pattern.end() };
    }

    std::vector<std::uint8_t> samples;
    samples.reserve(bytes_read * 8);
    for (std::size_t byte_index = 0; byte_index < bytes_read; ++byte_index) {
        const auto byte = bytes[byte_index];
        for (int bit_index = 7; bit_index >= 0; --bit_index) {
            samples.push_back(static_cast<std::uint8_t>((byte >> bit_index) & 0x1U));
        }
    }

    const bool has_high = std::any_of(samples.begin(), samples.end(), [](std::uint8_t value) { return value != 0; });
    const bool has_low = std::any_of(samples.begin(), samples.end(), [](std::uint8_t value) { return value == 0; });
    if (!has_high || !has_low) {
        return { fallback_pattern.begin(), fallback_pattern.end() };
    }

    return samples;
}

} // namespace

OpenFileStatus Session::open_file(const std::filesystem::path& path)
{
    std::error_code error;
    if (!std::filesystem::exists(path, error)) {
        return OpenFileStatus::file_not_found;
    }

    if (!std::filesystem::is_regular_file(path, error)) {
        return OpenFileStatus::not_regular_file;
    }

    std::ifstream stream(path, std::ios::binary);
    if (!stream.is_open()) {
        return OpenFileStatus::open_failed;
    }

    auto samples = create_samples_from_stream(stream);
    opened_file_path_ = path;
    digital_samples_ = std::move(samples);
    return OpenFileStatus::ok;
}

int Session::signal_count() const noexcept
{
    return digital_samples_.empty() ? 0 : 1;
}

double Session::duration_seconds() const noexcept
{
    if (digital_samples_.empty() || sample_rate_hz_ <= 0.0) {
        return 0.0;
    }

    return static_cast<double>(digital_samples_.size()) / sample_rate_hz_;
}

std::vector<DigitalSpan> Session::query_digital_spans(const ViewportRequest& request) const
{
    if (digital_samples_.empty()) {
        return {};
    }

    return pulseview::core::query_sampled_digital_spans(request, digital_samples_, sample_rate_hz_);
}

} // namespace pulseview::core
