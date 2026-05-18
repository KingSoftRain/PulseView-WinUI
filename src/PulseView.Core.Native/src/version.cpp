#include "pulseview/core/version.h"

namespace pulseview::core {

std::wstring_view version() noexcept
{
    return L"PulseView.NativeBridge 0.1.0";
}

} // namespace pulseview::core
