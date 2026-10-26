#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

static void event_name(wchar_t *output, const wchar_t *kind) {
    swprintf(output, 96, L"Local\\PartyPilot.MultiInstance.%ls.%lu", kind, GetCurrentProcessId());
}

int wmain(int argc, wchar_t **argv) {
    static const wchar_t *mutex_name = L"Local\\PartyPilot.MultiInstance.Smoke.Shared";
    if (argc == 2 && wcscmp(argv[1], L"--child") == 0) {
        wchar_t name[96];
        event_name(name, L"Ready");
        HANDLE ready = CreateEventW(NULL, FALSE, FALSE, name);
        if (ready == NULL) return 2;
        HMODULE runtime = LoadLibraryW(L"PartyPilot.MultiInstance.dll");
        if (runtime == NULL || WaitForSingleObject(ready, 10000) != WAIT_OBJECT_0) return 3;
        HANDLE first = CreateMutexW(NULL, FALSE, mutex_name);
        if (first == NULL || GetLastError() == ERROR_ALREADY_EXISTS) return 4;
        HANDLE second = CreateMutexExW(NULL, mutex_name, 0, MUTEX_ALL_ACCESS);
        if (second == NULL || GetLastError() != ERROR_ALREADY_EXISTS) return 5;
        HANDLE opened = OpenMutexW(MUTEX_ALL_ACCESS, FALSE, mutex_name);
        if (opened == NULL) return 6;
        CloseHandle(opened);
        CloseHandle(second);
        CloseHandle(first);
        CloseHandle(ready);
        return 0;
    }
    HANDLE parent = CreateMutexW(NULL, FALSE, mutex_name);
    if (parent == NULL || GetLastError() == ERROR_ALREADY_EXISTS) return 2;
    wchar_t self[32768];
    if (GetModuleFileNameW(NULL, self, 32768) == 0) return 2;
    wchar_t command[32768];
    if (swprintf(command, 32768, L"\"%ls\" --child", self) < 0) return 2;
    STARTUPINFOW startup = {0};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {0};
    if (!CreateProcessW(self, command, NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process)) return 2;
    if (WaitForSingleObject(process.hProcess, 15000) != WAIT_OBJECT_0) return 3;
    DWORD exit_code = 1;
    if (!GetExitCodeProcess(process.hProcess, &exit_code)) return 3;
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    CloseHandle(parent);
    if (exit_code != 0) return (int)exit_code;
    puts("Multi-instance mutex isolation smoke passed.");
    return 0;
}
