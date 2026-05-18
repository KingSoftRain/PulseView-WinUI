#pragma once

#include <filesystem>
#include <vector>

#include "pulseview/core/viewport.h"

namespace pulseview::core {

enum class OpenFileStatus {
    ok,
    file_not_found,
    not_regular_file,
    open_failed,
};

class Session final {
public:
    [[nodiscard]] OpenFileStatus open_file(const std::filesystem::path& path);
    [[nodiscard]] int signal_count() const noexcept;
    [[nodiscard]] double duration_seconds() const noexcept;
    [[nodiscard]] std::vector<DigitalSpan> query_digital_spans(const ViewportRequest& request) const;

private:
    std::filesystem::path opened_file_path_;
    double sample_rate_hz_ = 100'000.0;
    std::vector<std::uint8_t> digital_samples_;
};

} // namespace pulseview::core
