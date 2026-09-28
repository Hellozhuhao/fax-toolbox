@echo off
setlocal
if "%WEBVIEW2_SDK_ROOT%"=="" (
  echo Set WEBVIEW2_SDK_ROOT to the extracted Microsoft.Web.WebView2 NuGet package.
  exit /b 2
)
if not exist build mkdir build
cl /nologo /std:c++17 /W4 /WX /EHsc /O2 /I"%WEBVIEW2_SDK_ROOT%\build\native\include" windows_smoke.cpp /Fe:build\windows_smoke.exe /Fo:build\ /link /LIBPATH:"%WEBVIEW2_SDK_ROOT%\build\native\x64" WebView2Loader.dll.lib ole32.lib user32.lib
if errorlevel 1 exit /b 1
copy /y "%WEBVIEW2_SDK_ROOT%\build\native\x64\WebView2Loader.dll" build\ >nul
exit /b %errorlevel%
