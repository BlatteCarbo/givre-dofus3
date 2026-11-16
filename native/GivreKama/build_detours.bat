@echo off
rem Compile Detours (MIT, microsoft/Detours) en bibliotheque statique x64 /MT -> third_party\Detours\lib.X64\detours.lib
setlocal
call "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" || exit /b 1
set D=%~dp0third_party\Detours
if not exist "%D%\lib.X64" mkdir "%D%\lib.X64"
if not exist "%D%\obj" mkdir "%D%\obj"
cl /nologo /c /MT /O2 /W3 /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0601 /DDETOURS_X64 /I"%D%\include" /Fo"%D%\obj\\" ^
  "%D%\src\detours.cpp" "%D%\src\modules.cpp" "%D%\src\disasm.cpp" "%D%\src\image.cpp" "%D%\src\creatwth.cpp" ^
  "%D%\src\disolx64.cpp" "%D%\src\disolx86.cpp" "%D%\src\disolia64.cpp" "%D%\src\disolarm.cpp" "%D%\src\disolarm64.cpp" || exit /b 2
lib /nologo /OUT:"%D%\lib.X64\detours.lib" "%D%\obj\*.obj" || exit /b 3
echo OK detours.lib
