param([Parameter(Mandatory = $true)][string]$SourceDirectory, [switch]$NoStart)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceDirectory)
if (!(Test-Path -LiteralPath (Join-Path $source 'BetterScreenShot.exe'))) { throw 'Choose the published application folder.' }
$programsRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'))
$installRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot 'BetterScreenShot'))
if (!$installRoot.StartsWith($programsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid installation folder.'
}
$executable = Join-Path $installRoot 'BetterScreenShot.exe'
if (Test-Path -LiteralPath $executable) {
    Start-Process -FilePath $executable -ArgumentList '--exit' -WindowStyle Hidden -Wait
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $running = @(Get-CimInstance Win32_Process -Filter "Name = 'BetterScreenShot.exe'" | Where-Object { $_.ExecutablePath -eq $executable })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($running.Count -gt 0) { throw 'Exit BetterScreenShot from its tray menu before updating.' }
}
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $installRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination (Join-Path $installRoot 'Uninstall.ps1') -Force
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'BetterScreenShot.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $installRoot
$shortcut.Description = 'Capture screenshots and annotate images'
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name BetterScreenShot -Value ('"{0}" --background' -f $executable) -PropertyType String -Force | Out-Null
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BetterScreenShot'
New-Item -Path $uninstallKey -Force | Out-Null
$properties = @{
    DisplayName = 'BetterScreenShot'
    DisplayVersion = (Get-Item -LiteralPath $executable).VersionInfo.FileVersion
    InstallDate = [DateTime]::Now.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)
    Publisher = 'A. Peda'
    InstallLocation = $installRoot
    DisplayIcon = "$executable,0"
    UninstallString = ('"{0}" --uninstall' -f $executable)
}
foreach ($name in $properties.Keys) { New-ItemProperty -Path $uninstallKey -Name $name -Value $properties[$name] -PropertyType String -Force | Out-Null }
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
$installedBytes = (Get-ChildItem -LiteralPath $installRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value ([int][Math]::Ceiling($installedBytes / 1KB)) -PropertyType DWord -Force | Out-Null
Write-Output "Installed: $executable"
Write-Output 'Startup at sign-in is enabled. Your existing capture shortcuts are preserved.'
if (!$NoStart) { Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden }
