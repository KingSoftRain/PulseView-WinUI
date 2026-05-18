#pragma once

#include <string_view>

namespace pulseview::core {

[[nodiscard]] std::wstring_view version() noexcept;

} // namespace pulseview::core
