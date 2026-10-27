using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

if (args.Length == 2 && args[0] == "cuda")
{
    return CudaBenchmarks.Run(args[1]);
}

if (args.Length == 2 && args[0] == "accelerators")
{
    return AcceleratorBenchmarks.Run(args[1]);
}

if (args.Length is < 2 or > 3 || args[0] is not ("quick" or "full" or "kernels" or "memory"))
{
    Console.Error.WriteLine("Usage: QuantumMatter.Benchmarks <quick|full|kernels|memory> <output.csv> [baseline.dll] | <cuda|accelerators> <output.csv>");
    return 2;
}

int[] sizes = args[0] switch
{
    "quick" => new[] { 12, 16, 18 },
    "memory" => new[] { 20, 25 },
    _ => new[] { 12, 16, 18, 20 }
};
int[] depths = args[0] switch
{
    "quick" => new[] { 32 },
    "memory" => new[] { 8 },
    _ => new[] { 16, 64 }
};
int repetitions = args[0] == "quick" ? 3 : 5;
string[] mixes = args[0] == "kernels"
    ? new[] { "rx-low", "rx-high", "ry-low", "ry-high", "phase-low", "phase-high", "unitary-low", "unitary-high" }
    : new[] { "hadamard-low", "hadamard-high", "mixed-low", "mixed-high" };
var configurations = new List<BackendConfiguration>
{
    new("scalar", new CpuStateVectorBackend(), 1, false)
};
int[] workerCounts = args[0] == "memory" ? new[] { 1, 4, 8 } : new[] { 1, 2, 4, 8 };
bool[] simdModes = args[0] == "memory" ? new[] { true } : new[] { false, true };
foreach (int workers in workerCounts)
{
    foreach (bool simd in simdModes)
    {
        configurations.Add(new BackendConfiguration($"parallel-{workers}-simd-{simd.ToString().ToLowerInvariant()}",
            new ParallelCpuStateVectorBackend(new ParallelCpuOptions
            {
                MaxDegreeOfParallelism = workers,
                MinimumParallelQubits = 1,
                EnableSimd = simd
            }), workers, simd));
    }
}

if (args[0] == "memory")
{
    foreach (int workers in workerCounts)
    {
        configurations.Add(new BackendConfiguration($"blocked-{workers}-simd-true",
            new ParallelCpuStateVectorBackend(new ParallelCpuOptions
            {
                MaxDegreeOfParallelism = workers,
                MinimumParallelQubits = 1,
                EnableSimd = true,
                EnableGateBlocking = true
            }), workers, true, true));
    }
}

if (args.Length == 3)
{
    // The source baseline has a distinct assembly name and shares only the existing Core port.
    Assembly baseline = Assembly.LoadFrom(Path.GetFullPath(args[2]));
    Type optionsType = baseline.GetType("QuantumMatter.Infrastructure.ParallelCpuOptions", throwOnError: true)!;
    Type backendType = baseline.GetType("QuantumMatter.Infrastructure.ParallelCpuStateVectorBackend", throwOnError: true)!;
    foreach (int workers in workerCounts)
    {
        foreach (bool simd in simdModes)
        {
            object options = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("MaxDegreeOfParallelism")!.SetValue(options, workers);
            optionsType.GetProperty("MinimumParallelQubits")!.SetValue(options, 1);
            optionsType.GetProperty("EnableSimd")!.SetValue(options, simd);
            var backend = (IStateVectorBackend)Activator.CreateInstance(backendType, options)!;
            int candidate = configurations.FindIndex(entry => entry.Workers == workers && entry.Simd == simd
                && entry.Name.StartsWith("parallel-", StringComparison.Ordinal));
            configurations.Insert(candidate + 1,
                new BackendConfiguration($"before-{workers}-simd-{simd.ToString().ToLowerInvariant()}", backend, workers, simd));
        }
    }
}

string? configuredBudget = Environment.GetEnvironmentVariable("QUANTUMMATTER_BENCHMARK_MEMORY_BUDGET_BYTES");
long arrayBudget = configuredBudget is null ? 2L << 30
    : long.Parse(configuredBudget, System.Globalization.CultureInfo.InvariantCulture);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayBudget);
// The CPU harness owns three full arrays: initial, execution buffer and scalar oracle.
// Preflight every case before any state allocation or partial output is created.
foreach (int qubits in sizes)
{
    long required = 3 * StateVector.RequiredBytes(qubits);
    if (required > arrayBudget)
    {
        throw new ArgumentOutOfRangeException(nameof(args),
            $"The {qubits}-qubit benchmark requires {required} array bytes; the benchmark budget is {arrayBudget} bytes.");
    }
}

string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var metadata = new
{
    timestampUtc = TimeProvider.System.GetUtcNow(),
    profile = args[0],
    privacy = "Anonymous benchmark; host hardware and installation details are not collected.",
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
    precision = "double Complex",
    sizes,
    depths,
    mixes,
    repetitions,
    workerCounts,
    simdModes,
    benchmarkArrayBudgetBytes = arrayBudget,
    maximumLiveStateArrayBytes = 3 * StateVector.RequiredBytes(sizes.Max()),
    gateBlockingComparison = args[0] == "memory",
    warmupsPerBackendAndCase = 2,
    forcedParallelThresholdQubits = 1,
    timing = "Execute only; input reset, allocation, warmup and validation excluded",
    allocations = "Process-wide managed allocation delta, including worker threads; median per Execute",
    workingSet = "Cumulative process high-water mark, not an isolated per-backend peak",
    ordering = "Rotate backend order on every repetition and reverse on odd repetitions",
    source = "Local working tree; commit/release not implied",
    baselineAssembly = args.Length == 3 ? Path.GetFileName(args[2]) : null
};
File.WriteAllText(output + ".metadata.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(metadata));
using var csv = new StreamWriter(output);
csv.WriteLine("backend,qubits,gates,mix,max_workers,simd_requested,median_ms,min_ms,max_ms,median_allocated_bytes,state_bytes,process_peak_working_set_bytes,max_amplitude_error,max_norm_error,gates_per_second,gate_blocking");
using Process process = Process.GetCurrentProcess();
foreach (int qubits in sizes)
{
    foreach (int depth in depths)
    {
        foreach (string mix in mixes)
        {
            QuantumCircuit circuit = Circuit(qubits, depth, mix);
            Complex[] initial = Initial(1 << qubits);
            Complex[] buffer = new Complex[initial.Length];
            Complex[] expected = (Complex[])initial.Clone();
            new CpuStateVectorBackend().Execute(circuit, expected);
            var samples = configurations.Select(_ => new List<Sample>()).ToArray();
            var errors = new double[configurations.Count];
            var normErrors = new double[configurations.Count];

            for (int config = 0; config < configurations.Count; config++)
            {
                for (int warmup = 0; warmup < 2; warmup++)
                {
                    initial.CopyTo(buffer, 0);
                    configurations[config].Backend.Execute(circuit, buffer);
                }

                double norm = 0;
                for (int index = 0; index < buffer.Length; index++)
                {
                    errors[config] = Math.Max(errors[config], Complex.Abs(buffer[index] - expected[index]));
                    norm += buffer[index].Real * buffer[index].Real + buffer[index].Imaginary * buffer[index].Imaginary;
                }

                normErrors[config] = Math.Abs(norm - 1);
                if (errors[config] > 1e-10 || normErrors[config] > 1e-9)
                {
                    throw new InvalidOperationException($"Conformance failed for {configurations[config].Name}, {qubits}, {mix}.");
                }
            }

            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                for (int offset = 0; offset < configurations.Count; offset++)
                {
                    int config = (offset + repetition) % configurations.Count;
                    if ((repetition & 1) != 0) { config = configurations.Count - 1 - config; }
                    initial.CopyTo(buffer, 0);
                    long beforeAllocation = GC.GetTotalAllocatedBytes(precise: true);
                    long start = Stopwatch.GetTimestamp();
                    configurations[config].Backend.Execute(circuit, buffer);
                    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    long allocation = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocation;
                    samples[config].Add(new Sample(elapsed, allocation));
                }
            }

            for (int config = 0; config < configurations.Count; config++)
            {
                BackendConfiguration entry = configurations[config];
                double[] milliseconds = samples[config].Select(sample => sample.Milliseconds).Order().ToArray();
                long[] allocations = samples[config].Select(sample => sample.AllocatedBytes).Order().ToArray();
                double median = milliseconds[milliseconds.Length / 2];
                process.Refresh();
                csv.WriteLine(FormattableString.Invariant(
                    $"{entry.Name},{qubits},{depth},{mix},{entry.Workers},{entry.Simd},{median:F6},{milliseconds[0]:F6},{milliseconds[^1]:F6},{allocations[allocations.Length / 2]},{StateVector.RequiredBytes(qubits)},{process.PeakWorkingSet64},{errors[config]:G17},{normErrors[config]:G17},{depth * 1000.0 / median:F3},{entry.GateBlocking}"));
                Console.WriteLine(FormattableString.Invariant($"n={qubits}, gates={depth}, {mix}, {entry.Name}: {median:F3} ms"));
            }

            csv.Flush();
            // Bound setup arrays across cases; collection stays outside all timed regions.
            GC.Collect();
        }
    }
}

Console.WriteLine("Results written.");
return 0;

static QuantumCircuit Circuit(int qubits, int depth, string mix)
{
    var circuit = new QuantumCircuit(qubits);
    int target = mix.EndsWith("high", StringComparison.Ordinal) ? qubits - 1 : 0;
    int control = (target + qubits / 2) % qubits;
    var custom = SingleQubitUnitary.Create(0, Complex.ImaginaryOne, 1, 0);
    for (int gate = 0; gate < depth; gate++)
    {
        if (mix.StartsWith("hadamard", StringComparison.Ordinal))
        {
            circuit.H(target);
            continue;
        }

        if (mix.StartsWith("rx-", StringComparison.Ordinal)) { circuit.Rx(target, 0.31); continue; }
        if (mix.StartsWith("ry-", StringComparison.Ordinal)) { circuit.Ry(target, -0.74); continue; }
        if (mix.StartsWith("phase-", StringComparison.Ordinal)) { circuit.Phase(target, 0.71); continue; }
        if (mix.StartsWith("unitary-", StringComparison.Ordinal)) { circuit.U(target, custom); continue; }

        switch (gate % 8)
        {
            case 0: circuit.H(target); break;
            case 1: circuit.Rx(target, 0.31); break;
            case 2: circuit.Ry(target, -0.74); break;
            case 3: circuit.Rz(target, 1.23); break;
            case 4: circuit.CNot(control, target); break;
            case 5: circuit.Swap(target, (target + 1) % qubits); break;
            case 6: circuit.CS(control, target); break;
            case 7: circuit.U(target, custom); break;
        }
    }

    return circuit;
}

static Complex[] Initial(int length)
{
    var state = new Complex[length];
    double norm = 0;
    for (int index = 0; index < length; index++)
    {
        state[index] = new Complex(Math.Sin(index * 0.013), Math.Cos(index * 0.007));
        norm += state[index].Real * state[index].Real + state[index].Imaginary * state[index].Imaginary;
    }

    double scale = 1 / Math.Sqrt(norm);
    for (int index = 0; index < length; index++)
    {
        state[index] *= scale;
    }

    return state;
}

internal sealed record BackendConfiguration(string Name, IStateVectorBackend Backend, int Workers, bool Simd, bool GateBlocking = false);
internal sealed record Sample(double Milliseconds, long AllocatedBytes);
