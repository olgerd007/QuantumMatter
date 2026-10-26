[CmdletBinding()]
param([string[]]$ReportPaths = @('docs'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap {
    $message = 'Report privacy validation failed; diagnostic details are withheld.'
    if ($_.Exception -is [IO.InvalidDataException]) { $message = $_.Exception.Message }
    [Console]::Error.WriteLine($message)
    exit 1
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$privateFields = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($field in @('Device', 'Cpu', 'LogicalProcessors', 'ProcessorDescription', 'OperatingSystem', 'Os',
    'Architecture', 'Runtime', 'BuildSdk', 'Sdk', 'NativeWindowsProviderFiles', 'VectorDoubleLanes',
    'VectorAccelerated', 'SimdLayoutSupported', 'Avx', 'Avx2', 'Avx512', 'Avx512F',
    'Vector512Accelerated', 'ComplexLayoutSupported', 'Gpu', 'GpuName', 'CpuModel',
    'ProcessorName', 'DeviceName', 'DeviceId', 'DeviceUuid', 'Uuid', 'SerialNumber',
    'HostName', 'MachineName', 'ComputerName', 'TotalPhysicalMemory', 'TotalMemoryBytes',
    'PhysicalMemoryBytes', 'DeviceMemoryBytes', 'DriverVersion', 'CudaDriverVersion',
    'OsVersion', 'RuntimeVersion', 'SdkVersion')) { [void]$privateFields.Add($field) }

function Assert-JsonFields([System.Text.Json.JsonElement]$Element, [string]$Report) {
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        foreach ($property in $Element.EnumerateObject()) {
            if ($privateFields.Contains($property.Name)) {
                throw [IO.InvalidDataException]::new("Host-identifying metadata field '$($property.Name)' in $Report. Remove it before publication.")
            }
            Assert-JsonFields -Element $property.Value -Report $Report
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-JsonFields -Element $item -Report $Report }
    }
}

$reports = @(
    foreach ($path in $ReportPaths) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $path))
        if (![IO.Path]::GetRelativePath($repositoryRoot, $resolved).StartsWith('..')) {
            if (Test-Path -LiteralPath $resolved -PathType Leaf) { Get-Item -LiteralPath $resolved }
            else { Get-ChildItem -LiteralPath $resolved -File -Recurse }
        }
        else { throw [IO.InvalidDataException]::new('Privacy-check paths must be inside the repository.') }
    }
    foreach ($name in @('README.md', 'PACKAGE-README.md', 'CHANGELOG.md')) { Get-Item -LiteralPath (Join-Path $repositoryRoot $name) }
)
$identityPattern = '(?i)\b(?:Ryzen|Threadripper|GeForce|Quadro|Radeon|AuthenticAMD|GenuineIntel|AMD64\s+Family|Intel\(R\)\s+(?:Core|Xeon))\b|\b(?:RTX|GTX)\s*\d|\bWindows\s+build\s+\d|\bdriver\s+\d+\.\d+|\bSM_\d+|\bcompute\s+capability\s+\d|[A-Z]:\\(?:Users|GH)\\'
foreach ($file in $reports | Sort-Object FullName -Unique) {
    if ($file.Extension -notin @('.md', '.json', '.txt', '.csv')) { continue }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text -match $identityPattern) { throw [IO.InvalidDataException]::new("Possible host identity in $relative. Matched values are withheld from logs.") }
    if ($file.Extension -eq '.json') {
        $document = [System.Text.Json.JsonDocument]::Parse($text)
        try { Assert-JsonFields -Element $document.RootElement -Report $relative }
        finally { $document.Dispose() }
    }
}

# Runtime discovery APIs remain available to callers; report generators must not capture host identity.
$capturePattern = 'PROCESSOR_IDENTIFIER|RuntimeInformation\.(?:OSDescription|FrameworkDescription|ProcessArchitecture)|Environment\.ProcessorCount|\.Device\.Name|gpu\.Device|Registry\.LocalMachine|BuildSdkVersion'
foreach ($directory in @('benchmarks', 'experiments', 'samples')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $directory) -Filter '*.cs' -File -Recurse) {
        if ($file.FullName -match '[\\/](?:bin|obj)[\\/]') { continue }
        if ([IO.File]::ReadAllText($file.FullName) -match $capturePattern) {
            $relative = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
            throw [IO.InvalidDataException]::new("Host-identity capture in report/sample source $relative.")
        }
    }
}
Write-Output 'Report privacy checks passed; host identities and installation fingerprints are omitted.'
exit 0
