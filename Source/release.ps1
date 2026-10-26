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
          '--framework', 'net8-x64-desktop,webview2', '--icon', (Join-Path $PSScriptRoot 'Assets\HoverDeck.ico'), '--exclude', '\.(pdb|xml)$', '--releaseNotes', $notesFile, '-o', $packages)
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
# Friendlier download names. Updates only use releases.win.json and the .nupkg files.
$repoPath = $feed -replace '^https://github\.com/', ''
# Listed through the releases API, which (unlike the tag lookup) also finds a draft.
$release = & gh api "repos/$repoPath/releases?per_page=100" | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object tag_name -eq "v$Version" | Select-Object -First 1
if ($LASTEXITCODE -or -not $release) { throw "Release v$Version was uploaded, but GitHub did not list it; rename its download files on GitHub by hand." }
foreach ($rename in @(@("$PackId-win-Setup.exe", 'HoverDeck-Setup.exe'), @("$PackId-win-Portable.zip", 'HoverDeck-Portable.zip'))) {
    $asset = $release.assets | Where-Object name -eq $rename[0]
    if (-not $asset) { throw "Release v$Version has no $($rename[0]) to rename." }
    & gh api -X PATCH "repos/$repoPath/releases/assets/$($asset.id)" -f "name=$($rename[1])" | Out-Null
    if ($LASTEXITCODE) { throw "Could not rename $($rename[0]) on GitHub." }
}
if ($Draft) { Write-Host "Done. Draft release v$Version is ready; publish it on GitHub when you are."; return }
Write-Host "Done. Share this page with people: $feed/releases/latest  (they download HoverDeck-Setup.exe)"
