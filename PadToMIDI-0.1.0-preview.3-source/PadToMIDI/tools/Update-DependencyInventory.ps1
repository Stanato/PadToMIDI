param([string]$PackageRoot = $env:NUGET_PACKAGES)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (-not $PackageRoot) { $PackageRoot = Join-Path $env:USERPROFILE '.nuget/packages' }
$packages = @{}
foreach ($assets in Get-ChildItem (Join-Path $workspace 'src'), (Join-Path $workspace 'tests'), (Join-Path $workspace 'tools') -Recurse -Filter project.assets.json) {
    $graph = Get-Content -LiteralPath $assets.FullName -Raw | ConvertFrom-Json
    foreach ($library in $graph.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package') { $packages[$library.Name] = $true }
    }
    foreach ($framework in $graph.project.frameworks.PSObject.Properties) {
        foreach ($download in $framework.Value.downloadDependencies) {
            $version = $download.version.Trim([char[]]'[]').Split(',')[0].Trim()
            $packages[$download.name + '/' + $version] = $true
        }
    }
}
$inventory = foreach ($identity in $packages.Keys) {
    $id, $version = $identity.Split('/')
    $directory = Join-Path $PackageRoot ($id.ToLowerInvariant() + '/' + $version)
    $nuspec = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' | Select-Object -First 1
    [xml]$manifest = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $manifest.SelectSingleNode('//*[local-name()="metadata"]')
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $licenseUrl = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
    $projectUrl = $metadata.SelectSingleNode('*[local-name()="projectUrl"]')
    $licenseValue = if ($license) { $license.InnerText } else { 'See package license URL' }
    $licenseType = if ($license) { $license.type } else { 'url' }
    [PSCustomObject]@{
        Id = $id; Version = $version; License = $licenseValue; LicenseType = $licenseType
        LicenseUrl = $licenseUrl.InnerText; Project = $projectUrl.InnerText
    }
}
$inventory | Sort-Object Id, Version | Export-Csv -LiteralPath (Join-Path $workspace 'docs/dependency-inventory.csv') -NoTypeInformation -Encoding UTF8
Write-Output "Recorded $($packages.Count) dependencies."
