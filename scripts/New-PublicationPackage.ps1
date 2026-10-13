<#
.SYNOPSIS
Creates source and MOD ZIPs without game references, user data, or Git history.
.DESCRIPTION
Source mode uses an explicit selection of project files and needs no game DLLs.
Release and Both modes build the MOD and include only its DLL and documentation.
Existing output files are never overwritten. Extract the source ZIP into a new
directory to start a public repository without the old development history.
.PARAMETER Mode
Source, Release, or Both. The default is Both.
.PARAMETER OutputDirectory
An empty destination directory. Defaults to artifacts/publication in the project.
.PARAMETER GameDir
Game directory, or copied reference directory containing MelonLoader.
.PARAMETER MelonDir
MelonLoader directory containing net6.
.PARAMETER UnityAssembliesDir
Directory containing the generated game and Unity reference DLLs.
#>
[CmdletBinding()]
param(
    [ValidateSet('Source', 'Release', 'Both')]
    [string]$Mode = 'Both',
    [string]$OutputDirectory,
    [string]$GameDir,
    [string]$MelonDir,
    [string]$UnityAssembliesDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publicationRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Copy-PackageFile {
    <#
    .SYNOPSIS
    Copies one selected file into a package and rejects missing or linked files.
    #>
    param(
        [string]$RelativeSource,
        [string]$PackageRoot,
        [string]$RelativeDestination = $RelativeSource
    )

    $sourcePath = Join-Path $publicationRoot $RelativeSource
    $sourceFile = Get-Item -LiteralPath $sourcePath
    if ($sourceFile.PSIsContainer -or
        ($sourceFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw "Package source must be a regular file: $RelativeSource"
    }
    $destinationPath = Join-Path $PackageRoot $RelativeDestination
    $destinationParent = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath
}

function Test-PackageContents {
    <#
    .SYNOPSIS
    Ensures a package contains exactly its selected files and no forbidden data.
    #>
    param(
        [string]$PackageRoot,
        [string[]]$ExpectedFiles,
        [bool]$SourceOnly
    )

    $packagePrefix = $PackageRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $actualFiles = @(Get-ChildItem -LiteralPath $PackageRoot -File -Force -Recurse |
        ForEach-Object { $_.FullName.Substring($packagePrefix.Length).Replace('\', '/') })
    $expectedPaths = @($ExpectedFiles | ForEach-Object { $_.Replace('\', '/') })
    $differences = @(Compare-Object -ReferenceObject $expectedPaths -DifferenceObject $actualFiles)
    if ($differences.Count -ne 0) {
        throw 'Package files differ from the explicit selection. No ZIP was created.'
    }
    foreach ($relativePath in $actualFiles) {
        if ($relativePath -match '(^|/)(\.git|Libs|bin|obj|UserData)(/|$)' -or
            $relativePath -match '(^|/)GameDir\.props$' -or
            $relativePath -match '\.(log|vrm|pdb|exe)$') {
            throw "Forbidden publication file: $relativePath"
        }
        if ($SourceOnly -and $relativePath -match '\.dll$') {
            throw "Source packages must not contain DLLs: $relativePath"
        }
        if (-not $SourceOnly -and $relativePath -match '\.dll$' -and
            $relativePath -ne 'Mods/SynthRidersFullBody.dll') {
            throw "Unexpected release DLL: $relativePath"
        }
    }
}

function New-PackageZip {
    <#
    .SYNOPSIS
    Archives the validated directory, including dotfiles such as .gitignore.
    #>
    param([string]$PackageRoot, [string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $PackageRoot, $ZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

$modSource = Get-Content -LiteralPath (Join-Path $publicationRoot 'SynthRidersFullBody/FullBodyMod.cs') -Raw
$versionMatch = [regex]::Match($modSource,
    'MelonInfo\(.*?,\s*"Synth Riders Full Body",\s*"(?<version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)"')
if (-not $versionMatch.Success) {
    throw 'Could not read the MOD version from FullBodyMod.cs.'
}
$publicationVersion = $versionMatch.Groups['version'].Value

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $publicationRoot 'artifacts/publication'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    $outputItem = Get-Item -LiteralPath $OutputDirectory
    if (-not $outputItem.PSIsContainer -or
        ($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
        @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw 'OutputDirectory must be an empty, regular directory. Choose a new directory.'
    }
}

$documentationFiles = @(
    'README.md',
    'REAL_DEVICE_CHECKLIST.md',
    'docs/BUILDING.md',
    'docs/PUBLISHING.md',
    'docs/DEVELOPMENT_NOTES.md',
    'docs/ULTIMATE_TRACKER_INVESTIGATION.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md'
)
$sourceFiles = @(
    $documentationFiles
    '.gitignore'
    '.gitattributes'
    'SynthRidersFullBody.sln'
    'scripts/New-PublicationPackage.ps1'
    'SynthRidersFullBody/OpenVrLicense.txt'
)
# Only direct source files are selected; recursive copying could include Libs/obj.
foreach ($relativeDirectory in @('SynthRidersFullBody', 'tests/AvatarPoseMath', 'tests/ConfigSmoke')) {
    $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $publicationRoot $relativeDirectory) -File |
        Where-Object { $_.Extension -in @('.cs', '.csproj') } |
        ForEach-Object { "$relativeDirectory/$($_.Name)" })
}
$sourceFiles = @($sourceFiles | Sort-Object -Unique)
foreach ($relativePath in $sourceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publicationRoot $relativePath) -PathType Leaf)) {
        throw "Required publication file is missing: $relativePath"
    }
}

if ($Mode -ne 'Source') {
    $buildArguments = @(
        'build', (Join-Path $publicationRoot 'SynthRidersFullBody/SynthRidersFullBody.csproj'),
        '-c', 'Release', '--nologo'
    )
    if ($GameDir) { $buildArguments += "-p:GameDir=$GameDir" }
    if ($MelonDir) { $buildArguments += "-p:MelonDir=$MelonDir" }
    if ($UnityAssembliesDir) { $buildArguments += "-p:UnityAssembliesDir=$UnityAssembliesDir" }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed. No publication package was created.'
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$packageChecksums = @()
if ($Mode -ne 'Release') {
    $sourceName = "SynthRidersFullBody-v$publicationVersion-source"
    $sourceStage = Join-Path $OutputDirectory $sourceName
    foreach ($relativePath in $sourceFiles) {
        Copy-PackageFile -RelativeSource $relativePath -PackageRoot $sourceStage
    }
    Test-PackageContents -PackageRoot $sourceStage -ExpectedFiles $sourceFiles -SourceOnly $true
    $sourceZip = Join-Path $OutputDirectory "$sourceName.zip"
    New-PackageZip -PackageRoot $sourceStage -ZipPath $sourceZip
    $packageChecksums += "$( (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant() )  $sourceName.zip"
    Write-Output "Source: $sourceZip ($($sourceFiles.Count) files; no game DLLs or Git history)"
}

if ($Mode -ne 'Source') {
    $releaseName = "SynthRidersFullBody-v$publicationVersion-mod"
    $releaseStage = Join-Path $OutputDirectory $releaseName
    foreach ($relativePath in $documentationFiles) {
        Copy-PackageFile -RelativeSource $relativePath -PackageRoot $releaseStage
    }
    Copy-PackageFile -RelativeSource 'SynthRidersFullBody/OpenVrLicense.txt' -PackageRoot $releaseStage -RelativeDestination 'OpenVrLicense.txt'
    Copy-PackageFile -RelativeSource 'SynthRidersFullBody/bin/Release/net6.0/SynthRidersFullBody.dll' -PackageRoot $releaseStage -RelativeDestination 'Mods/SynthRidersFullBody.dll'
    $releaseFiles = @($documentationFiles) + @('OpenVrLicense.txt', 'Mods/SynthRidersFullBody.dll')
    Test-PackageContents -PackageRoot $releaseStage -ExpectedFiles $releaseFiles -SourceOnly $false
    $releaseZip = Join-Path $OutputDirectory "$releaseName.zip"
    New-PackageZip -PackageRoot $releaseStage -ZipPath $releaseZip
    $packageChecksums += "$( (Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256).Hash.ToLowerInvariant() )  $releaseName.zip"
    Write-Output "MOD: $releaseZip ($($releaseFiles.Count) files; only the MOD DLL)"
}

$checksumPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$packageChecksums, [System.Text.UTF8Encoding]::new($false))
Write-Output "Checksums: $checksumPath"
