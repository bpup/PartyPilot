#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>

#include "MinHook.h"

typedef HANDLE (WINAPI *create_mutex_w)(LPSECURITY_ATTRIBUTES, BOOL, LPCWSTR);
typedef HANDLE (WINAPI *create_mutex_ex_w)(LPSECURITY_ATTRIBUTES, LPCWSTR, DWORD, DWORD);
typedef HANDLE (WINAPI *open_mutex_w)(DWORD, BOOL, LPCWSTR);

static create_mutex_w original_create;
static create_mutex_ex_w original_create_ex;
static open_mutex_w original_open;

// Stable within one game process, distinct across different process IDs.
// Unnamed mutexes are passed through without alteration.
static LPCWSTR isolated_name(LPCWSTR original, wchar_t output[96]) {
    if (original == NULL) return NULL;
    uint64_t hash = UINT64_C(14695981039346656037);
    unsigned int length = 0;
    while (original[length] != L'\0' && length < 32767u) {
        uint16_t unit = (uint16_t)original[length++];
        hash ^= (uint8_t)unit;
        hash *= UINT64_C(1099511628211);
        hash ^= (uint8_t)(unit >> 8);
        hash *= UINT64_C(1099511628211);
    }
    if (length == 32767u) return original;
    static const wchar_t prefix[] = L"Local\\PartyPilot.GameMutex.";
    unsigned int at = 0;
    while (prefix[at] != L'\0') { output[at] = prefix[at]; ++at; }
    DWORD pid = GetCurrentProcessId();
    wchar_t digits[10];
    unsigned int digit_count = 0;
    do { digits[digit_count++] = (wchar_t)(L'0' + pid % 10u); pid /= 10u; } while (pid != 0);
    while (digit_count > 0) output[at++] = digits[--digit_count];
    output[at++] = L'.';
    static const wchar_t hex[] = L"0123456789abcdef";
    for (int shift = 60; shift >= 0; shift -= 4)
        output[at++] = hex[(hash >> shift) & 15u];
    output[at] = L'\0';
    return output;
}

static HANDLE WINAPI hooked_create(LPSECURITY_ATTRIBUTES attributes, BOOL initial, LPCWSTR name) {
    wchar_t rewritten[96];
    return original_create(attributes, initial, isolated_name(name, rewritten));
}

static HANDLE WINAPI hooked_create_ex(LPSECURITY_ATTRIBUTES attributes, LPCWSTR name,
                                       DWORD flags, DWORD access) {
    wchar_t rewritten[96];
    return original_create_ex(attributes, isolated_name(name, rewritten), flags, access);
}

static HANDLE WINAPI hooked_open(DWORD access, BOOL inherit, LPCWSTR name) {
    wchar_t rewritten[96];
    return original_open(access, inherit, isolated_name(name, rewritten));
}

static void signal_parent(const wchar_t *kind) {
    wchar_t name[96];
    static const wchar_t prefix[] = L"Local\\PartyPilot.MultiInstance.";
    unsigned int at = 0;
    while (prefix[at] != L'\0') { name[at] = prefix[at]; ++at; }
    while (*kind != L'\0') name[at++] = *kind++;
    name[at++] = L'.';
    DWORD pid = GetCurrentProcessId();
    wchar_t digits[10];
    unsigned int count = 0;
    do { digits[count++] = (wchar_t)(L'0' + pid % 10u); pid /= 10u; } while (pid != 0);
    while (count > 0) name[at++] = digits[--count];
    name[at] = L'\0';
    HANDLE event = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
    if (event != NULL) { SetEvent(event); CloseHandle(event); }
}

static DWORD WINAPI install_hooks(void *unused) {
    (void)unused;
    HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
    if (kernel == NULL || MH_Initialize() != MH_OK) goto failed;
    FARPROC create = GetProcAddress(kernel, "CreateMutexW");
    FARPROC create_ex = GetProcAddress(kernel, "CreateMutexExW");
    FARPROC open = GetProcAddress(kernel, "OpenMutexW");
    if (create == NULL || create_ex == NULL || open == NULL ||
        MH_CreateHook((LPVOID)create, hooked_create, (LPVOID *)&original_create) != MH_OK ||
        MH_CreateHook((LPVOID)create_ex, hooked_create_ex, (LPVOID *)&original_create_ex) != MH_OK ||
        MH_CreateHook((LPVOID)open, hooked_open, (LPVOID *)&original_open) != MH_OK ||
        MH_EnableHook(MH_ALL_HOOKS) != MH_OK) goto failed;
    signal_parent(L"Ready");
    return 0;
failed:
    MH_DisableHook(MH_ALL_HOOKS);
    signal_parent(L"Failed");
    return 1;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        HANDLE worker = CreateThread(NULL, 0, install_hooks, NULL, 0, NULL);
        if (worker == NULL) return FALSE;
        CloseHandle(worker);
    }
    return TRUE;
}
