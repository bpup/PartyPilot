#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xinput.h>

#include <stdio.h>
#include <string.h>

#include "MinHook.h"
#include "private_slots.h"

// The managed bridge owns the mapping. This DLL is loaded only into its
// assigned Client 3 process; it never discovers another game process.
static volatile uint8_t *g_frame;
static SRWLOCK g_frame_lock = SRWLOCK_INIT;
static const uint64_t kMaxSampleAgeMs = 500; // Existing bridge command timeout.
static uint32_t g_p5_index = PP_NO_INDEX;
static uint32_t g_p6_index = PP_NO_INDEX;

static uint32_t header32(const volatile uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static pp_read_result read_private(DWORD index, pp_report *report) {
    AcquireSRWLockShared(&g_frame_lock);
    if (g_frame == NULL) {
        ReleaseSRWLockShared(&g_frame_lock);
        return PP_UNAVAILABLE;
    }
    int player = index == g_p5_index ? 5 : index == g_p6_index ? 6 : 0;
    pp_read_result result = player == 0 ? PP_UNAVAILABLE :
        pp_read_pad(g_frame, PP_FRAME_SIZE, player,
                    GetTickCount64(), kMaxSampleAgeMs, report);
    ReleaseSRWLockShared(&g_frame_lock);
    return result;
}

static DWORD WINAPI hooked_get_state(DWORD index, XINPUT_STATE *state) {
    if (state == NULL) return ERROR_BAD_ARGUMENTS;
    pp_report report;
    pp_read_result result = read_private(index, &report);
    if (result == PP_UNAVAILABLE) return ERROR_DEVICE_NOT_CONNECTED;
    memset(state, 0, sizeof(*state));
    state->dwPacketNumber = report.packet;
    state->Gamepad.wButtons = report.buttons;
    state->Gamepad.bLeftTrigger = report.left_trigger;
    state->Gamepad.bRightTrigger = report.right_trigger;
    state->Gamepad.sThumbLX = report.left_x;
    state->Gamepad.sThumbLY = report.left_y;
    state->Gamepad.sThumbRX = report.right_x;
    state->Gamepad.sThumbRY = report.right_y;
    return ERROR_SUCCESS;
}

static DWORD WINAPI hooked_get_capabilities(DWORD index, DWORD flags,
                                              XINPUT_CAPABILITIES *capabilities) {
    (void)flags;
    if (capabilities == NULL) return ERROR_BAD_ARGUMENTS;
    pp_report report;
    if (read_private(index, &report) == PP_UNAVAILABLE)
        return ERROR_DEVICE_NOT_CONNECTED;
    memset(capabilities, 0, sizeof(*capabilities));
    capabilities->Type = XINPUT_DEVTYPE_GAMEPAD;
    capabilities->SubType = XINPUT_DEVSUBTYPE_GAMEPAD;
    capabilities->Gamepad.wButtons = 0xffff;
    capabilities->Gamepad.bLeftTrigger = 0xff;
    capabilities->Gamepad.bRightTrigger = 0xff;
    capabilities->Gamepad.sThumbLX = 32767;
    capabilities->Gamepad.sThumbLY = 32767;
    capabilities->Gamepad.sThumbRX = 32767;
    capabilities->Gamepad.sThumbRY = 32767;
    return ERROR_SUCCESS;
}

static DWORD WINAPI hooked_set_state(DWORD index, XINPUT_VIBRATION *vibration) {
    if (vibration == NULL) return ERROR_BAD_ARGUMENTS;
    pp_report report;
    return read_private(index, &report) == PP_UNAVAILABLE
        ? ERROR_DEVICE_NOT_CONNECTED : ERROR_SUCCESS;
}

static int hook_export(HMODULE module, const char *name, LPVOID detour) {
    FARPROC target = GetProcAddress(module, name);
    if (target == NULL) return 0;
    MH_STATUS status = MH_CreateHook((LPVOID)target, detour, NULL);
    if (status != MH_OK && status != MH_ERROR_ALREADY_CREATED) return 0;
    status = MH_EnableHook((LPVOID)target);
    return status == MH_OK || status == MH_ERROR_ENABLED;
}

static int hook_loaded_xinput(void) {
    static const wchar_t *names[] = {
        L"xinput1_4.dll", L"xinput1_3.dll", L"xinput9_1_0.dll"
    };
    int loaded = 0;
    int all_hooked = 1;
    for (size_t i = 0; i < sizeof(names) / sizeof(names[0]); ++i) {
        HMODULE module = GetModuleHandleW(names[i]);
        if (module == NULL) continue;
        ++loaded;
        int state = hook_export(module, "XInputGetState", (LPVOID)hooked_get_state);
        int capabilities = hook_export(module, "XInputGetCapabilities",
                                       (LPVOID)hooked_get_capabilities);
        int vibration = hook_export(module, "XInputSetState", (LPVOID)hooked_set_state);
        if (!state || !capabilities || !vibration) all_hooked = 0;
    }
    return loaded > 0 && all_hooked;
}

static DWORD WINAPI worker(void *unused) {
    (void)unused;
    if (MH_Initialize() != MH_OK) return 1;
    for (;;) {
        wchar_t name[128];
        swprintf(name, sizeof(name) / sizeof(name[0]),
                 L"Local\\PartyPilot.PrivateSlots.%lu", GetCurrentProcessId());
        HANDLE mapping = NULL;
        while (mapping == NULL) {
            mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
            if (mapping == NULL) Sleep(100);
        }
        volatile uint8_t *frame = (volatile uint8_t *)MapViewOfFile(
            mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, PP_FRAME_SIZE);
        if (frame == NULL) { CloseHandle(mapping); return 1; }
        swprintf(name, sizeof(name) / sizeof(name[0]),
                 L"Local\\PartyPilot.PrivateSlots.Config.%lu", GetCurrentProcessId());
        HANDLE config_mapping = NULL;
        while (config_mapping == NULL) {
            config_mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, name);
            if (config_mapping == NULL) Sleep(100);
        }
        const volatile uint8_t *config = (const volatile uint8_t *)MapViewOfFile(
            config_mapping, FILE_MAP_READ, 0, 0, PP_CONFIG_SIZE);
        if (config == NULL) {
            InterlockedExchange((volatile LONG *)(frame + 12), PP_STATUS_FAILED);
            UnmapViewOfFile((const void *)frame);
            CloseHandle(mapping);
            CloseHandle(config_mapping);
            return 1;
        }
        uint32_t connected = header32(frame + 16);
        uint32_t p5_index = PP_NO_INDEX;
        uint32_t p6_index = PP_NO_INDEX;
        int valid = header32(frame) == PP_MAGIC &&
            header32(frame + 4) == PP_VERSION &&
            header32(frame + 8) == PP_FRAME_SIZE &&
            (connected == PP_SLOT_P5 || connected == (PP_SLOT_P5 | PP_SLOT_P6)) &&
            pp_read_config(config, PP_CONFIG_SIZE, connected,
                           &p5_index, &p6_index);
        if (valid) {
            AcquireSRWLockExclusive(&g_frame_lock);
            g_p5_index = p5_index;
            g_p6_index = p6_index;
            g_frame = frame;
            ReleaseSRWLockExclusive(&g_frame_lock);
        } else {
            InterlockedExchange((volatile LONG *)(frame + 12), PP_STATUS_FAILED);
        }
        while (header32(frame + 12) != PP_STATUS_STOPPING) {
            if (valid && hook_loaded_xinput() &&
                header32(frame) == PP_MAGIC &&
                header32(frame + 4) == PP_VERSION &&
                header32(frame + 8) == PP_FRAME_SIZE &&
                header32(frame + 16) == connected &&
                header32(frame + 12) == PP_STATUS_STARTING)
                InterlockedExchange((volatile LONG *)(frame + 12), PP_STATUS_READY);
            Sleep(100);
        }
        AcquireSRWLockExclusive(&g_frame_lock);
        g_frame = NULL;
        g_p5_index = PP_NO_INDEX;
        g_p6_index = PP_NO_INDEX;
        UnmapViewOfFile((const void *)frame);
        ReleaseSRWLockExclusive(&g_frame_lock);
        UnmapViewOfFile((const void *)config);
        CloseHandle(config_mapping);
        CloseHandle(mapping);
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        HANDLE thread = CreateThread(NULL, 0, worker, NULL, 0, NULL);
        if (thread == NULL) return FALSE;
        CloseHandle(thread);
    }
    return TRUE;
}
