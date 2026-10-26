#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xinput.h>

#include <stdio.h>
#include <stdint.h>
#include <string.h>

#include "private_slots.h"

static void put16(uint8_t *p, uint16_t v) { p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); }
static void put32(uint8_t *p, uint32_t v) { put16(p, (uint16_t)v); put16(p + 2, (uint16_t)(v >> 16)); }
static void put64(uint8_t *p, uint64_t v) { put32(p, (uint32_t)v); put32(p + 4, (uint32_t)(v >> 32)); }

typedef DWORD (WINAPI *get_state_fn)(DWORD, XINPUT_STATE *);
typedef DWORD (WINAPI *get_capabilities_fn)(DWORD, DWORD, XINPUT_CAPABILITIES *);
typedef DWORD (WINAPI *set_state_fn)(DWORD, XINPUT_VIBRATION *);

int main(void) {
    HMODULE xinput = LoadLibraryW(L"xinput1_4.dll");
    if (xinput == NULL) { fputs("XInput 1.4 unavailable.\n", stderr); return 2; }
    get_state_fn get_state = (get_state_fn)GetProcAddress(xinput, "XInputGetState");
    get_capabilities_fn get_capabilities = (get_capabilities_fn)GetProcAddress(xinput, "XInputGetCapabilities");
    set_state_fn set_state = (set_state_fn)GetProcAddress(xinput, "XInputSetState");
    if (get_state == NULL || get_capabilities == NULL || set_state == NULL) return 2;
    wchar_t name[128];
    swprintf(name, sizeof(name) / sizeof(name[0]),
             L"Local\\PartyPilot.PrivateSlots.%lu", GetCurrentProcessId());
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0,
                                        PP_FRAME_SIZE, name);
    if (mapping == NULL) return 2;
    uint8_t *frame = (uint8_t *)MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE,
                                              0, 0, PP_FRAME_SIZE);
    if (frame == NULL) return 2;
    swprintf(name, sizeof(name) / sizeof(name[0]),
             L"Local\\PartyPilot.PrivateSlots.Config.%lu", GetCurrentProcessId());
    HANDLE config_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0,
                                               PP_CONFIG_SIZE, name);
    if (config_mapping == NULL) return 2;
    uint8_t *config = (uint8_t *)MapViewOfFile(config_mapping, FILE_MAP_READ | FILE_MAP_WRITE,
                                               0, 0, PP_CONFIG_SIZE);
    if (config == NULL) return 2;
    memset(config, 0, PP_CONFIG_SIZE);
    put32(config, PP_CONFIG_MAGIC);
    put32(config + 4, PP_VERSION);
    put32(config + 8, PP_CONFIG_SIZE);
    put32(config + 12, 2); // Explicit test indices, not a game mapping.
    put32(config + 16, 0);
    memset(frame, 0, PP_FRAME_SIZE);
    put32(frame, PP_MAGIC);
    put32(frame + 4, PP_VERSION);
    put32(frame + 8, PP_FRAME_SIZE);
    put32(frame + 12, 1);
    put32(frame + 16, PP_SLOT_P5 | PP_SLOT_P6);
    put32(frame + 20, 2);
    put32(frame + 24, 1);
    put16(frame + 28, 0x1000);
    put64(frame + 40, GetTickCount64());
    put32(frame + 48, 2);
    put32(frame + 52, 2);
    put16(frame + 56, 0x0020);
    put64(frame + 68, GetTickCount64());
    HMODULE runtime = LoadLibraryW(L"PartyPilot.PrivateSlots.dll");
    if (runtime == NULL) { fputs("Private runtime did not load.\n", stderr); return 2; }
    int ready = 0;
    for (int i = 0; i < 100; ++i) {
        if (InterlockedCompareExchange((volatile LONG *)(frame + 12), 0, 0) ==
            PP_STATUS_READY) { ready = 1; break; }
        Sleep(50);
    }
    if (!ready) { fputs("XInput hook did not become ready.\n", stderr); return 1; }
    XINPUT_STATE state = {0};
    XINPUT_CAPABILITIES caps = {0};
    XINPUT_VIBRATION vibration = {0};
    if (get_state(2, &state) != ERROR_SUCCESS || state.Gamepad.wButtons != 0x1000 ||
        get_state(0, &state) != ERROR_SUCCESS || state.Gamepad.wButtons != 0x0020 ||
        get_state(1, &state) != ERROR_DEVICE_NOT_CONNECTED ||
        get_capabilities(2, 0, &caps) != ERROR_SUCCESS ||
        set_state(0, &vibration) != ERROR_SUCCESS) {
        fputs("Private XInput routing failed.\n", stderr);
        return 1;
    }
    put64(frame + 40, GetTickCount64() - 501);
    if (get_state(2, &state) != ERROR_SUCCESS || state.Gamepad.wButtons != 0) {
        fputs("Stale input was not neutralized.\n", stderr);
        return 1;
    }
    InterlockedExchange((volatile LONG *)(frame + 12), 5);
    if (get_state(2, &state) != ERROR_DEVICE_NOT_CONNECTED) {
        fputs("Stopped private slot remained connected.\n", stderr);
        return 1;
    }
    puts("Private XInput runtime smoke passed.");
    return 0;
}
