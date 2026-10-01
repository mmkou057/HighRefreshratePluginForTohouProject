/* ==================================================================
 * TH_PluginInjector —— 东方 TH15 插件整合开关 / 启动器
 *
 * 同时管理两个 DLL 代理插件（启用 = 放入游戏目录，停用 = 改名备份，
 * 不删除任何文件，可反复切换、随时还原）：
 *   高刷新率插件：d3d9.dll + hfr.ini（plugins\hfr\）
 *   全向移动插件：dinput8.dll + padhook.ini（plugins\omni\）
 *
 * 插件载荷放在本程序同目录的 plugins\ 下；游戏目录记忆在
 * injector.ini。切换需在游戏退出状态下进行（DLL 被进程锁定）。
 * 纯 Win32 + 32 位 MinGW 编译，无外部依赖。
 * ================================================================== */
#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#include <windows.h>
#include <commdlg.h>
#include <shellapi.h>
#include <stdio.h>

#define IDC_DIR     1001
#define IDC_BROWSE  1002
#define IDC_CHK_HFR 1003
#define IDC_CHK_OM  1004
#define IDC_REFRESH 1005
#define IDC_APPLY   1006
#define IDC_LAUNCH  1007
#define IDC_STATUS  1008

static wchar_t g_dir[MAX_PATH]   = L"";   /* 游戏目录 */
static wchar_t g_edir[MAX_PATH]  = L"";   /* 注入器所在目录 */
static HWND g_hdir, g_hst;

static const wchar_t* HFR_FILES[] = { L"d3d9.dll", L"hfr.ini" };
static const wchar_t* OM_FILES[]  = { L"dinput8.dll", L"padhook.ini" };
#define BACKUP_SUFFIX L".hfr-off"

static void path_join(wchar_t* out, const wchar_t* a, const wchar_t* b) {
    _snwprintf(out, MAX_PATH, L"%ls\\%ls", a, b);
}

static int file_exists(const wchar_t* p) {
    DWORD a = GetFileAttributesW(p);
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

static void cfg_path(wchar_t* out) { path_join(out, g_edir, L"injector.ini"); }

static void cfg_load(void) {
    wchar_t ini[MAX_PATH]; cfg_path(ini);
    GetPrivateProfileStringW(L"main", L"gamedir", L"", g_dir, MAX_PATH, ini);
}
static void cfg_save(void) {
    wchar_t ini[MAX_PATH]; cfg_path(ini);
    WritePrivateProfileStringW(L"main", L"gamedir", g_dir, ini);
}

/* 插件状态：1=已启用(活动文件在)，0=已停用(只有备份)，-1=未安装过 */
static int plugin_state(const wchar_t* const* files, int n) {
    int seen_bak = 0;
    for (int i = 0; i < n; ++i) {
        wchar_t t[MAX_PATH], b[MAX_PATH];
        path_join(t, g_dir, files[i]);
        _snwprintf(b, MAX_PATH, L"%ls%ls", t, BACKUP_SUFFIX);
        if (file_exists(t)) return 1;
        if (file_exists(b)) seen_bak = 1;
    }
    return seen_bak ? 0 : -1;
}

static int toggle_one(const wchar_t* group, const wchar_t* file,
                      int enable, wchar_t* err, size_t errcap) {
    wchar_t target[MAX_PATH], bak[MAX_PATH], payload[MAX_PATH];
    path_join(target, g_dir, file);
    _snwprintf(bak, MAX_PATH, L"%ls%ls", target, BACKUP_SUFFIX);
    path_join(payload, g_edir, L"plugins");
    wchar_t tmp[MAX_PATH];
    path_join(tmp, payload, group);
    path_join(payload, tmp, file);

    if (enable) {
        if (file_exists(target)) return 1;                 /* 已在游戏目录 */
        if (file_exists(bak)) {                            /* 还原停用前的文件 */
            if (!MoveFileW(bak, target)) {
                _snwprintf(err, errcap, L"还原 %ls 失败（错误 %lu，游戏可能还在运行）", file, GetLastError());
                return 0;
            }
        } else {                                           /* 首次安装：释放载荷 */
            if (!file_exists(payload)) {
                _snwprintf(err, errcap, L"缺少载荷文件 plugins\\%ls\\%ls", group, file);
                return 0;
            }
            if (!CopyFileW(payload, target, FALSE)) {
                _snwprintf(err, errcap, L"复制 %ls 失败（错误 %lu，游戏可能还在运行）", file, GetLastError());
                return 0;
            }
        }
    } else {
        if (!file_exists(target)) return 1;                /* 本来就没启用 */
        if (file_exists(bak)) DeleteFileW(bak);
        if (!MoveFileW(target, bak)) {
            _snwprintf(err, errcap, L"停用 %ls 失败（错误 %lu，游戏可能还在运行）", file, GetLastError());
            return 0;
        }
    }
    return 1;
}

static int apply_plugin(const wchar_t* group, const wchar_t* const* files,
                        int n, int enable, wchar_t* err, size_t errcap) {
    for (int i = 0; i < n; ++i)
        if (!toggle_one(group, files[i], enable, err, errcap)) return 0;
    return 1;
}

static void refresh_ui(void) {
    wchar_t exe[MAX_PATH]; path_join(exe, g_dir, L"th15.exe");
    int valid = g_dir[0] != 0 && file_exists(exe);
    int hs = plugin_state(HFR_FILES, 2);
    int os = plugin_state(OM_FILES,  2);
    HWND h = GetParent(g_hst);
    CheckDlgButton(h, IDC_CHK_HFR, hs == 1 ? BST_CHECKED : BST_UNCHECKED);
    CheckDlgButton(h, IDC_CHK_OM,  os == 1 ? BST_CHECKED : BST_UNCHECKED);
    EnableWindow(GetDlgItem(h, IDC_APPLY), valid);
    EnableWindow(GetDlgItem(h, IDC_LAUNCH), valid);
    wchar_t msg[256];
    if (!g_dir[0])
        _snwprintf(msg, 256, L"请先选择游戏目录（th15.exe 所在文件夹）。");
    else if (!valid)
        _snwprintf(msg, 256, L"目录中未找到 th15.exe：%ls", g_dir);
    else
        _snwprintf(msg, 256,
            L"高刷新率：%ls    全向移动：%ls",
            hs == 1 ? L"已启用" : (hs == 0 ? L"已停用（备份保留）" : L"未安装"),
            os == 1 ? L"已启用" : (os == 0 ? L"已停用（备份保留）" : L"未安装"));
    SetWindowTextW(g_hst, msg);
}

static void do_apply(int and_launch) {
    wchar_t exe[MAX_PATH]; path_join(exe, g_dir, L"th15.exe");
    if (!file_exists(exe)) { MessageBoxW(NULL, L"请先选择包含 th15.exe 的有效游戏目录。", L"整合注入器", MB_ICONWARNING); return; }
    cfg_save();
    int eh = IsDlgButtonChecked(GetParent(g_hst), IDC_CHK_HFR) == BST_CHECKED;
    int eo = IsDlgButtonChecked(GetParent(g_hst), IDC_CHK_OM)  == BST_CHECKED;
    wchar_t err[256] = L"";
    if (!apply_plugin(L"hfr",  HFR_FILES, 2, eh, err, 256)) { MessageBoxW(NULL, err, L"高刷新率插件", MB_ICONERROR); refresh_ui(); return; }
    if (!apply_plugin(L"omni", OM_FILES,  2, eo, err, 256)) { MessageBoxW(NULL, err, L"全向移动插件", MB_ICONERROR); refresh_ui(); return; }
    refresh_ui();
    if (and_launch) {
        HINSTANCE r = ShellExecuteW(NULL, L"open", exe, NULL, g_dir, SW_SHOWNORMAL);
        if ((INT_PTR)r <= 32)
            MessageBoxW(NULL, L"游戏启动失败，请手动运行 th15.exe。", L"整合注入器", MB_ICONWARNING);
    }
}

static void browse(void) {
    wchar_t sel[MAX_PATH]; GetWindowTextW(g_hdir, sel, MAX_PATH);
    if (!sel[0] && g_dir[0]) wcscpy(sel, g_dir);
    OPENFILENAMEW ofn; ZeroMemory(&ofn, sizeof ofn);
    ofn.lStructSize = sizeof ofn;
    ofn.hwndOwner = GetParent(g_hst);
    ofn.lpstrFilter = L"东方绀珠传主程序 (th15.exe)\0th15.exe\0程序 (*.exe)\0*.exe\0";
    ofn.lpstrFile = sel;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST;
    if (!GetOpenFileNameW(&ofn)) return;
    wcsncpy(g_dir, sel, MAX_PATH - 1); g_dir[MAX_PATH - 1] = 0;
    wchar_t* slash = wcsrchr(g_dir, L'\\');
    if (slash) *slash = 0;
    SetWindowTextW(g_hdir, g_dir);
    refresh_ui();
}

static void layout(HWND h) {
    int W = 540;
    int y = 14;
    CreateWindowW(L"STATIC", L"游戏目录（th15.exe 所在文件夹）：", WS_CHILD | WS_VISIBLE,
                  14, y, 320, 18, h, NULL, NULL, NULL);
    y += 22;
    g_hdir = CreateWindowW(L"EDIT", g_dir, WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
                           14, y, 400, 24, h, (HMENU)(INT_PTR)IDC_DIR, NULL, NULL);
    CreateWindowW(L"BUTTON", L"浏览…", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                  428, y, 98, 24, h, (HMENU)(INT_PTR)IDC_BROWSE, NULL, NULL);
    y += 38;
    CreateWindowW(L"BUTTON", L"启用高刷新率插件（d3d9.dll，120/144/240/360/400Hz；签名自动识别，不限 exe 版本）",
                  WS_CHILD | WS_VISIBLE | BS_AUTOCHECKBOX,
                  14, y, 512, 22, h, (HMENU)(INT_PTR)IDC_CHK_HFR, NULL, NULL);
    y += 30;
    CreateWindowW(L"BUTTON", L"启用全向移动插件（dinput8.dll，摇杆任意角度移动，帧率自适应）",
                  WS_CHILD | WS_VISIBLE | BS_AUTOCHECKBOX,
                  14, y, 500, 22, h, (HMENU)(INT_PTR)IDC_CHK_OM, NULL, NULL);
    y += 40;
    CreateWindowW(L"BUTTON", L"应用开关", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                  14, y, 120, 30, h, (HMENU)(INT_PTR)IDC_APPLY, NULL, NULL);
    CreateWindowW(L"BUTTON", L"应用并启动游戏", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                  148, y, 150, 30, h, (HMENU)(INT_PTR)IDC_LAUNCH, NULL, NULL);
    CreateWindowW(L"BUTTON", L"刷新状态", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                  312, y, 100, 30, h, (HMENU)(INT_PTR)IDC_REFRESH, NULL, NULL);
    y += 44;
    g_hst = CreateWindowW(L"STATIC", L"", WS_CHILD | WS_VISIBLE,
                          14, y, 510, 40, h, (HMENU)(INT_PTR)IDC_STATUS, NULL, NULL);
    (void)W;
    HFONT f = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    for (HWND c = GetWindow(h, GW_CHILD); c; c = GetWindow(c, GW_HWNDNEXT))
        SendMessageW(c, WM_SETFONT, (WPARAM)f, TRUE);
}

static LRESULT CALLBACK WndProc(HWND h, UINT m, WPARAM wp, LPARAM lp) {
    switch (m) {
    case WM_CREATE:
        layout(h);
        cfg_load();
        SetWindowTextW(g_hdir, g_dir);
        refresh_ui();
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wp)) {
        case IDC_BROWSE:  browse(); return 0;
        case IDC_REFRESH:
            GetWindowTextW(g_hdir, g_dir, MAX_PATH);
            refresh_ui(); return 0;
        case IDC_APPLY:   do_apply(0); return 0;
        case IDC_LAUNCH:  do_apply(1); return 0;
        }
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(h, m, wp, lp);
}

int WINAPI wWinMain(HINSTANCE hi, HINSTANCE hp, PWSTR cmd, int show) {
    (void)hp; (void)cmd;
    GetModuleFileNameW(NULL, g_edir, MAX_PATH);
    wchar_t* s = wcsrchr(g_edir, L'\\'); if (s) *s = 0;

    WNDCLASSW wc; ZeroMemory(&wc, sizeof wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hi;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = L"THPluginInjector";
    RegisterClassW(&wc);
    HWND h = CreateWindowW(wc.lpszClassName,
        L"东方 TH15 插件整合注入器（高刷 + 全向移动）",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        CW_USEDEFAULT, CW_USEDEFAULT, 560, 280,
        NULL, NULL, hi, NULL);
    RECT rc;
    GetWindowRect(h, &rc);
    int sw = GetSystemMetrics(SM_CXSCREEN), sh = GetSystemMetrics(SM_CYSCREEN);
    SetWindowPos(h, NULL, (sw - (rc.right - rc.left)) / 2,
                 (sh - (rc.bottom - rc.top)) / 3, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    ShowWindow(h, show);
    UpdateWindow(h);
    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return 0;
}
