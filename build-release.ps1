# Builds the Windows installer with Velopack.
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-release.ps1 -Version 0.1.0
# Output: .\Releases\IndustrialDuckPanic-win-Setup.exe  (installer)
#         .\Releases\IndustrialDuckPanic-win-Portable.zip (no install)
#         + the files to upload to a GitHub release for automatic updates.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }

dotnet test tests/IndustrialDuckPanic.Tests --filter "Category!=Live"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed: no release built.' }

if (Test-Path publish) { Remove-Item -Recurse -Force publish }
dotnet publish src/IndustrialDuckPanic -c Release -r win-x64 --self-contained -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet vpk pack `
  --packId IndustrialDuckPanic `
  --packVersion $Version `
  --packDir publish `
  --mainExe IndustrialDuckPanic.exe `
  --packTitle "Industrial Duck Panic" `
  --packAuthors "Industrial Duck Panic" `
  --icon src/IndustrialDuckPanic/Assets/app.ico `
  --outputDir Releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Get-ChildItem Releases | Select-Object Name, Length
