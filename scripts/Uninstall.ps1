$ErrorActionPreference = 'Stop'
trap {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'Could not uninstall BetterScreenShot', 'OK', 'Error') | Out-Null
    exit 1
}
$programsRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'))
$installRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot 'BetterScreenShot'))
if (!$installRoot.StartsWith($programsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) `
    -or (Split-Path -Leaf $installRoot) -ne 'BetterScreenShot') { throw 'Invalid uninstall folder.' }
if ((Test-Path -LiteralPath $installRoot) -and ((Get-Item -LiteralPath $installRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'The installation folder is a link. Remove it manually.'
}
$executable = Join-Path $installRoot 'BetterScreenShot.exe'
if (Test-Path -LiteralPath $executable) {
    Start-Process -FilePath $executable -ArgumentList '--exit' -WindowStyle Hidden -Wait
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $running = @(Get-CimInstance Win32_Process -Filter "Name = 'BetterScreenShot.exe'" | Where-Object { $_.ExecutablePath -eq $executable })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($running.Count -gt 0) { throw 'Exit BetterScreenShot before uninstalling.' }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name BetterScreenShot -ErrorAction SilentlyContinue
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'BetterScreenShot.lnk'
if (Test-Path -LiteralPath $startMenu) { Remove-Item -LiteralPath $startMenu -Force }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BetterScreenShot'
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }
$classesRoot = 'HKCU:\Software\Classes'
foreach ($extension in @('.png', '.jpg', '.jpeg', '.bmp', '.gif', '.tif', '.tiff')) {
    $progId = 'BetterScreenShot' + $extension
    $choices = Join-Path $classesRoot ($extension + '\OpenWithProgids')
    Remove-ItemProperty -LiteralPath $choices -Name $progId -ErrorAction SilentlyContinue
    $typeKey = Join-Path $classesRoot $progId
    if (Test-Path -LiteralPath $typeKey) { Remove-Item -LiteralPath $typeKey -Recurse -Force }
}
$applicationKey = Join-Path $classesRoot 'Applications\BetterScreenShot.exe'
if (Test-Path -LiteralPath $applicationKey) { Remove-Item -LiteralPath $applicationKey -Recurse -Force }
Add-Type -Namespace BetterScreenShot -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, System.IntPtr item1, System.IntPtr item2);'
[BetterScreenShot.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
if (Test-Path -LiteralPath $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
Write-Output 'BetterScreenShot was uninstalled. Saved screenshots and shortcut preferences were kept.'
