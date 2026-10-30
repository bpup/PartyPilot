cmake_minimum_required(VERSION 3.21)
project(PartyPilotPrivateSlots C)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Private slot runtime requires Windows x64")
endif()

set(MINHOOK "${CMAKE_CURRENT_SOURCE_DIR}/vendor/minhook")
add_library(PartyPilotPrivateSlots SHARED
  private_slots.c
  runtime.c
  "${MINHOOK}/src/buffer.c"
  "${MINHOOK}/src/hook.c"
  "${MINHOOK}/src/trampoline.c"
  "${MINHOOK}/src/hde/hde64.c")
target_include_directories(PartyPilotPrivateSlots PRIVATE
  "${MINHOOK}/include" "${MINHOOK}/src" "${MINHOOK}/src/hde")
if(MSVC)
  target_compile_options(PartyPilotPrivateSlots PRIVATE /experimental:c11atomics)
endif()
set_target_properties(PartyPilotPrivateSlots PROPERTIES
  PREFIX "" OUTPUT_NAME "PartyPilot.PrivateSlots"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin")

add_executable(PartyPilotPrivateSlotsSmoke windows_smoke.c)
set_target_properties(PartyPilotPrivateSlotsSmoke PROPERTIES
  OUTPUT_NAME "PartyPilot.PrivateSlots.Smoke"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin")

# A separate process with a PoE2-shaped executable name. It contains no game
# code; the managed smoke attaches to it through the real Client 3 path.
add_executable(PartyPilotRemoteAttachTarget remote_attach_target.c)
set_target_properties(PartyPilotRemoteAttachTarget PROPERTIES
  OUTPUT_NAME "PathOfExile_x64"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin")
target_link_libraries(PartyPilotRemoteAttachTarget PRIVATE user32)

enable_testing()
add_test(NAME private_slot_runtime
  COMMAND $<TARGET_FILE:PartyPilotPrivateSlotsSmoke>)
