param([string]$ReleaseDirectory = '')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifacts = Join-Path $workspace 'artifacts'
function Assert-WorkspaceArtifact([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Not a workspace artifact: $resolved" }
    return $resolved
}
if (-not $ReleaseDirectory) {
    $version = (Get-Content (Join-Path $workspace 'release/version.json') -Raw | ConvertFrom-Json).version
    $ReleaseDirectory = Join-Path $artifacts "releases/$version"
}
$ReleaseDirectory = Assert-WorkspaceArtifact $ReleaseDirectory
$build = Get-Content (Join-Path $ReleaseDirectory 'build.json') -Raw | ConvertFrom-Json
$installer = Assert-WorkspaceArtifact $build.installer
$portable = Assert-WorkspaceArtifact $build.portable
foreach ($line in Get-Content (Join-Path $ReleaseDirectory 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    $file = Assert-WorkspaceArtifact (Join-Path $ReleaseDirectory $name)
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "Archive checksum mismatch: $name" }
}
$fixture = Assert-WorkspaceArtifact (Join-Path $artifacts ('release-test-' + [guid]::NewGuid().ToString('N')))
[void][IO.Directory]::CreateDirectory($fixture)
$working = Join-Path $fixture 'unrelated-working-directory'
[void][IO.Directory]::CreateDirectory($working)
Expand-Archive -LiteralPath $portable -DestinationPath (Join-Path $fixture 'portable')
$payload = Join-Path $fixture 'portable/PadToMIDI'
$install = Assert-WorkspaceArtifact (Join-Path $fixture 'installed app')
$profiles = Join-Path $fixture 'profiles'
[void][IO.Directory]::CreateDirectory($profiles)
Set-Content -LiteralPath (Join-Path $profiles 'preserve.txt') -Value 'User profiles remain outside the install directory.'
function Verify-Payload([string]$Directory) {
    $manifest = Get-Content (Join-Path $Directory 'release-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $file = [IO.Path]::GetFullPath((Join-Path $Directory $entry.path))
        if (-not $file.StartsWith($Directory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
        if ((Get-Item -LiteralPath $file).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256) { throw "Payload mismatch: $($entry.path)" }
    }
}
function Run-Checked([string]$Executable, [string]$Arguments, [int]$Expected = 0) {
    $id = [guid]::NewGuid().ToString('N')
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable; $start.Arguments = $Arguments; $start.WorkingDirectory = $working
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Process timed out: $Executable" }
    $output = $stdout.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $fixture "$id.out"), $output)
    [IO.File]::WriteAllText((Join-Path $fixture "$id.err"), $stderr.GetAwaiter().GetResult())
    $exitCode = $process.ExitCode; $process.Dispose()
    if ($exitCode -ne $Expected) { throw "Expected exit $Expected, got ${exitCode}: $Executable. See $fixture/$id.err" }
    if ($Arguments -eq '--version' -and -not $output.StartsWith('PadToMIDI ')) { throw 'Incorrect packaged application identity.' }
}
Verify-Payload $payload
$oldRoot = $env:DOTNET_ROOT; $oldX64 = $env:DOTNET_ROOT_X64
$app = $null
try {
    # An invalid global runtime location proves that the portable app uses its bundled runtime.
    $env:DOTNET_ROOT = Join-Path $fixture 'no-runtime'; $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    Run-Checked (Join-Path $payload 'PadToMIDI.exe') '--version'
    Run-Checked (Join-Path $payload 'PadToMIDI.exe') ('--smoke-test --profile-directory "' + $profiles + '"')
    # A build from before the rename must not run concurrently with the renamed app.
    $previousInstance = New-Object Threading.Mutex($false, 'Local\GamepadMidi.Desktop')
    try {
        Run-Checked (Join-Path $payload 'PadToMIDI.exe') '--smoke-test' 3
        Run-Checked $installer ('/S /TESTMODE /D=' + $install) 5
    } finally { $previousInstance.Dispose() }
    Run-Checked $installer ('/S /TESTMODE /D=' + $install)
    Verify-Payload $install
    Set-Content -LiteralPath (Join-Path $install 'foreign-user-file.txt') -Value 'Keep this file during upgrade and uninstall.'
    Run-Checked $installer ('/S /TESTMODE /D=' + $install)
    Verify-Payload $install
    Run-Checked (Join-Path $install 'PadToMIDI.exe') ('--smoke-test --profile-directory "' + $profiles + '"')
    $app = Start-Process -FilePath (Join-Path $install 'PadToMIDI.exe') -ArgumentList ('--profile-directory "' + $profiles + '"') -WorkingDirectory $working -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100; $app.Refresh()
        if ($app.HasExited) { throw 'Installed application exited before showing its window.' }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Installed application did not open a window.' }
    } while ($app.MainWindowHandle -eq 0)
    Run-Checked (Join-Path $install 'PadToMIDI.exe') '--smoke-test' 3
    Run-Checked $installer ('/S /TESTMODE /D=' + $install) 5
    Run-Checked (Join-Path $install 'Uninstall.exe') ('/S _?=' + $install) 5
    Verify-Payload $install
    [void]$app.CloseMainWindow()
    if (-not $app.WaitForExit(20000)) { throw 'Installed application did not shut down gracefully.' }
    Run-Checked (Join-Path $install 'Uninstall.exe') '/S'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    # NSIS spawns a temporary uninstaller. Its parent can exit before the child
    # finishes deleting DLLs, so wait for the entire known payload, not just app.exe.
    $knownFiles = @((Get-Content (Join-Path $payload 'release-manifest.json') -Raw | ConvertFrom-Json).files.path) +
        @('release-manifest.json', 'Uninstall.exe', 'installer-test-mode')
    do {
        $remaining = @($knownFiles | Where-Object { Test-Path -LiteralPath (Join-Path $install $_) })
        if ($remaining.Count -eq 0) { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Uninstall left files: $($remaining -join ', ')" }
        Start-Sleep -Milliseconds 250
    } while ($true)
    if (-not (Test-Path (Join-Path $install 'foreign-user-file.txt')) -or -not (Test-Path (Join-Path $profiles 'preserve.txt'))) { throw 'Uninstall removed user data.' }
    Write-Output "PASS: archive/payload integrity, bundled runtime, portable and installed startup, upgrade, single instance, running-app install/uninstall guards, graceful shutdown, uninstall and user-data preservation. Fixtures: $fixture"
    Write-Output 'TESTMODE avoids changing user shortcuts and registry; check normal Start Menu/Installed Apps integration manually.'
} finally {
    if ($app -and -not $app.HasExited) { [void]$app.CloseMainWindow(); if (-not $app.WaitForExit(5000)) { $app.Kill() } }
    $env:DOTNET_ROOT = $oldRoot; $env:DOTNET_ROOT_X64 = $oldX64
}
