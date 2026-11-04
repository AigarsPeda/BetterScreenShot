param([Parameter(Mandatory = $true)][string]$PortfolioDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$portfolioRoot = [IO.Path]::GetFullPath($PortfolioDirectory)
if (!(Test-Path -LiteralPath (Join-Path $portfolioRoot 'package.json'))) { throw 'Choose the portfolio project folder.' }
$downloadRoot = [IO.Path]::GetFullPath((Join-Path $portfolioRoot 'public\downloads\betterscreenshot'))
if (!$downloadRoot.StartsWith($portfolioRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid portfolio download folder.'
}
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\portfolio-release'))
if (!$releaseRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid release folder.'
}
dotnet publish (Join-Path $projectRoot 'BetterScreenShot.csproj') -c Release -r win-x64 --self-contained true `
    -p:AutoInstallOnLaunch=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded -o $releaseRoot
if ($LASTEXITCODE -ne 0) { throw 'Publishing BetterScreenShot failed.' }
$executable = Join-Path $releaseRoot 'BetterScreenShot.exe'
$version = (Get-Item -LiteralPath $executable).VersionInfo.FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Set a four-part application file version before publishing.' }
$manifest = [ordered]@{
    version = $version
    sizeBytes = (Get-Item -LiteralPath $executable).Length
    sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
}
$manifestPath = Join-Path $releaseRoot 'latest.json'
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $downloadRoot 'BetterScreenShot.exe') -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $downloadRoot 'latest.json') -Force
Write-Output "Portfolio release ${version}: $downloadRoot"
