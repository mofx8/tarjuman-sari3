$ErrorActionPreference = "Stop"
dotnet publish src/QuickTranslate.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/app
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) { & $iscc installer/QuickTranslate.iss } else { Write-Host "Inno Setup not found - skipped installer (dist/app is ready)." }
