param([string]$DestinationDirectory = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (-not $DestinationDirectory) { $DestinationDirectory = Join-Path $workspace 'vendor/nuget' }
$DestinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
$metadata = Get-Content -LiteralPath (Join-Path $workspace 'vendor/nuget/sdk.json') -Raw | ConvertFrom-Json
if ($metadata.fileName -notmatch '^[a-zA-Z0-9.-]+\.nupkg$' -or $metadata.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
    $metadata.url -notlike 'https://github.com/microsoft/MIDI/releases/download/*') { throw 'Invalid MIDI SDK download metadata.' }
[void][IO.Directory]::CreateDirectory($DestinationDirectory)
$destination = Join-Path $DestinationDirectory $metadata.fileName
if (Test-Path -LiteralPath $destination) {
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $metadata.sha256) {
        throw "MIDI SDK checksum mismatch: $destination. Remove the invalid file and retry."
    }
    Write-Output 'MIDI SDK present; SHA-256 verified.'
    return
}
$temporary = $destination + '.' + [guid]::NewGuid().ToString('N') + '.download'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $metadata.url -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $metadata.sha256) {
        throw 'Downloaded MIDI SDK checksum mismatch. The package was not installed.'
    }
    Move-Item -LiteralPath $temporary -Destination $destination -ErrorAction Stop
    Write-Output 'MIDI SDK downloaded from Microsoft; SHA-256 verified.'
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
