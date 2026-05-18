add_library(PulseView.NativeBridge SHARED
    src/native_bridge.cpp
)

target_include_directories(PulseView.NativeBridge
    PUBLIC
        include
)

target_link_libraries(PulseView.NativeBridge
    PRIVATE
        PulseView.Core.Native
)

target_compile_definitions(PulseView.NativeBridge
    PRIVATE
        PV_NATIVEBRIDGE_EXPORTS
)
