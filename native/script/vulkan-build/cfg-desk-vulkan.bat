@echo off
REM Desktop Vulkan reconfigure: load MSVC vcvars64, then cmake configure build-desktop with Vulkan ON.
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 ( echo VCVARS_FAILED & exit /b 1 )

set CMAKE="C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
set NINJA="C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
set SRC=J:\GGML-GAME\OpenUtauMobile\native
set BLD=J:\GGML-GAME\OpenUtauMobile\native\build-desktop

REM Use a SEPARATE build dir so the working CPU build stays intact as fallback.
set BLDV=J:\GGML-GAME\OpenUtauMobile\native\build-desktop-vulkan
%CMAKE% -S %SRC% -B %BLDV% -G Ninja -DCMAKE_BUILD_TYPE=Release ^
  -DCMAKE_MAKE_PROGRAM=%NINJA% ^
  -DGAME_GGML_VULKAN=ON -DGGML_VULKAN=ON ^
  -DGGML_NATIVE=ON -DGGML_OPENMP=ON ^
  -DFETCHCONTENT_SOURCE_DIR_GGML="J:\GGML-GAME\OpenUtauMobile\native\build-desktop\_deps\ggml-src" ^
  -DFETCHCONTENT_SOURCE_DIR_POCKETFFT="J:\GGML-GAME\OpenUtauMobile\native\build-desktop\_deps\pocketfft-src" ^
  -DFETCHCONTENT_SOURCE_DIR_DR_LIBS="J:\GGML-GAME\OpenUtauMobile\native\build-desktop\_deps\dr_libs-src" ^
  -DGAME_GGML_BUILD_CLI=OFF -DGAME_GGML_BUILD_TESTS=OFF -DOPUM_NATIVE_SMOKE=ON
if errorlevel 1 ( echo CONFIGURE_FAILED & exit /b 1 )
echo CONFIGURE_OK
exit /b 0
