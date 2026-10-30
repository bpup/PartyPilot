cmake_minimum_required(VERSION 3.20)
project(PartyPilotProbeFixture LANGUAGES CXX)
add_executable(PathOfExile_x64 main.cpp)
target_compile_features(PathOfExile_x64 PRIVATE cxx_std_17)
