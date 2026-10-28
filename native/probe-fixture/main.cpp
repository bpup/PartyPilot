// Synthetic memory layout for a cross-process probe smoke check. This is
// deliberately not game code or a source of real PoE2 offsets.
#include <chrono>
#include <cstdint>
#include <cstring>
#include <limits>
#include <string>
#include <thread>

namespace {
alignas(8) volatile unsigned char signature[64] = {
    0x83, 0, 0, 0, 0x8B, 0, 0x33, 0, 0, 0x39, 0x05,
    0, 0, 0, 0, 0x0F, 0x85, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xE8};
alignas(8) volatile std::uintptr_t game_state_pointer = 0;
alignas(8) unsigned char game_state[4096] = {};
alignas(8) unsigned char ingame_state[4096] = {};
alignas(8) unsigned char area_instance[4096] = {};
alignas(8) unsigned char entity[2][256] = {};
alignas(8) unsigned char details[2][256] = {};
alignas(8) unsigned char lookup[2][256] = {};
alignas(8) unsigned char render[2][512] = {};
alignas(8) std::uintptr_t local_players[2] = {};
alignas(8) std::uintptr_t components[2][1] = {};
alignas(8) unsigned char names[2][16] = {};
alignas(8) char render_name[64] = "Render";
alignas(8) char16_t paths[2][64] = {
    u"Metadata/Characters/FixtureOne", u"Metadata/Characters/FixtureTwo"};

template <class T> void put(unsigned char* data, std::size_t offset, T value) {
    std::memcpy(data + offset, &value, sizeof(value));
}

void vector_header(unsigned char* data, std::size_t offset,
                   std::uintptr_t first, std::size_t count, std::size_t capacity,
                   std::size_t stride = sizeof(std::uintptr_t)) {
    put(data, offset, first);
    put(data, offset + 8, first + count * stride);
    put(data, offset + 16, first + capacity * stride);
}

bool initialize() {
    const auto rip = reinterpret_cast<std::intptr_t>(signature + 15);
    const auto storage = reinterpret_cast<std::intptr_t>(&game_state_pointer);
    const auto delta = storage - rip;
    if (delta < std::numeric_limits<std::int32_t>::min() ||
        delta > std::numeric_limits<std::int32_t>::max()) return false;
    const auto displacement = static_cast<std::int32_t>(delta);
    const auto* bytes = reinterpret_cast<const unsigned char*>(&displacement);
    for (std::size_t index = 0; index < sizeof(displacement); ++index)
        signature[11 + index] = bytes[index];

    put(game_state, 136, reinterpret_cast<std::uintptr_t>(ingame_state));
    put(ingame_state, 656, reinterpret_cast<std::uintptr_t>(area_instance));
    put(area_instance, 192, std::int32_t{12});
    put(area_instance, 196, std::int32_t{14});
    local_players[0] = reinterpret_cast<std::uintptr_t>(entity[0]);
    local_players[1] = reinterpret_cast<std::uintptr_t>(entity[1]);
    vector_header(area_instance, 1408 + 72,
                  reinterpret_cast<std::uintptr_t>(local_players), 2, 2);
    for (int index = 0; index < 2; ++index) {
        put(entity[index], 8, reinterpret_cast<std::uintptr_t>(details[index]));
        put(details[index], 8, reinterpret_cast<std::uintptr_t>(paths[index]));
        const auto length = static_cast<std::int64_t>(std::char_traits<char16_t>::length(paths[index]));
        put(details[index], 24, length);
        put(details[index], 32, length + 1);
        put(details[index], 40, reinterpret_cast<std::uintptr_t>(lookup[index]));
        put(names[index], 0, reinterpret_cast<std::uintptr_t>(render_name));
        put(names[index], 8, std::int32_t{0});
        vector_header(lookup[index], 40,
                      reinterpret_cast<std::uintptr_t>(names[index]), 1, 1, 16);
        components[index][0] = reinterpret_cast<std::uintptr_t>(render[index]);
        vector_header(entity[index], 16,
                      reinterpret_cast<std::uintptr_t>(components[index]), 1, 1);
        put(render[index], 8, reinterpret_cast<std::uintptr_t>(entity[index]));
        put(render[index], 312, static_cast<float>(100 + index * 20));
        put(render[index], 316, static_cast<float>(200 + index * 20));
        put(render[index], 320, float{0});
    }
    return true;
}
} // namespace

int main() {
    if (!initialize()) return 2;
    // The probe must recover after discovering a process before its state is ready.
    std::this_thread::sleep_for(std::chrono::seconds(3));
    game_state_pointer = reinterpret_cast<std::uintptr_t>(game_state);
    std::this_thread::sleep_for(std::chrono::seconds(30));
    return 0;
}
