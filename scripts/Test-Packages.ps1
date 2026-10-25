[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('net9.0', 'net10.0')][string]$ConsumerFramework = 'net9.0',
    [switch]$SkipBuild,
    [switch]$EnableCuda
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionFile = [xml][IO.File]::ReadAllText((Join-Path $repositoryRoot 'Directory.Build.props'))
$packageVersion = $versionFile.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$libraryProperties = [xml][IO.File]::ReadAllText((Join-Path $repositoryRoot 'build/QuantumMatter.Library.props'))
$baselineVersion = $libraryProperties.SelectSingleNode('/Project/PropertyGroup/PackageValidationBaselineVersion').InnerText
$packageDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$consumerDirectory = Join-Path $repositoryRoot ('artifacts/package-smoke/' + [guid]::NewGuid().ToString('N'))
$observationDirectory = $consumerDirectory + '-observations'
$cacheDirectory = Join-Path $consumerDirectory 'packages'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

Push-Location $repositoryRoot
try {
    if (!$SkipBuild) {
        Invoke-DotNet -Arguments @('restore', 'QuantumMatter.sln')
        Invoke-DotNet -Arguments @('build', 'QuantumMatter.sln', '--no-restore', '-c', $Configuration, '--verbosity', 'minimal')
    }
    Invoke-DotNet -Arguments @('pack', 'QuantumMatter.sln', '--no-build', '--no-restore', '-c', $Configuration,
        '--output', $packageDirectory, '--verbosity', 'minimal')

    foreach ($packageId in @('QuantumMatter.Core', 'QuantumMatter.Application', 'QuantumMatter.Infrastructure')) {
        $packagePath = Join-Path $packageDirectory "$packageId.$packageVersion.nupkg"
        $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            foreach ($requiredEntry in @("lib/net9.0/$packageId.dll", "lib/net9.0/$packageId.xml", 'PACKAGE-README.md',
                'LICENSE', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md', 'docs/api/1.0.0.md', 'docs/releases/1.0.0.md',
                'docs/releases/1.1.0.md', 'docs/releases/1.2.0.md', 'docs/observations.md',
                'api/PublicAPI.Shipped.txt', 'api/PublicAPI.Unshipped.txt')) {
                if (!$archive.GetEntry($requiredEntry)) { throw "$packageId is missing $requiredEntry." }
            }
            $reader = [IO.StreamReader]::new($archive.GetEntry("$packageId.nuspec").Open())
            try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
            $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
            if ($metadata.version -ne $packageVersion -or $metadata.license.InnerText -ne 'MIT') {
                throw "$packageId version/license metadata is incorrect."
            }
            $dependencies = @($metadata.SelectNodes('.//*[local-name()="dependency"]'))
            $expectedDependencies = switch ($packageId) {
                'QuantumMatter.Core' { @() }
                'QuantumMatter.Application' { @('QuantumMatter.Core') }
                'QuantumMatter.Infrastructure' { @('ILGPU', 'QuantumMatter.Core') }
            }
            $actualDependencies = @($dependencies | ForEach-Object { $_.id } | Sort-Object)
            if (($actualDependencies -join ',') -ne ($expectedDependencies -join ',')) {
                throw "$packageId has unexpected runtime dependencies: $($actualDependencies -join ',')."
            }
            foreach ($dependency in $dependencies) {
                $expectedVersion = if ($dependency.id -eq 'ILGPU') { '1.5.3' } else { $packageVersion }
                if (!$dependency.version.Contains($expectedVersion)) { throw "Incorrect dependency version for $($dependency.id)." }
            }
        }
        finally { $archive.Dispose() }
        if (!(Test-Path -LiteralPath (Join-Path $packageDirectory "$packageId.$packageVersion.snupkg"))) {
            throw "$packageId symbol package is missing."
        }
    }

    foreach ($directory in @($consumerDirectory, $observationDirectory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        # Stop traversal into repository build/package configuration: only package references are allowed.
        [IO.File]::WriteAllText((Join-Path $directory 'Directory.Build.props'), "<Project />`n")
        [IO.File]::WriteAllText((Join-Path $directory 'Directory.Packages.props'),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`n")
        if ($ConsumerFramework -eq 'net10.0') {
            [IO.File]::WriteAllText((Join-Path $directory 'global.json'),
                '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }')
        }
    }
    Copy-Item -Path (Join-Path $repositoryRoot 'samples/PackageConsumer/*') -Destination $consumerDirectory
    $escapedFeed = [Security.SecurityElement]::Escape($packageDirectory)
    $configurationPath = Join-Path $consumerDirectory 'NuGet.Config'
    [IO.File]::WriteAllText($configurationPath, @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="QuantumMatter.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@)
    $consumerProject = Join-Path $consumerDirectory 'PackageConsumer.csproj'
    $versionArgument = "-p:QuantumMatterPackageVersion=$packageVersion"
    $frameworkArgument = "-p:TargetFramework=$ConsumerFramework"
    Push-Location $consumerDirectory
    try {
        Invoke-DotNet -Arguments @('restore', $consumerProject, '--configfile', $configurationPath,
            '--packages', $cacheDirectory, $versionArgument, $frameworkArgument)
        foreach ($packageId in @('QuantumMatter.Core', 'QuantumMatter.Application', 'QuantumMatter.Infrastructure')) {
            $artifactHash = (Get-FileHash -LiteralPath (Join-Path $packageDirectory "$packageId.$packageVersion.nupkg") -Algorithm SHA512).Hash
            $cachePackage = Join-Path $cacheDirectory "$($packageId.ToLowerInvariant())/$packageVersion/$($packageId.ToLowerInvariant()).$packageVersion.nupkg"
            if ((Get-FileHash -LiteralPath $cachePackage -Algorithm SHA512).Hash -ne $artifactHash) {
                throw "Consumer did not restore the freshly produced $packageId package."
            }
        }
        Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-restore', '-c', $Configuration,
            $versionArgument, $frameworkArgument, '--', '--expected-sdk-version', $packageVersion)
        if ($EnableCuda) {
            Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-build', '--no-restore', '-c', $Configuration,
                $versionArgument, $frameworkArgument, '--', '--cuda', '--expected-sdk-version', $packageVersion)
        }

        # New APIs have a separate package-only consumer; the baseline consumer stays limited to 1.0 APIs.
        # Use a sibling directory so SDK default compile globs cannot include one consumer in the other.
        Copy-Item -Path (Join-Path $repositoryRoot 'samples/ObservationConsumer/*') -Destination $observationDirectory
        $observationProject = Join-Path $observationDirectory 'ObservationConsumer.csproj'
        Invoke-DotNet -Arguments @('restore', $observationProject, '--configfile', $configurationPath,
            '--packages', $cacheDirectory, $versionArgument, $frameworkArgument)
        Invoke-DotNet -Arguments @('run', '--project', $observationProject, '--no-restore', '-c', $Configuration,
            $versionArgument, $frameworkArgument)
        if ($EnableCuda) {
            Invoke-DotNet -Arguments @('run', '--project', $observationProject, '--no-build', '--no-restore', '-c', $Configuration,
                $versionArgument, $frameworkArgument, '--', '--cuda')
        }

        # Recompile only the consumer against the published baseline, then replace just the SDK DLLs.
        # The unchanged consumer binary and baseline deps.json must execute with the candidate assemblies.
        $baselineCache = Join-Path $consumerDirectory 'baseline-packages'
        $baselineConfiguration = Join-Path $consumerDirectory 'NuGet.Baseline.Config'
        [IO.File]::WriteAllText($baselineConfiguration,
            '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
        $baselineArgument = "-p:QuantumMatterPackageVersion=$baselineVersion"
        Invoke-DotNet -Arguments @('restore', $consumerProject, '--configfile', $baselineConfiguration,
            '--packages', $baselineCache, '--force', '--no-http-cache', $baselineArgument, $frameworkArgument)
        foreach ($packageId in @('quantummatter.core', 'quantummatter.application', 'quantummatter.infrastructure')) {
            $baselineMetadata = [IO.File]::ReadAllText((Join-Path $baselineCache "$packageId/$baselineVersion/.nupkg.metadata")) | ConvertFrom-Json
            if ($baselineMetadata.source -ne 'https://api.nuget.org/v3/index.json') { throw 'Baseline must come from nuget.org.' }
        }
        Invoke-DotNet -Arguments @('build', $consumerProject, '--no-restore', '-c', $Configuration,
            $baselineArgument, $frameworkArgument, '--verbosity', 'minimal')
        $consumerOutput = Join-Path $consumerDirectory "bin/$Configuration/$ConsumerFramework"
        $consumerBinary = Join-Path $consumerOutput 'PackageConsumer.dll'
        $consumerDependencies = Join-Path $consumerOutput 'PackageConsumer.deps.json'
        $binaryHash = (Get-FileHash -LiteralPath $consumerBinary -Algorithm SHA256).Hash
        $dependencyHash = (Get-FileHash -LiteralPath $consumerDependencies -Algorithm SHA256).Hash
        $baselineDependencies = [IO.File]::ReadAllText($consumerDependencies) | ConvertFrom-Json
        if (!$baselineDependencies.libraries.PSObject.Properties["QuantumMatter.Application/$baselineVersion"]) {
            throw 'Consumer dependencies do not reference the published baseline.'
        }
        foreach ($packageId in @('QuantumMatter.Core', 'QuantumMatter.Application', 'QuantumMatter.Infrastructure')) {
            $candidateAssembly = Join-Path $cacheDirectory "$($packageId.ToLowerInvariant())/$packageVersion/lib/net9.0/$packageId.dll"
            Copy-Item -LiteralPath $candidateAssembly -Destination (Join-Path $consumerOutput "$packageId.dll") -Force
        }
        Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-build', '--no-restore', '-c', $Configuration,
            $baselineArgument, $frameworkArgument, '--', '--expected-sdk-version', $packageVersion)
        if ($EnableCuda) {
            Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-build', '--no-restore', '-c', $Configuration,
                $baselineArgument, $frameworkArgument, '--', '--cuda', '--expected-sdk-version', $packageVersion)
        }
        if ((Get-FileHash -LiteralPath $consumerBinary -Algorithm SHA256).Hash -ne $binaryHash -or
            (Get-FileHash -LiteralPath $consumerDependencies -Algorithm SHA256).Hash -ne $dependencyHash) {
            throw 'The baseline consumer was rebuilt or its dependency manifest was changed.'
        }
        Write-Output "Binary compatibility passed: consumer compiled against $baselineVersion, executed with $packageVersion on $ConsumerFramework."
    }
    finally { Pop-Location }
    Write-Output "Validated package version $packageVersion from $packageDirectory"
    Write-Output "Isolated consumer and cache: $consumerDirectory"
    Write-Output "Isolated new-API consumer: $observationDirectory"
}
finally { Pop-Location }
