<#
Builds HoverDeck, packs the installer and update packages with Velopack, and publishes
them as a GitHub release. Installed copies of HoverDeck find the new version on their
next start (or Settings > Check for updates) and ask before updating.

  .\release.ps1 -Version 1.7.1 -Notes "Thinner top bar, new Close clients menu"

Options:
  -Draft        create the GitHub release as a draft (publish it yourself on GitHub)
  -LocalFeed    build into this folder instead of GitHub (for testing updates locally)

Needs: the GitHub CLI logged in (gh auth login) and vpk (dotnet tool install -g vpk).
The repository comes from <UpdateRepository> in HoverDeck.csproj.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Notes = "",
    [switch]$Draft,
    [string]$LocalFeed = "",
    [string]$PackId = "HoverDeckApp",
    [switch]$NoShortcuts
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'HoverDeck.csproj'
$work = Join-Path $PSScriptRoot 'release'
$publish = Join-Path $work "publish\$Version"

if ($LocalFeed) {
    $feed = [IO.Path]::GetFullPath($LocalFeed); $packages = $feed
} else {
    $feed = (& dotnet msbuild $project -getProperty:UpdateRepository).Trim()
    if ($feed -notmatch '^https://github\.com/[^/]+/[^/]+$') { throw "Set <UpdateRepository> in HoverDeck.csproj to your GitHub repository, e.g. https://github.com/you/HoverDeck" }
    $token = (& gh auth token 2>$null)
    if (-not $token) { throw "Log in to GitHub first: gh auth login" }
    $packages = Join-Path $work 'packages'
}
New-Item -ItemType Directory -Force $packages | Out-Null

Write-Host "Building HoverDeck $Version..."
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
& dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true "-p:Version=$Version" "-p:UpdateRepository=$feed" -o $publish
if ($LASTEXITCODE) { throw "Build failed." }

if (-not $LocalFeed) {
    # Previous release lets Velopack make a small delta update instead of a full download.
    & vpk download github --repoUrl $feed --token $token -o $packages
}

$notesFile = Join-Path $work "notes-$Version.md"
Set-Content -Path $notesFile -Value ($(if ($Notes) { $Notes } else { "HoverDeck $Version" })) -Encoding utf8
$pack = @('pack', '-u', $PackId, '-v', $Version, '-p', $publish, '-e', 'HoverDeck.exe', '--packTitle', 'HoverDeck', '--packAuthors', 'HoverDeck',
          '--framework', 'net8-x64-desktop,webview2', '--exclude', '\.(pdb|xml)$', '--releaseNotes', $notesFile, '-o', $packages)
if ($NoShortcuts) { $pack += @('--shortcuts', 'None') }
Write-Host "Packing installer and update..."
& vpk @pack
if ($LASTEXITCODE) { throw "Packing failed." }

if ($LocalFeed) { Write-Host "Done. Installer and update packages are in $packages"; return }

Write-Host "Publishing GitHub release v$Version..."
$upload = @('upload', 'github', '--repoUrl', $feed, '--token', $token, '-o', $packages, '--releaseName', "HoverDeck $Version", '--tag', "v$Version")
if (-not $Draft) { $upload += '--publish' }
& vpk @upload
if ($LASTEXITCODE) { throw "Upload failed." }
Write-Host "Done. Share this page with people: $feed/releases/latest  (they download HoverDeck-win-Setup.exe)"
