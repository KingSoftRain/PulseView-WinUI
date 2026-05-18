add_library(PulseView.Core.Native STATIC
    src/session.cpp
    src/viewport.cpp
    src/version.cpp
)

target_include_directories(PulseView.Core.Native
    PUBLIC
        include
)
