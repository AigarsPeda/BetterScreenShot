param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$suffix = if ($FrameworkDependent) { '-FrameworkDependent' } else { '' }
$publishRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "BetterScreenShot-Portable-$Runtime$suffix"))
if (!$publishRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable output must stay inside the project artifacts folder.'
}
if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
$selfContained = (!$FrameworkDependent).ToString().ToLowerInvariant()
# Compression is supported only for self-contained bundles.
dotnet publish (Join-Path $projectRoot 'BetterScreenShot.csproj') -c Release -r $Runtime --self-contained $selfContained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:EnableCompressionInSingleFile=$selfContained" `
    -p:DebugType=embedded -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publishing the portable executable failed.' }
$executable = Join-Path $publishRoot 'BetterScreenShot.exe'
$files = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File)
if ($files.Count -ne 1 -or $files[0].FullName -ne $executable) {
    throw 'The portable build must contain exactly one executable with no companion files.'
}
Write-Output ('Portable executable: {0} ({1:N2} MiB)' -f $executable, ((Get-Item -LiteralPath $executable).Length / 1MB))
if ($FrameworkDependent) { Write-Output "Requires the .NET 8 Windows Desktop Runtime for $Runtime on the destination PC." }
else { Write-Output 'Includes the .NET runtime. Copy this EXE to another Windows PC with the matching CPU architecture and run it.' }
