param(
    [string]$Dotnet = 'dotnet',
    [string]$NsisCompiler = '',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$metadata = Get-Content -LiteralPath (Join-Path $workspace 'release/version.json') -Raw | ConvertFrom-Json
if ($metadata.version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Invalid release version.' }
$releaseRoot = Join-Path $workspace ('artifacts/releases/' + $metadata.version)
$work = Join-Path $releaseRoot ('work-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'PadToMIDI'
[void][IO.Directory]::CreateDirectory($payload)
if (-not $NsisCompiler) {
    $localCompiler = Join-Path $workspace '.tools/nsis-download/nsis-3.11/makensis.exe'
    $NsisCompiler = if (Test-Path -LiteralPath $localCompiler) { $localCompiler } else { (Get-Command makensis.exe -ErrorAction Stop).Source }
}
function Invoke-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
Push-Location $workspace
try {
    & (Join-Path $workspace 'tools/Restore-MidiSdk.ps1')
    Invoke-Dotnet @('restore','PadToMIDI.sln','--locked-mode')
    Invoke-Dotnet @('build','PadToMIDI.sln','-c','Release','--no-restore')
    if (-not $SkipTests) {
        foreach ($project in @('Core','App','Midi.Windows')) {
            Invoke-Dotnet @('test','--project',"tests/PadToMIDI.$project.Tests",'-c','Release','--no-build','--no-restore')
        }
    }
    # Keep native MIDI DLL/PRI and SDL app-local. Trimming/single-file risk WinRT and native discovery.
    Invoke-Dotnet @('publish','src/PadToMIDI.App','-c','Release','-r','win-x64','--self-contained','true',
        '-p:RestoreLockedMode=true','-p:NuGetLockFilePath=packages.win-x64.lock.json',
        '-p:PublishTrimmed=false','-p:PublishSingleFile=false','-p:DebugType=None','-p:DebugSymbols=false','-o',$payload)
    foreach ($required in @('PadToMIDI.exe','PadToMIDI.dll','coreclr.dll','SDL3.dll','Windows.Devices.Midi2.dll','Windows.Devices.Midi2.pri','Profiles/default-c-major.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing packaged dependency: $required" }
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $payload 'licenses'))
    Copy-Item -LiteralPath (Join-Path $workspace 'release/QUICK-START.txt') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $workspace 'release/THIRD-PARTY-NOTICES.txt') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE.txt') -Destination $payload
    foreach ($file in Get-ChildItem (Join-Path $workspace 'docs/licenses') -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $payload 'licenses') }
    Copy-Item -LiteralPath (Join-Path $workspace 'docs/dependency-inventory.csv') -Destination $payload
    $compilerDirectory = Split-Path $NsisCompiler -Parent
    Copy-Item -LiteralPath (Join-Path $compilerDirectory 'COPYING') -Destination (Join-Path $payload 'licenses/NSIS.txt')
    # Runtime pack includes its own license/notices; preserve the notices from this exact distribution.
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $runtimeConfiguration = Get-Content -LiteralPath (Join-Path $payload 'PadToMIDI.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = ($runtimeConfiguration.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    if ($runtimeVersion -ne $metadata.runtimeVersion) { throw "Expected runtime $($metadata.runtimeVersion), found $runtimeVersion. Review release metadata when changing SDK/runtime." }
    $runtimePack = Join-Path $packageRoot ('microsoft.netcore.app.runtime.win-x64/' + $runtimeVersion)
    foreach ($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $source = Join-Path $runtimePack $notice
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing .NET runtime notice: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $payload ('licenses/DotNet-' + $notice))
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'PadToMIDI.exe'))
    if ($versionInfo.FileVersion -ne $metadata.fileVersion -or $versionInfo.ProductVersion.Split('+')[0] -ne $metadata.version) { throw 'Release/version.json and application version differ.' }
    $files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName)
    $entries = @($files | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($payload.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes=$_.Length }
    })
    $manifest = [ordered]@{ version=$metadata.version; platform='win-x64'; minimumWindowsBuild=$metadata.minimumWindowsBuild; runtime=$runtimeVersion;
        midiApi='0.99.88-preview.10'; sdl='3.5.0 development'; selfContained=$true; files=$entries }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $payload 'release-manifest.json') -Encoding UTF8
    $uninstall = Join-Path $work 'uninstall-files.nsh'
    $lines = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
        $relative=$_.FullName.Substring($payload.Length + 1)
        if ($relative -match '["$\r\n]') { throw 'Unsupported payload filename.' }
        'Delete "$INSTDIR\' + $relative + '"'
    })
    $lines += @(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        'RMDir "$INSTDIR\' + $_.FullName.Substring($payload.Length + 1) + '"'
    })
    $lines | Set-Content -LiteralPath $uninstall -Encoding UTF8
    $installer = Join-Path $releaseRoot ("PadToMIDI-$($metadata.version)-win-x64-setup.exe")
    & $NsisCompiler '/V2' "/DVERSION=$($metadata.version)" "/DFILEVERSION=$($metadata.fileVersion)" "/DMINBUILD=$($metadata.minimumWindowsBuild)" "/DPAYLOAD=$payload" "/DOUTPUT=$installer" "/DUNINSTALLFILES=$uninstall" "/DICON=$workspace/src/PadToMIDI.App/Assets/app.ico" (Join-Path $workspace 'release/PadToMIDI.nsi')
    if ($LASTEXITCODE -ne 0) { throw "Installer compiler failed ($LASTEXITCODE)." }
    $zip = Join-Path $releaseRoot ("PadToMIDI-$($metadata.version)-win-x64.zip")
    Compress-Archive -LiteralPath $payload -DestinationPath $zip -CompressionLevel Optimal -Force
    @($installer,$zip) | ForEach-Object { $hash=Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash)  $([IO.Path]::GetFileName($_))" } |
        Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ASCII
    [ordered]@{ version=$metadata.version; payload=$payload; installer=$installer; portable=$zip } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $releaseRoot 'build.json') -Encoding UTF8
    Write-Output "Release ready: $releaseRoot"
} finally { Pop-Location }
