@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%
cd /d "%~dp0"
if not exist obj mkdir obj
if not exist bin mkdir bin
cl /nologo /std:c++20 /EHsc /O2 /MD /utf-8 /DWIN32_LEAN_AND_MEAN /DNOMINMAX /Iadapter /Ivendor\dofus-enhanced\src /I..\dofus3-native-host\include /LD bridge.cpp vendor\dofus-enhanced\src\core\common.cpp vendor\dofus-enhanced\src\core\runtime.cpp vendor\dofus-enhanced\src\animations\*.cpp vendor\dofus-enhanced\src\appearance\features.cpp vendor\dofus-enhanced\src\appearance\appearance-refresh.cpp vendor\dofus-enhanced\src\appearance\size-measurements.cpp /Foobj\ /Febin\GivreCombatVisuals.dll /link bcrypt.lib user32.lib
exit /b %errorlevel%
