@echo off
setlocal
for %%I in ("%~dp0..\..") do set "ROOT=%%~fI"
if "%WEBVIEW2_SDK_ROOT%"=="" set "WEBVIEW2_SDK_ROOT=%ROOT%\.cache\webview2-sdk-1.0.4258.31"
if not exist "%WEBVIEW2_SDK_ROOT%\build\native\include\WebView2.h" (
  echo WebView2 SDK missing. Run: powershell -File scripts\setup-webview2.ps1
  exit /b 2
)
where cl >nul 2>&1
if errorlevel 1 (
  echo Run this script from an x64 Native Tools Command Prompt for Visual Studio.
  exit /b 2
)
if not exist "%~dp0build" mkdir "%~dp0build"
cl /nologo /utf-8 /std:c++17 /W4 /WX /EHsc /O2 /DTB_CORE_BUILD /LD "%ROOT%\native\core\tb_core.cpp" /I"%ROOT%\native\core" /Fe:"%~dp0build\tb_core.dll" /Fo:"%~dp0build\tb_core.obj"
if errorlevel 1 exit /b 1
cl /nologo /utf-8 /std:c++17 /W4 /WX /EHsc /O2 /I"%WEBVIEW2_SDK_ROOT%\build\native\include" /I"%ROOT%\native\core" "%~dp0main.cpp" /Fe:"%~dp0build\toolbox.exe" /Fo:"%~dp0build\main.obj" /link /LIBPATH:"%WEBVIEW2_SDK_ROOT%\build\native\x64" /LIBPATH:"%~dp0build" WebView2Loader.dll.lib tb_core.lib ole32.lib user32.lib shlwapi.lib
if errorlevel 1 exit /b 1
copy /y "%WEBVIEW2_SDK_ROOT%\build\native\x64\WebView2Loader.dll" "%~dp0build\" >nul
exit /b %errorlevel%
