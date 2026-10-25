[CmdletBinding()]
param(
    [ValidateSet('net9.0', 'net10.0')][string]$ConsumerFramework = 'net9.0',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$PackageVersion,
    [string]$ExpectedCommit,
    [switch]$ValidateObservations,
    [switch]$ValidateDynamicCircuits,
    [switch]$EnableCuda
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$properties = [xml][IO.File]::ReadAllText((Join-Path $repositoryRoot 'Directory.Build.props'))
if (!$PackageVersion) { $PackageVersion = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText }
$consumerDirectory = Join-Path $repositoryRoot ('artifacts/published-smoke/' + [guid]::NewGuid().ToString('N'))
$cacheDirectory = Join-Path $consumerDirectory 'packages'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

New-Item -ItemType Directory -Path $consumerDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $repositoryRoot 'samples/PackageConsumer/*') -Destination $consumerDirectory
# Only package references and nuget.org may supply the SDK under test.
[IO.File]::WriteAllText((Join-Path $consumerDirectory 'Directory.Build.props'), '<Project />')
[IO.File]::WriteAllText((Join-Path $consumerDirectory 'Directory.Packages.props'),
    '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $consumerDirectory 'NuGet.Config'),
    '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
if ($ConsumerFramework -eq 'net10.0') {
    [IO.File]::WriteAllText((Join-Path $consumerDirectory 'global.json'),
        '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }')
}

Push-Location $consumerDirectory
try {
    $versionArgument = "-p:QuantumMatterPackageVersion=$packageVersion"
    $frameworkArgument = "-p:TargetFramework=$ConsumerFramework"
    # Bypass cached pre-indexing 404 responses as well as the shared package cache.
    Invoke-DotNet -Arguments @('restore', 'PackageConsumer.csproj', '--configfile', 'NuGet.Config',
        '--packages', 'packages', '--no-http-cache', $versionArgument, $frameworkArgument)
    foreach ($packageId in @('quantummatter.core', 'quantummatter.application', 'quantummatter.infrastructure')) {
        $packageRoot = Join-Path $consumerDirectory "packages/$packageId/$packageVersion"
        $metadata = [IO.File]::ReadAllText((Join-Path $packageRoot '.nupkg.metadata')) | ConvertFrom-Json
        if ($metadata.source -ne 'https://api.nuget.org/v3/index.json') {
            throw "$packageId came from an unexpected source."
        }
        if ($ExpectedCommit) {
            $nuspec = [xml][IO.File]::ReadAllText((Join-Path $packageRoot "$packageId.nuspec"))
            $repository = $nuspec.SelectSingleNode('//*[local-name()="repository"]')
            if (!$repository -or $repository.commit -ne $ExpectedCommit) {
                throw "$packageId was not built from the expected release commit."
            }
        }
    }
    Invoke-DotNet -Arguments @('run', '--project', 'PackageConsumer.csproj', '--no-restore', '-c', 'Release',
        $versionArgument, $frameworkArgument, '--', '--expected-sdk-version', $PackageVersion)
    if ($EnableCuda) {
        Invoke-DotNet -Arguments @('run', '--project', 'PackageConsumer.csproj', '--no-build', '--no-restore',
            '-c', 'Release', $versionArgument, $frameworkArgument, '--', '--cuda', '--expected-sdk-version', $PackageVersion)
    }
    $additionalSamples = @()
    if ($ValidateObservations) { $additionalSamples += @{ Name = 'ObservationConsumer'; Suffix = '-observations' } }
    if ($ValidateDynamicCircuits) { $additionalSamples += @{ Name = 'DynamicConsumer'; Suffix = '-dynamic' } }
    foreach ($sample in $additionalSamples) {
        # Keep sources separate from the baseline consumer's default compile globs.
        $sampleDirectory = $consumerDirectory + $sample.Suffix
        New-Item -ItemType Directory -Path $sampleDirectory | Out-Null
        Copy-Item -Path (Join-Path $repositoryRoot "samples/$($sample.Name)/*") -Destination $sampleDirectory
        foreach ($boundaryFile in @('Directory.Build.props', 'Directory.Packages.props', 'NuGet.Config')) {
            Copy-Item -LiteralPath (Join-Path $consumerDirectory $boundaryFile) -Destination $sampleDirectory
        }
        if ($ConsumerFramework -eq 'net10.0') {
            Copy-Item -LiteralPath (Join-Path $consumerDirectory 'global.json') -Destination $sampleDirectory
        }
        $sampleProject = Join-Path $sampleDirectory "$($sample.Name).csproj"
        Invoke-DotNet -Arguments @('restore', $sampleProject, '--configfile', 'NuGet.Config',
            '--packages', $cacheDirectory, '--no-http-cache', $versionArgument, $frameworkArgument)
        Invoke-DotNet -Arguments @('run', '--project', $sampleProject, '--no-restore', '-c', 'Release',
            $versionArgument, $frameworkArgument)
        if ($EnableCuda) {
            Invoke-DotNet -Arguments @('run', '--project', $sampleProject, '--no-build', '--no-restore', '-c', 'Release',
                $versionArgument, $frameworkArgument, '--', '--cuda')
        }
        Write-Output "Published $($sample.Name) APIs validated on $ConsumerFramework."
    }
    Write-Output "Published package version $packageVersion validated on $ConsumerFramework from nuget.org."
    Write-Output "Isolated consumer and cache: $consumerDirectory"
}
finally { Pop-Location }
