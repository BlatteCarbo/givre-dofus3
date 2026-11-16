# Outils de décompilation locaux ; pas de modification des paramètres Windows.
$env:GHIDRA_INSTALL_DIR = Join-Path $env:LOCALAPPDATA 'Programs\Givre-Analysis\ghidra_12.1.4_PUBLIC'
$env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot'
$env:REA_JAVA_COMMAND = Join-Path $env:JAVA_HOME 'bin\java.exe'
$env:Path = (Join-Path $env:JAVA_HOME 'bin') + ';' + $env:Path
& npx.cmd -y rea-agents@6.7.0 @args
exit $LASTEXITCODE
