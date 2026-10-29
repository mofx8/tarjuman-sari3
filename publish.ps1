$ErrorActionPreference = "Stop"
# Builds the release files in dist/: a portable zip (always) and an installer (if Inno Setup 6 is installed).
$version = ([xml](Get-Content src/TarjumanSari3.App/TarjumanSari3.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = "1.0.0" }

dotnet publish src/TarjumanSari3.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/app
Get-ChildItem dist/app -Exclude TarjumanSari3.exe | Remove-Item -Recurse -Force   # keep only the single-file exe

$zip = "dist/TarjumanSari3-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path dist/app/TarjumanSari3.exe, README.md, LICENSE, PRIVACY.md -DestinationPath $zip
Write-Host "Portable zip: $zip"

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) { & $iscc "/DAppVersion=$version" installer/TarjumanSari3.iss } else { Write-Host "Inno Setup not found - skipped the installer (the zip is ready)." }
