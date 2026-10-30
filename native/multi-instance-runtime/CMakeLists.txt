cmake_minimum_required(VERSION 3.21)
project(PartyPilotMultiInstance C)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Multi-instance runtime requires Windows x64")
endif()

set(MINHOOK "${CMAKE_CURRENT_SOURCE_DIR}/../private-slot-runtime/vendor/minhook")
add_library(PartyPilotMultiInstance SHARED
  runtime.c
  "${MINHOOK}/src/buffer.c"
  "${MINHOOK}/src/hook.c"
  "${MINHOOK}/src/trampoline.c"
  "${MINHOOK}/src/hde/hde64.c")
target_include_directories(PartyPilotMultiInstance PRIVATE
  "${MINHOOK}/include" "${MINHOOK}/src" "${MINHOOK}/src/hde")
set_target_properties(PartyPilotMultiInstance PROPERTIES
  PREFIX "" OUTPUT_NAME "PartyPilot.MultiInstance"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin")

add_executable(PartyPilotMultiInstanceSmoke windows_smoke.c)
set_target_properties(PartyPilotMultiInstanceSmoke PROPERTIES
  OUTPUT_NAME "PartyPilot.MultiInstance.Smoke"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin")

enable_testing()
add_test(NAME multi_instance_runtime
  COMMAND $<TARGET_FILE:PartyPilotMultiInstanceSmoke>)
