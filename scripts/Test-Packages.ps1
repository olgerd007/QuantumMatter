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
$packageDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$consumerDirectory = Join-Path $repositoryRoot ('artifacts/package-smoke/' + [guid]::NewGuid().ToString('N'))
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

    New-Item -ItemType Directory -Path $consumerDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $repositoryRoot 'samples/PackageConsumer/*') -Destination $consumerDirectory
    # Stop traversal into repository build/package configuration: only published package references are allowed.
    [IO.File]::WriteAllText((Join-Path $consumerDirectory 'Directory.Build.props'), "<Project />`n")
    [IO.File]::WriteAllText((Join-Path $consumerDirectory 'Directory.Packages.props'),
        "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`n")
    if ($ConsumerFramework -eq 'net10.0') {
        [IO.File]::WriteAllText((Join-Path $consumerDirectory 'global.json'),
            '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }')
    }
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
        Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-restore', '-c', $Configuration, $versionArgument, $frameworkArgument)
        if ($EnableCuda) {
            Invoke-DotNet -Arguments @('run', '--project', $consumerProject, '--no-build', '--no-restore', '-c', $Configuration,
                $versionArgument, $frameworkArgument, '--', '--cuda')
        }
    }
    finally { Pop-Location }
    Write-Output "Validated package version $packageVersion from $packageDirectory"
    Write-Output "Isolated consumer and cache: $consumerDirectory"
}
finally { Pop-Location }
