#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// Unity 2021.3 documented in-process entry point. Keep the existing player,
// managed shell/API, backend, executable name and application data paths.
extern "C" __declspec(dllimport) int UnityMain(HINSTANCE, HINSTANCE, LPWSTR, int);
// Preserve the two GPU preference exports present in the shipped Unity exe.
extern "C" {
    __declspec(dllexport) DWORD NvOptimusEnablement = 1;
    __declspec(dllexport) DWORD AmdPowerXpressRequestHighPerformance = 1;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR arguments, int show) {
    // Mono Assembly.Load(bytes).GetName() uses the process working directory
    // as a synthetic CodeBase and fails on non-ASCII Windows paths (#20968).
    // Asset/config paths remain rooted at the executable via Application.dataPath.
    wchar_t directory[MAX_PATH];
    UINT length = GetWindowsDirectoryW(directory, MAX_PATH);
    if (length < 3 || length >= MAX_PATH || directory[1] != L':' || directory[2] != L'\\'
        || !((directory[0] >= L'A' && directory[0] <= L'Z') || (directory[0] >= L'a' && directory[0] <= L'z'))) {
        MessageBoxW(nullptr, L"Windows 실행 경로를 확인하지 못했습니다.", L"Starseed Old Story Viewer", MB_OK | MB_ICONERROR);
        return 1;
    }
    directory[3] = L'\0';
    if (!SetCurrentDirectoryW(directory)) {
        MessageBoxW(nullptr, L"실행 기준 폴더를 설정하지 못했습니다.", L"Starseed Old Story Viewer", MB_OK | MB_ICONERROR);
        return 1;
    }
    return UnityMain(instance, previous, arguments, show);
}
