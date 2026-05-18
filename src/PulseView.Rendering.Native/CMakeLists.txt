add_library(PulseView.Rendering.Native SHARED
    src/renderer.cpp
)

file(GLOB _pv_winui_dxinterop_headers
    "$ENV{USERPROFILE}/.nuget/packages/microsoft.windowsappsdk.winui/*/include/microsoft.ui.xaml.media.dxinterop.h"
)
if(NOT _pv_winui_dxinterop_headers)
    message(FATAL_ERROR "microsoft.ui.xaml.media.dxinterop.h was not found. Restore the WinUI app project before building native rendering.")
endif()

list(SORT _pv_winui_dxinterop_headers)
list(REVERSE _pv_winui_dxinterop_headers)
list(GET _pv_winui_dxinterop_headers 0 _pv_winui_dxinterop_header)
get_filename_component(_pv_winui_include_dir "${_pv_winui_dxinterop_header}" DIRECTORY)

target_include_directories(PulseView.Rendering.Native
    PUBLIC
        include
    PRIVATE
        "${_pv_winui_include_dir}"
)

target_compile_definitions(PulseView.Rendering.Native
    PRIVATE
        PV_RENDERING_EXPORTS
)

target_link_libraries(PulseView.Rendering.Native
    PRIVATE
        PulseView.Core.Native
        d2d1
        d3d11
        dxgi
)
