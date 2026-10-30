param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$packageRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "BetterScreenShot-$Runtime"))
if (!$packageRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package folder must stay inside the project artifacts folder.'
}
if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
$appRoot = Join-Path $packageRoot 'app'
dotnet publish (Join-Path $projectRoot 'BetterScreenShot.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o $appRoot
if ($LASTEXITCODE -ne 0) { throw 'Publishing the application failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $packageRoot
@'
@echo off
powershell.exe -NoProfile -File "%~dp0Install.ps1" -SourceDirectory "%~dp0app"
if errorlevel 1 pause
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'Install.cmd') -Encoding ascii
$archive = Join-Path $artifactsRoot "BetterScreenShot-$Runtime.zip"
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force
Write-Output "Installer package: $archive"
