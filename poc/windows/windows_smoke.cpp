#include <windows.h>
#include <wrl.h>
#include <WebView2.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

static ComPtr<ICoreWebView2Controller> g_controller;
static ComPtr<ICoreWebView2> g_webview;
static bool g_hello = false;
static bool g_video = false;

static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
  if (message == WM_TIMER) {
    std::cerr << "FAIL WebView2 smoke timed out\n";
    PostQuitMessage(2);
    return 0;
  }
  if (message == WM_DESTROY) {
    PostQuitMessage(0);
    return 0;
  }
  return DefWindowProcW(window, message, wparam, lparam);
}

static std::wstring FromUtf8(const std::string& input) {
  const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
      input.data(), static_cast<int>(input.size()), nullptr, 0);
  if (length <= 0) return {};
  std::wstring output(static_cast<size_t>(length), L'\0');
  MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, input.data(),
      static_cast<int>(input.size()), output.data(), length);
  return output;
}

int wmain(int argc, wchar_t* argv[]) {
  if (argc != 3) {
    std::cerr << "usage: windows_smoke.exe page-agent.js profile-directory\n";
    return 2;
  }
  std::ifstream source_file(std::filesystem::path(argv[1]), std::ios::binary);
  if (!source_file) {
    std::cerr << "FAIL cannot read page agent\n";
    return 2;
  }
  std::string bytes((std::istreambuf_iterator<char>(source_file)), std::istreambuf_iterator<char>());
  std::wstring script = FromUtf8(bytes);
  if (script.empty()) {
    std::cerr << "FAIL invalid UTF-8 page agent\n";
    return 2;
  }
  script += LR"(
    window.__tbSmokeAgent = ToolboxPageAgent.createAgent({
      document: document,
      transport: ToolboxPageAgent.resolveHostTransport(window),
      moduleId: 'toolbox.smoke', sessionId: 'webview2-smoke', generation: 1
    });
    window.__tbSmokeAgent.start();
  )";

  HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
  if (FAILED(hr)) return 2;
  const wchar_t* class_name = L"ToolboxWebView2Smoke";
  WNDCLASSW wc{};
  wc.lpfnWndProc = WindowProc;
  wc.hInstance = GetModuleHandleW(nullptr);
  wc.lpszClassName = class_name;
  RegisterClassW(&wc);
  HWND window = CreateWindowExW(WS_EX_TOOLWINDOW, class_name, L"Toolbox smoke",
      WS_OVERLAPPEDWINDOW, -3000, -3000, 800, 600, nullptr, nullptr, wc.hInstance, nullptr);
  if (!window) {
    CoUninitialize();
    return 2;
  }
  ShowWindow(window, SW_SHOWNOACTIVATE);
  SetTimer(window, 1, 15000, nullptr);

  std::wstring profile = argv[2];
  hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, profile.c_str(), nullptr,
      Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
          [window, script](HRESULT result, ICoreWebView2Environment* environment) -> HRESULT {
            if (FAILED(result) || !environment) {
              std::cerr << "FAIL create WebView2 environment\n";
              PostQuitMessage(2);
              return S_OK;
            }
            environment->CreateCoreWebView2Controller(window,
                Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                    [window, script](HRESULT controller_result,
                                     ICoreWebView2Controller* controller) -> HRESULT {
                      if (FAILED(controller_result) || !controller) {
                        std::cerr << "FAIL create WebView2 controller\n";
                        PostQuitMessage(2);
                        return S_OK;
                      }
                      g_controller = controller;
                      g_controller->get_CoreWebView2(&g_webview);
                      RECT bounds{};
                      GetClientRect(window, &bounds);
                      g_controller->put_Bounds(bounds);
                      EventRegistrationToken token{};
                      g_webview->add_WebMessageReceived(
                          Callback<ICoreWebView2WebMessageReceivedEventHandler>(
                              [](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                                LPWSTR raw = nullptr;
                                if (SUCCEEDED(args->TryGetWebMessageAsString(&raw)) && raw) {
                                  if (wcsstr(raw, L"\"type\":\"HELLO\"")) g_hello = true;
                                  if (wcsstr(raw, L"\"type\":\"VIDEO_FOUND\"")) g_video = true;
                                  CoTaskMemFree(raw);
                                  if (g_hello && g_video) {
                                    std::cout << "PASS real WebView2 document-start bridge and DOM event\n";
                                    PostQuitMessage(0);
                                  }
                                }
                                return S_OK;
                              }).Get(), &token);
                      g_webview->AddScriptToExecuteOnDocumentCreated(
                          script.c_str(),
                          Callback<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler>(
                              [](HRESULT add_result, LPCWSTR) -> HRESULT {
                                if (FAILED(add_result)) {
                                  std::cerr << "FAIL document-start injection\n";
                                  PostQuitMessage(2);
                                  return S_OK;
                                }
                                g_webview->NavigateToString(
                                    L"<!doctype html><html><body><video id='sample-video'></video></body></html>");
                                return S_OK;
                              }).Get());
                      return S_OK;
                    }).Get());
            return S_OK;
          }).Get());
  if (FAILED(hr)) {
    std::cerr << "FAIL WebView2 loader\n";
    DestroyWindow(window);
    CoUninitialize();
    return 2;
  }

  MSG message{};
  int result = 2;
  while (GetMessageW(&message, nullptr, 0, 0) > 0) {
    TranslateMessage(&message);
    DispatchMessageW(&message);
  }
  result = static_cast<int>(message.wParam);
  KillTimer(window, 1);
  g_webview.Reset();
  g_controller.Reset();
  DestroyWindow(window);
  CoUninitialize();
  return result;
}
