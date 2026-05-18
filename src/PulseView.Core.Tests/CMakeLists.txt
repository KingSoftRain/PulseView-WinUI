add_executable(PulseView.Core.Tests
    src/native_bridge_tests.cpp
)

target_link_libraries(PulseView.Core.Tests
    PRIVATE
        PulseView.Core.Native
        PulseView.NativeBridge
)

add_custom_command(TARGET PulseView.Core.Tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        $<TARGET_FILE:PulseView.NativeBridge>
        $<TARGET_FILE_DIR:PulseView.Core.Tests>
)

add_test(NAME PulseView.Core.Tests COMMAND PulseView.Core.Tests)
