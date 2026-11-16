@echo off
REM ============================================================================
REM  Agent GivreRelay (l'exe que l'AMI lance pour partager sa connexion).
REM  Exe natif unique (NativeAOT), sans dependance : prod-builds\agent\GivreRelay.exe
REM ============================================================================
setlocal
set "OUT=%~dp0prod-builds\agent"
REM ILC (codegen NativeAOT) appelle vswhere pour localiser link.exe -> l'Installer VS DOIT etre sur le PATH (cf. build-dist.bat).
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
if exist "%OUT%" rmdir /s /q "%OUT%"
REM Build dans un dossier a part : un agent lance depuis bin\ (essai) ne verrouille pas l'edition de liens.
dotnet publish "%~dp0GivreRelayAgent\GivreRelayAgent.csproj" -c Release -r win-x64 --disable-build-servers --artifacts-path "%~dp0prod-builds\.agent-build" -o "%OUT%" || goto :err
if not exist "%OUT%\GivreRelay.exe" goto :err
del /q "%OUT%\*.pdb" 2>nul
echo.
echo  Agent pret : %OUT%\GivreRelay.exe
exit /b 0
:err
echo.
echo  ECHEC de la publication de l'agent GivreRelay.
exit /b 1
