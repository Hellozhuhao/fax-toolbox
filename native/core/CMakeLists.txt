cmake_minimum_required(VERSION 3.22.1)
project(toolbox_core LANGUAGES C CXX)

add_library(toolbox_core_jni SHARED
  tb_core.cpp
  ../../apps/android-host/app/src/main/cpp/toolbox_jni.cpp
)
target_include_directories(toolbox_core_jni PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_features(toolbox_core_jni PRIVATE cxx_std_17)
target_compile_options(toolbox_core_jni PRIVATE -Wall -Wextra -Werror)
