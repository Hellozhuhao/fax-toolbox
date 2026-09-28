@echo off
setlocal
if not exist build mkdir build
cl /nologo /std:c++17 /W4 /WX /EHsc /O2 /DTB_CORE_BUILD /LD tb_core.cpp /Fe:build\tb_core.dll /Fo:build\tb_core.obj
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /W4 /WX /EHsc /O2 native_smoke.cpp /Fe:build\native_smoke.exe /Fo:build\native_smoke.obj /link /LIBPATH:build tb_core.lib
if errorlevel 1 exit /b 1
build\native_smoke.exe
exit /b %errorlevel%
