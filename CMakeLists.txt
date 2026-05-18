cmake_minimum_required(VERSION 3.25)

project(PulseViewWinUI LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

enable_testing()

add_subdirectory(src/PulseView.Core.Native)
add_subdirectory(src/PulseView.NativeBridge)
add_subdirectory(src/PulseView.Rendering.Native)
add_subdirectory(src/PulseView.Core.Tests)
