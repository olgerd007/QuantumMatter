[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][bool]$Prerelease
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$properties = [xml][IO.File]::ReadAllText((Join-Path $repositoryRoot 'Directory.Build.props'))
$version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($Tag -cne "v$version") {
    throw "Release tag must be v$version and match the committed package version."
}
if ($Prerelease -ne $version.Contains('-')) {
    throw 'The GitHub prerelease flag must match the package version suffix.'
}
Write-Output "Release version and type validated: $version"
