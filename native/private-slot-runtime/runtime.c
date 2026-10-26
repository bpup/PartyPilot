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
static HANDLE g_mapping;
static const uint64_t kMaxSampleAgeMs = 500; // Existing bridge command timeout.
static DWORD g_p5_index = 0xffffffffu;
static DWORD g_p6_index = 0xffffffffu;

static uint32_t header32(const volatile uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static pp_read_result read_private(DWORD index, pp_report *report) {
    if (g_frame == NULL) return PP_UNAVAILABLE;
    int player = index == g_p5_index ? 5 : index == g_p6_index ? 6 : 0;
    if (player == 0) return PP_UNAVAILABLE;
    return pp_read_pad(g_frame, PP_FRAME_SIZE, player,
                       GetTickCount64(), kMaxSampleAgeMs, report);
}

static int read_index(const wchar_t *name, DWORD *out) {
    wchar_t text[4] = {0};
    DWORD length = GetEnvironmentVariableW(name, text, 4);
    if (length != 1 || text[0] < L'0' || text[0] > L'3') return 0;
    *out = (DWORD)(text[0] - L'0');
    return 1;
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
    wchar_t name[128];
    swprintf(name, sizeof(name) / sizeof(name[0]),
             L"Local\\PartyPilot.PrivateSlots.%lu", GetCurrentProcessId());
    while (g_mapping == NULL) {
        g_mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
        if (g_mapping == NULL) Sleep(100);
    }
    g_frame = (volatile uint8_t *)MapViewOfFile(g_mapping, FILE_MAP_READ | FILE_MAP_WRITE,
                                                 0, 0, PP_FRAME_SIZE);
    if (g_frame == NULL) return 1;
    uint32_t connected = header32(g_frame + 16);
    int valid_header = header32(g_frame) == PP_MAGIC &&
        header32(g_frame + 4) == PP_VERSION &&
        header32(g_frame + 8) == PP_FRAME_SIZE &&
        (connected == PP_SLOT_P5 || connected == (PP_SLOT_P5 | PP_SLOT_P6));
    if (!valid_header || !read_index(L"PARTYPILOT_P5_LOCAL_INDEX", &g_p5_index) ||
        (connected == (PP_SLOT_P5 | PP_SLOT_P6) &&
         (!read_index(L"PARTYPILOT_P6_LOCAL_INDEX", &g_p6_index) ||
          g_p5_index == g_p6_index)) || MH_Initialize() != MH_OK) {
        if (valid_header) InterlockedExchange((volatile LONG *)(g_frame + 12), 3);
        return 1;
    }
    for (;;) {
        int hooks_ready = hook_loaded_xinput();
        connected = header32(g_frame + 16);
        if (hooks_ready && header32(g_frame) == PP_MAGIC &&
            header32(g_frame + 4) == PP_VERSION &&
            header32(g_frame + 8) == PP_FRAME_SIZE &&
            (connected == PP_SLOT_P5 || connected == (PP_SLOT_P5 | PP_SLOT_P6)) &&
            header32(g_frame + 12) == 1) {
            InterlockedExchange((volatile LONG *)(g_frame + 12), PP_STATUS_READY);
        }
        Sleep(100);
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
