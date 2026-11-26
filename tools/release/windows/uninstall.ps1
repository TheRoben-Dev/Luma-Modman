# Removes Luma's program files, Start menu entry, Settings > Apps entry and its Nexus link handler.
# Mods, downloads and settings (%LOCALAPPDATA%\Luma, %APPDATA%\Luma) are kept unless -RemoveData.
# Mods are only ever linked into games while one runs, so game folders need no cleanup.
param([switch]$Quiet, [switch]$RemoveData)
$ErrorActionPreference = 'Continue'
$dest = $PSScriptRoot
if (-not $Quiet) {
    $answer = Read-Host "Uninstall Luma from $dest? Your mods and settings are kept. [y/N]"
    if ($answer -notmatch '^[yY]') { exit 1 }
}
Get-Process -Name Luma -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dest, 'OrdinalIgnoreCase') } | Stop-Process -Force
foreach ($lnk in @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Luma.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Luma.lnk'))) {
    if (Test-Path $lnk) { Remove-Item $lnk -Force }
}
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Luma' -Recurse -Force -ErrorAction SilentlyContinue
$nxm = (Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command' -ErrorAction SilentlyContinue).'(default)'
if ($nxm -and $nxm -like "*$dest*") { Remove-Item 'HKCU:\Software\Classes\nxm' -Recurse -Force }
if ($RemoveData) {
    Remove-Item (Join-Path $env:LOCALAPPDATA 'Luma'), (Join-Path $env:APPDATA 'Luma') -Recurse -Force -ErrorAction SilentlyContinue
}
# This script runs from the folder being removed: finish from a separate process once it exits.
Start-Process cmd.exe -ArgumentList '/c', "timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`" & rmdir /s /q `"$dest.previous`"" -WindowStyle Hidden
Write-Host 'Luma was uninstalled.'
