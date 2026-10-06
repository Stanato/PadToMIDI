param([switch]$SourceOnly)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$metadata = Get-Content -LiteralPath (Join-Path $workspace 'release/version.json') -Raw | ConvertFrom-Json
if ($metadata.version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Invalid release version.' }
$artifacts = Join-Path $workspace 'artifacts'
$output = Join-Path $artifacts ('github-release/' + $metadata.version)
$work = Join-Path $artifacts ('github-release-work/' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'PadToMIDI'
[void][IO.Directory]::CreateDirectory($source)
[void][IO.Directory]::CreateDirectory($output)

function Assert-Artifact([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Not a workspace artifact: $full" }
    return $full
}
function Copy-SourceFile([IO.FileInfo]$File) {
    $relative = $File.FullName.Substring($workspace.Length + 1)
    if ($File.Length -gt 25MB) { throw "Unexpected large source file: $relative" }
    $destination = Join-Path $source $relative
    [void][IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    Copy-Item -LiteralPath $File.FullName -Destination $destination
}
$rootFiles = @('.gitignore','.gitattributes','.editorconfig','PadToMIDI.sln','Directory.Build.props',
    'Directory.Packages.props','global.json','NuGet.Config','README.md','LICENSE.txt','CONTRIBUTING.md','CHANGELOG.md')
foreach ($file in $rootFiles) { Copy-SourceFile (Get-Item -LiteralPath (Join-Path $workspace $file) -Force) }
$extensions = @('.cs','.csproj','.axaml','.json','.md','.ps1','.nsi','.nsh','.yml','.yaml','.txt','.csv','.rtf','.ico','.png','.svg','.manifest')
foreach ($directory in @('src','tests','tools','docs','release','vendor','.github','.vscode')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspace $directory) -File -Recurse -Force) {
        $relative = $file.FullName.Substring($workspace.Length + 1)
        if ($relative -match '(^|\\)(bin|obj|TestResults)(\\|$)' -or $file.Extension -eq '.nupkg') { continue }
        if ($extensions -notcontains $file.Extension.ToLowerInvariant()) { throw "Unreviewed source file type: $relative" }
        Copy-SourceFile $file
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$sourceZip = Join-Path $output ("PadToMIDI-$($metadata.version)-source.zip")
$temporary = $sourceZip + '.' + [guid]::NewGuid().ToString('N') + '.zip'
try {
    # Explicit entries retain dotfiles and use ZIP-standard separators on Windows PowerShell.
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force) {
            $relative = $file.FullName.Substring($source.Length + 1).Replace('\','/')
            $entry = $zip.CreateEntry('PadToMIDI/' + $relative, [IO.Compression.CompressionLevel]::Optimal)
            $input = [IO.File]::OpenRead($file.FullName)
            $stream = $entry.Open()
            try { $input.CopyTo($stream) } finally { $stream.Dispose(); $input.Dispose() }
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $sourceZip -Force
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
$archive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
try {
    $names = @($archive.Entries.FullName)
    foreach ($required in @('PadToMIDI/.gitignore','PadToMIDI/.github/workflows/ci.yml','PadToMIDI/README.md','PadToMIDI/LICENSE.txt','PadToMIDI/tools/Restore-MidiSdk.ps1')) {
        if ($names -notcontains $required) { throw "Source archive missing $required" }
    }
    if ($names | Where-Object { $_ -match '(^|/)(bin|obj|artifacts|\.tools|TestResults)/|\.nupkg$|\.(pfx|p12|key)$' }) {
        throw 'Source archive contains excluded files.'
    }
} finally { $archive.Dispose() }
if ($SourceOnly) { Write-Output "Source archive ready: $sourceZip"; return }

$release = Join-Path $artifacts ('releases/' + $metadata.version)
$build = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
if ($build.version -ne $metadata.version) { throw 'Application and source release versions differ.' }
foreach ($line in Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    $file = Assert-Artifact (Join-Path $release $name)
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "Archive checksum mismatch: $name" }
}
$installer = Assert-Artifact $build.installer
$portable = Assert-Artifact $build.portable
$expanded = Join-Path $work 'portable'
[IO.Compression.ZipFile]::ExtractToDirectory($portable, $expanded)
$payload = Join-Path $expanded 'PadToMIDI'
$manifest = Get-Content -LiteralPath (Join-Path $payload 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $metadata.version) { throw 'Portable version differs.' }
foreach ($entry in $manifest.files) {
    $file = [IO.Path]::GetFullPath((Join-Path $payload $entry.path))
    if (-not $file.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-Item -LiteralPath $file).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Portable payload mismatch: $($entry.path)"
    }
}
foreach ($notice in @(@{Source='LICENSE.txt';Payload='LICENSE.txt'},
    @{Source='release/THIRD-PARTY-NOTICES.txt';Payload='THIRD-PARTY-NOTICES.txt'})) {
    if ((Get-FileHash -LiteralPath (Join-Path $workspace $notice.Source)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $payload $notice.Payload)).Hash) {
        throw "Rebuild the packages to include the current $($notice.Payload)."
    }
}
$downloads = @()
foreach ($file in @($installer,$portable)) {
    $destination = Join-Path $output ([IO.Path]::GetFileName($file))
    Copy-Item -LiteralPath $file -Destination $destination -Force
    $downloads += $destination
}
$downloads += $sourceZip
Copy-Item -LiteralPath (Join-Path $workspace 'release/RELEASE-NOTES.md') -Destination (Join-Path $output 'RELEASE-NOTES.md') -Force
$downloads | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash)  $([IO.Path]::GetFileName($_))" } |
    Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "GitHub preview assets ready: $output"
Write-Output 'Upload only the three downloads and SHA256SUMS.txt; use RELEASE-NOTES.md as the release description.'
