#include <windows.h>
#include <wrl.h>
#include <WebView2.h>
#include <shlwapi.h>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include "tb_core.h"

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

namespace {
constexpr int kToolbarHeight = 48;
constexpr int kRunButton = 1001;
HWND g_window = nullptr;
HWND g_status = nullptr;
ComPtr<ICoreWebView2Controller> g_controller;
ComPtr<ICoreWebView2> g_webview;
tb_core* g_core = nullptr;
std::wstring g_page_url;
fs::path g_page_path;
bool g_smoke = false;

void Fail(const char* reason) {
  std::cerr << "FAIL " << reason << '\n';
  if (g_smoke) PostQuitMessage(2);
  else if (g_status) SetWindowTextA(g_status, reason);
}

std::wstring Utf8(const std::string& bytes) {
  int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(),
                                static_cast<int>(bytes.size()), nullptr, 0);
  if (size <= 0) return {};
  std::wstring result(static_cast<size_t>(size), L'\0');
  MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(),
                      static_cast<int>(bytes.size()), result.data(), size);
  return result;
}

std::wstring ReadScript(const fs::path& path) {
  std::ifstream input(path, std::ios::binary);
  if (!input) return {};
  return Utf8(std::string(std::istreambuf_iterator<char>(input), {}));
}

bool HasField(const std::wstring& raw, const wchar_t* field) {
  return raw.find(field) != std::wstring::npos;
}

bool IsTrustedUri(const wchar_t* uri) {
  if (!uri || wcsncmp(uri, L"file:///", 8) != 0) return false;
  std::string bytes;
  for (const wchar_t* cursor = uri + 8; *cursor; ++cursor) {
    if (*cursor == L'%') {
      auto hex = [](wchar_t digit) -> int {
        if (digit >= L'0' && digit <= L'9') return digit - L'0';
        if (digit >= L'A' && digit <= L'F') return digit - L'A' + 10;
        if (digit >= L'a' && digit <= L'f') return digit - L'a' + 10;
        return -1;
      };
      if (!cursor[1] || !cursor[2]) return false;
      int high = hex(cursor[1]);
      int low = hex(cursor[2]);
      if (high < 0 || low < 0) return false;
      bytes.push_back(static_cast<char>(high * 16 + low));
      cursor += 2;
    } else {
      if (*cursor > 127) return false;
      bytes.push_back(*cursor == L'/' ? '\\' : static_cast<char>(*cursor));
    }
  }
  std::wstring path = Utf8(bytes);
  return !path.empty() && CompareStringOrdinal(path.c_str(), -1,
      g_page_path.c_str(), -1, TRUE) == CSTR_EQUAL;
}

void ResetCore() {
  tb_core_destroy(g_core);
  g_core = tb_core_create();
  if (g_status) SetWindowTextW(g_status, L"加载模块中…");
}

void HandleMessage(ICoreWebView2WebMessageReceivedEventArgs* args) {
  LPWSTR source = nullptr;
  if (FAILED(args->get_Source(&source)) || !source) return;
  const bool trusted = IsTrustedUri(source);
  CoTaskMemFree(source);
  if (!trusted) return;
  LPWSTR raw = nullptr;
  if (FAILED(args->TryGetWebMessageAsString(&raw)) || !raw) return;
  std::wstring message(raw);
  CoTaskMemFree(raw);
  if (message.size() > 16384 ||
      !HasField(message, L"\"protocol\":1") ||
      !HasField(message, L"\"moduleId\":\"toolbox.smoke\"") ||
      !HasField(message, L"\"sessionId\":\"starter\"") ||
      !HasField(message, L"\"frameId\":\"main\"") ||
      !HasField(message, L"\"generation\":1")) return;

  const size_t seq_at = message.find(L"\"seq\":");
  if (seq_at == std::wstring::npos) return;
  const wchar_t* seq_text = message.c_str() + seq_at + 6;
  wchar_t* end = nullptr;
  unsigned long long seq = wcstoull(seq_text, &end, 10);
  if (end == seq_text || (end && *end != L',')) return;

  tb_event event{};
  if (HasField(message, L"\"type\":\"HELLO\"")) event = TB_PAGE_READY;
  else if (HasField(message, L"\"type\":\"VIDEO_FOUND\"")) event = TB_VIDEO_FOUND;
  else if (HasField(message, L"\"type\":\"VIDEO_ENDED\"")) event = TB_VIDEO_ENDED;
  else return;
  if (tb_core_dispatch(g_core, event, seq) != TB_OK) return;
  const tb_state state = tb_core_state(g_core);
  if (state == TB_OBSERVING) {
    if (g_status) SetWindowTextW(g_status, L"已发现视频；点击页面按钮完成验证");
    if (g_smoke) g_webview->ExecuteScript(L"document.getElementById('finish').click();", nullptr);
  } else if (state == TB_COMPLETE) {
    if (g_status) SetWindowTextW(g_status, L"C++ 核心状态：COMPLETE");
    if (g_smoke) {
      std::cout << "PASS Windows WebView2 -> shared agent -> C++ core COMPLETE\n";
      PostQuitMessage(0);
    }
  }
}

void Resize() {
  if (!g_controller || !g_window) return;
  RECT bounds{};
  GetClientRect(g_window, &bounds);
  if (!g_smoke) bounds.top += kToolbarHeight;
  g_controller->put_Bounds(bounds);
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
  switch (message) {
    case WM_SIZE:
      if (g_status) {
        RECT rect{};
        GetClientRect(window, &rect);
        MoveWindow(g_status, 144, 14, rect.right - 156, 24, TRUE);
      }
      Resize();
      return 0;
    case WM_COMMAND:
      if (LOWORD(wparam) == kRunButton && g_webview) {
        ResetCore();
        g_webview->Navigate(g_page_url.c_str());
      }
      return 0;
    case WM_TIMER:
      Fail("WebView2 startup timed out");
      return 0;
    case WM_DESTROY:
      PostQuitMessage(0);
      return 0;
  }
  return DefWindowProcW(window, message, wparam, lparam);
}
}  // namespace

int wmain(int argc, wchar_t* argv[]) {
  g_smoke = argc > 1 && std::wstring(argv[1]) == L"--smoke";
  wchar_t executable[MAX_PATH]{};
  if (!GetModuleFileNameW(nullptr, executable, MAX_PATH)) return 2;
  fs::path root = fs::path(executable).parent_path().parent_path().parent_path().parent_path();
  std::wstring script = ReadScript(root / L"packages/page-agent/page-agent.js");
  if (script.empty()) { Fail("page-agent.js missing or invalid UTF-8"); return 2; }
  script += LR"(
    window.__toolboxAgent = ToolboxPageAgent.createAgent({
      document: document, transport: ToolboxPageAgent.resolveHostTransport(window),
      moduleId: 'toolbox.smoke', sessionId: 'starter', generation: 1
    });
    window.__toolboxAgent.start();
  )";
  wchar_t url[4096]{};
  DWORD url_length = 4096;
  const std::wstring page = (root / L"modules/smoke/smoke.html").wstring();
  g_page_path = fs::path(page).make_preferred();
  if (!fs::exists(page) || FAILED(UrlCreateFromPathW(page.c_str(), url, &url_length, 0))) {
    Fail("smoke.html missing or invalid path"); return 2;
  }
  g_page_url = url;
  g_core = tb_core_create();
  if (!g_core) return 2;
  if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
  WNDCLASSW wc{};
  wc.lpfnWndProc = WindowProc;
  wc.hInstance = GetModuleHandleW(nullptr);
  wc.lpszClassName = L"ToolboxStarterWindow";
  RegisterClassW(&wc);
  g_window = CreateWindowExW(g_smoke ? WS_EX_TOOLWINDOW : 0, wc.lpszClassName,
      L"Toolbox Starter", WS_OVERLAPPEDWINDOW, g_smoke ? -3000 : CW_USEDEFAULT,
      g_smoke ? -3000 : CW_USEDEFAULT, 900, 620, nullptr, nullptr, wc.hInstance, nullptr);
  if (!g_window) return 2;
  if (!g_smoke) {
    CreateWindowW(L"BUTTON", L"重新运行", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                  12, 10, 120, 30, g_window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kRunButton)), wc.hInstance, nullptr);
    g_status = CreateWindowW(L"STATIC", L"加载模块中…", WS_CHILD | WS_VISIBLE,
                  144, 14, 600, 24, g_window, nullptr, wc.hInstance, nullptr);
  }
  ShowWindow(g_window, g_smoke ? SW_SHOWNOACTIVATE : SW_SHOW);
  if (g_smoke) SetTimer(g_window, 1, 15000, nullptr);
  wchar_t local_app_data[MAX_PATH]{};
  GetEnvironmentVariableW(L"LOCALAPPDATA", local_app_data, MAX_PATH);
  fs::path profile = g_smoke ? root / L".cache/webview2-smoke" :
      fs::path(local_app_data[0] ? local_app_data : L".") / L"Toolbox/WebView2";
  fs::create_directories(profile);
  HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, profile.c_str(), nullptr,
    Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
      [script](HRESULT result, ICoreWebView2Environment* environment) -> HRESULT {
        if (FAILED(result) || !environment) { Fail("WebView2 environment unavailable"); return S_OK; }
        environment->CreateCoreWebView2Controller(g_window,
          Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
            [script](HRESULT controller_result, ICoreWebView2Controller* controller) -> HRESULT {
              if (FAILED(controller_result) || !controller) { Fail("WebView2 controller failed"); return S_OK; }
              g_controller = controller;
              g_controller->get_CoreWebView2(&g_webview);
              Resize();
              EventRegistrationToken token{};
              g_webview->add_WebMessageReceived(
                Callback<ICoreWebView2WebMessageReceivedEventHandler>(
                  [](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                    HandleMessage(args); return S_OK;
                  }).Get(), &token);
              g_webview->add_NavigationStarting(
                Callback<ICoreWebView2NavigationStartingEventHandler>(
                  [](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                    LPWSTR uri = nullptr;
                    if (SUCCEEDED(args->get_Uri(&uri)) && uri) {
                      if (!IsTrustedUri(uri)) args->put_Cancel(TRUE);
                      CoTaskMemFree(uri);
                    }
                    return S_OK;
                  }).Get(), &token);
              g_webview->AddScriptToExecuteOnDocumentCreated(script.c_str(),
                Callback<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler>(
                  [](HRESULT add_result, LPCWSTR) -> HRESULT {
                    if (FAILED(add_result)) Fail("document-start injection failed");
                    else g_webview->Navigate(g_page_url.c_str());
                    return S_OK;
                  }).Get());
              return S_OK;
            }).Get());
        return S_OK;
      }).Get());
  if (FAILED(hr)) Fail("WebView2 loader failed");
  MSG message{};
  while (GetMessageW(&message, nullptr, 0, 0) > 0) {
    TranslateMessage(&message);
    DispatchMessageW(&message);
  }
  const int result = static_cast<int>(message.wParam);
  g_webview.Reset();
  g_controller.Reset();
  tb_core_destroy(g_core);
  if (IsWindow(g_window)) DestroyWindow(g_window);
  CoUninitialize();
  return result;
}
