#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xinput.h>

#include <stdio.h>
#include <stdint.h>

typedef DWORD (WINAPI *get_state_fn)(DWORD, XINPUT_STATE *);

int main(void) {
    HMODULE xinput = LoadLibraryW(L"xinput1_4.dll");
    if (xinput == NULL) return 2;
    get_state_fn get_state = (get_state_fn)GetProcAddress(xinput, "XInputGetState");
    if (get_state == NULL) return 2;
    HWND window = CreateWindowExW(0, L"STATIC", L"PartyPilot attach smoke",
                                  WS_OVERLAPPED, 0, 0, 32, 32, NULL, NULL,
                                  GetModuleHandleW(NULL), NULL);
    if (window == NULL) return 2;
    printf("WINDOW %llu\n", (unsigned long long)(uintptr_t)window);
    fflush(stdout);

    DWORD last_status[2] = {0xffffffffu, 0xffffffffu};
    WORD last_buttons[2] = {0xffffu, 0xffffu};
    for (;;) {
        MSG message;
        while (PeekMessageW(&message, NULL, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        for (DWORD slot = 0; slot < 2; ++slot) {
            XINPUT_STATE state = {0};
            DWORD status = get_state(slot, &state);
            WORD buttons = status == ERROR_SUCCESS ? state.Gamepad.wButtons : 0;
            if (status != last_status[slot] || buttons != last_buttons[slot]) {
                printf("PAD %lu %lu %u\n", (unsigned long)slot,
                       (unsigned long)status, (unsigned)buttons);
                fflush(stdout);
                last_status[slot] = status;
                last_buttons[slot] = buttons;
            }
        }
        Sleep(20);
    }
}
