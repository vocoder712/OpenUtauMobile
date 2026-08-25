@echo off
REM Build desktop Vulkan variant (no reconfigure - uses existing build-desktop-vulkan).
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 ( echo VCVARS_FAILED & exit /b 1 )

set CMAKE="C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
set BLDV=J:\GGML-GAME\OpenUtauMobile\native\build-desktop-vulkan

REM Existing configure in $BLDV has Vulkan=ON; just build.
%CMAKE% --build %BLDV% --target game_ggml_shared game_capi_check -j 12
if errorlevel 1 ( echo BUILD_FAILED & exit /b 1 )
echo BUILD_OK
exit /b 0
