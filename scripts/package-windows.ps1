# Baut das Windows-Paket (self-contained, x64) und entfernt Laufzeiten anderer Systeme.
#   ./scripts/package-windows.ps1 -Version 1.2.3 -Out dist
param([string]$Version = '1.0.0', [string]$Out = 'dist')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$pub = Join-Path $Out 'win-x64'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish windows/Fluester.Windows/Fluester.Windows.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=none -o $pub -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }
# Native Whisper-Bibliotheken nur für win-x64 behalten (CPU + Vulkan)
$rt = Join-Path $pub 'runtimes'
if (Test-Path $rt) {
  Get-ChildItem $rt -Directory | Where-Object { $_.Name -notin @('win-x64', 'vulkan') } | Remove-Item -Recurse -Force
  $vk = Join-Path $rt 'vulkan'
  if (Test-Path $vk) { Get-ChildItem $vk -Directory | Where-Object { $_.Name -ne 'win-x64' } | Remove-Item -Recurse -Force }
}
Get-ChildItem $pub -Recurse -Filter *.pdb | Remove-Item -Force
$zip = Join-Path $Out 'Fluester-Windows-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip -CompressionLevel Optimal
Get-Item $zip | Select-Object Name, Length
