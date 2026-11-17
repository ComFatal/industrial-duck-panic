# Builds the installer and publishes it as a GitHub release, in one command.
# Usage:  powershell -ExecutionPolicy Bypass -File .\publish-release.ps1 -Version 0.2.1
#
# It uses the GitHub login Git already has (Git Credential Manager), so nothing to configure.
# Steps: check the code is committed -> push -> fetch the previous release (smaller updates)
#        -> tests + build (build-release.ps1) -> create the GitHub release "v<Version>" with the files.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Draft   # create the release as a draft (not visible to players until you publish it on GitHub)
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }

# 1. Everything must be committed, so the release matches the code on GitHub.
$dirty = git status --porcelain
if ($dirty) { throw "Uncommitted changes. Commit them first:`n$dirty" }
$tag = "v$Version"
if (git tag --list $tag) { throw "Tag $tag already exists. Pick a higher version." }

# 2. Repository and token (taken from Git's stored GitHub login, never printed).
$remote = (git remote get-url origin).Trim() -replace '\.git$', ''
if ($remote -notmatch '^https://github\.com/[^/]+/[^/]+$') { throw "origin is not a GitHub https URL: $remote" }
# Windows PowerShell 5.1 mangles text piped to native programs, so feed git from a file.
$query = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($query, "protocol=https`nhost=github.com`n`n", (New-Object System.Text.UTF8Encoding($false)))
try { $cred = cmd /c "git credential fill < `"$query`"" } finally { Remove-Item $query -ErrorAction SilentlyContinue }
$token = ($cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
if (-not $token) { throw 'No GitHub login found. Run "git push" once (it opens the GitHub login), then retry.' }

Write-Host "Pushing main to $remote ..."
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw 'git push failed' }

# 3. Previous release, so Velopack can build a small delta update.
if (Test-Path Releases) { Remove-Item -Recurse -Force Releases }
dotnet tool restore | Out-Null
dotnet vpk download github --repoUrl $remote --token $token --outputDir Releases
if ($LASTEXITCODE -ne 0) { Write-Host 'No previous release downloaded (normal for the first one).' }

# 4. Tests + build + installer.
& "$PSScriptRoot\build-release.ps1" -Version $Version

# 5. Create the GitHub release and upload the files.
$publishArgs = @('vpk', 'upload', 'github',
    '--repoUrl', $remote, '--token', $token, '--outputDir', 'Releases',
    '--tag', $tag, '--releaseName', "Industrial Duck Panic $Version",
    '--targetCommitish', (git rev-parse HEAD).Trim())
if (-not $Draft) { $publishArgs += '--publish' }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Upload to GitHub failed' }

git fetch --tags --quiet
Write-Host ''
Write-Host "Done: $remote/releases/tag/$tag"
