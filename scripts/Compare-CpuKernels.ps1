[CmdletBinding()]
param(
    [string]$BaselineRef = 'HEAD',
    [ValidateSet('quick', 'full', 'kernels', 'memory')][string]$Profile = 'full',
    [string]$Output = 'artifacts/cpu-comparison.csv'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files = @('CpuStateVectorBackend.cs', 'ParallelCpuOptions.cs', 'ParallelCpuStateVectorBackend.cs',
    'PreparedStateVectorGate.cs', 'SimdHadamardKernel.cs')
Push-Location $repositoryRoot
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $revision = & git rev-parse --verify $BaselineRef
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the baseline revision.' }
    $baselineDirectory = Join-Path $repositoryRoot "artifacts/cpu-baseline/$($revision.Trim())"
    New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
    foreach ($optional in @('AvxStateVectorKernel.cs', 'Avx512StateVectorKernel.cs')) {
        $optionalFile = & git ls-tree --name-only $revision "src/QuantumMatter.Infrastructure/$optional"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect baseline CPU sources.' }
        if ($optionalFile) { $files += $optional }
    }
    foreach ($file in $files) {
        $source = & git show "${revision}:src/QuantumMatter.Infrastructure/$file"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $file." }
        [IO.File]::WriteAllLines((Join-Path $baselineDirectory $file), [string[]]$source)
    }

    # The isolated tooling assembly has no package inventory or shipping configuration.
    [IO.File]::WriteAllText((Join-Path $baselineDirectory 'Directory.Build.props'), '<Project />')
    $coreProject = [Security.SecurityElement]::Escape((Join-Path $repositoryRoot 'src/QuantumMatter.Core/QuantumMatter.Core.csproj'))
    [IO.File]::WriteAllText((Join-Path $baselineDirectory 'QuantumMatter.CpuBaseline.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$coreProject" /></ItemGroup>
</Project>
"@)
    & dotnet build (Join-Path $baselineDirectory 'QuantumMatter.CpuBaseline.csproj') -c Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
    & dotnet build benchmarks/QuantumMatter.Benchmarks -c Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Candidate benchmark build failed.' }
    $baselineAssembly = Join-Path $baselineDirectory 'bin/Release/net9.0/QuantumMatter.CpuBaseline.dll'
    $env:DOTNET_TieredCompilation = '0'
    & dotnet benchmarks/QuantumMatter.Benchmarks/bin/Release/net9.0/QuantumMatter.Benchmarks.dll $Profile $Output $baselineAssembly
    if ($LASTEXITCODE -ne 0) { throw 'CPU comparison failed.' }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output) + '.baseline.txt', $revision.Trim())
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    Pop-Location
}
