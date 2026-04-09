<#
.SYNOPSIS
    Computes the next semantic version from git commit messages (Conventional Commits)
    and writes Version.props so the build pipeline picks it up automatically.

.DESCRIPTION
    Reads all commits since the last vX.Y.Z tag, classifies each one:
      - BREAKING CHANGE / feat!: / fix!:  → bump MAJOR
      - feat:                              → bump MINOR
      - fix: / perf: / refactor: / etc.    → bump PATCH

    If no version tag exists yet, starts from 0.0.0.
    Writes the result to vsix-companion\Version.props.

.PARAMETER DryRun
    Show what would happen without writing Version.props.

.EXAMPLE
    .\auto-version.ps1
    .\auto-version.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$AlwaysBumpPatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    Write-Error "Not inside a git repository."
    exit 1
}

$propsPath = Join-Path $repoRoot 'vsix-companion\Version.props'

function Get-CurrentVersionFromProps {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @{ Major = 0; Minor = 0; Patch = 0 }
    }

    [xml]$xml = Get-Content -Path $Path -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

    $majNode = $xml.SelectSingleNode('//msb:VersionMajor', $ns)
    $minNode = $xml.SelectSingleNode('//msb:VersionMinor', $ns)
    $patNode = $xml.SelectSingleNode('//msb:VersionPatch', $ns)

    $maj = if ($majNode) { [int]$majNode.InnerText } else { 0 }
    $min = if ($minNode) { [int]$minNode.InnerText } else { 0 }
    $pat = if ($patNode) { [int]$patNode.InnerText } else { 0 }

    return @{ Major = $maj; Minor = $min; Patch = $pat }
}

$useGitBasedVersioning = -not $AlwaysBumpPatch

if ($AlwaysBumpPatch) {
    $current = Get-CurrentVersionFromProps -Path $propsPath
    $major = [int]$current.Major
    $minor = [int]$current.Minor
    $patch = [int]$current.Patch + 1

    Write-Host "AlwaysBumpPatch enabled."
    Write-Host "Current version from Version.props: $($current.Major).$($current.Minor).$($current.Patch)"
    Write-Host "Next version: $major.$minor.$patch"
}

if ($useGitBasedVersioning) {
    # --- Find the latest version tag ---
    $lastTag = git describe --tags --match "v[0-9]*" --abbrev=0 2>$null
    if ($lastTag) {
        if ($lastTag -match '^v(\d+)\.(\d+)\.(\d+)') {
            $major = [int]$Matches[1]
            $minor = [int]$Matches[2]
            $patch = [int]$Matches[3]
        }
        else {
            Write-Error "Tag '$lastTag' doesn't match vMAJOR.MINOR.PATCH"
            exit 1
        }
        $commitRange = "$lastTag..HEAD"
        Write-Host "Last version tag: $lastTag ($major.$minor.$patch)"
    }
    else {
        $major = 0; $minor = 0; $patch = 0
        $commitRange = $null  # all commits
        Write-Host "No version tag found. Starting from 0.0.0"
    }

    # --- Read commits since last tag ---
    if ($commitRange) {
        $commits = git log $commitRange --pretty=format:"%s%n%b" --no-merges 2>$null
    }
    else {
        $commits = git log --pretty=format:"%s%n%b" --no-merges 2>$null
    }

    if (-not $commits -or $null -eq ($commits | Where-Object { $_.Trim() })) {
        Write-Host "No new commits since $lastTag. Version stays at $major.$minor.$patch"
        $bumpType = 'none'
    }
    else {
        $commitText = ($commits | Out-String)
        $lines = $commitText -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

        # --- Classify commits ---
        $hasBreaking = $false
        $hasFeat = $false
        $hasFix = $false

        foreach ($line in $lines) {
            # BREAKING CHANGE in body or footer
            if ($line -match '(?i)^BREAKING[- ]CHANGE') {
                $hasBreaking = $true
                continue
            }
            # Subject line with ! before colon (e.g., feat!: or fix!:)
            if ($line -match '(?i)^\w+(\(.+\))?!\s*:') {
                $hasBreaking = $true
                continue
            }
            # feat: or feature:
            if ($line -match '(?i)^feat(\(.+\))?\s*:') {
                $hasFeat = $true
                continue
            }
            # fix, perf, refactor, build, ci, docs, style, test, chore
            if ($line -match '(?i)^(fix|perf|refactor|build|ci|docs|style|test|chore)(\(.+\))?\s*:') {
                $hasFix = $true
                continue
            }
            # Non-conventional commit messages count as patch
            if ($line -match '(?i)^(add|update|remove|change|improve|correct|bump)') {
                $hasFix = $true
            }
        }

        # --- Determine bump ---
        if ($hasBreaking) {
            $bumpType = 'major'
        }
        elseif ($hasFeat) {
            $bumpType = 'minor'
        }
        elseif ($hasFix) {
            $bumpType = 'patch'
        }
        else {
            $bumpType = 'patch'  # default: any commit = at least a patch
        }

        Write-Host "Commits analyzed: breaking=$hasBreaking, feat=$hasFeat, fix=$hasFix"
        Write-Host "Bump type: $bumpType"
    }

    # --- Apply bump ---
    switch ($bumpType) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
        'none'  { <# no change #> }
    }

    $newVersion = "$major.$minor.$patch"
    Write-Host "Next version: $newVersion"
}

# --- Write Version.props ---

$propsContent = @"
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!--
    AUTO-GENERATED by auto-version.ps1 from git commit history.
    Do not edit manually — commit messages drive the version.

    Conventional Commits cheat-sheet:
      feat: ...           -> bumps MINOR
      fix: ...            -> bumps PATCH
      feat!: ...          -> bumps MAJOR
      BREAKING CHANGE     -> bumps MAJOR (in commit body)
      perf/refactor/etc.  -> bumps PATCH
  -->
  <PropertyGroup>
    <VersionMajor>$major</VersionMajor>
    <VersionMinor>$minor</VersionMinor>
    <VersionPatch>$patch</VersionPatch>
    <VersionSuffix></VersionSuffix>

    <!-- Derived — do not edit -->
    <ExtensionVersion>`$(VersionMajor).`$(VersionMinor).`$(VersionPatch)</ExtensionVersion>
    <AssemblyVersion>`$(VersionMajor).`$(VersionMinor).`$(VersionPatch).0</AssemblyVersion>
    <FileVersion>`$(VersionMajor).`$(VersionMinor).`$(VersionPatch).0</FileVersion>
    <InformationalVersion Condition="'`$(VersionSuffix)' == ''">`$(ExtensionVersion)</InformationalVersion>
    <InformationalVersion Condition="'`$(VersionSuffix)' != ''">`$(ExtensionVersion)-`$(VersionSuffix)</InformationalVersion>
  </PropertyGroup>
</Project>
"@

if ($DryRun) {
    Write-Host "`n[DRY RUN] Would write to: $propsPath"
    Write-Host $propsContent
}
else {
    Set-Content -Path $propsPath -Value $propsContent -Encoding UTF8
    Write-Host "Wrote $propsPath"
}
