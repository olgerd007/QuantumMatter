using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using Microsoft.Win32;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

if (args.Length == 2 && args[0] == "cuda")
{
    return CudaBenchmarks.Run(args[1]);
}

if (args.Length != 2 || args[0] is not ("quick" or "full"))
{
    Console.Error.WriteLine("Usage: QuantumMatter.Benchmarks <quick|full|cuda> <output.csv>");
    return 2;
}

int[] sizes = args[0] == "quick" ? new[] { 12, 16, 18 } : new[] { 12, 16, 18, 20 };
int[] depths = args[0] == "quick" ? new[] { 32 } : new[] { 16, 64 };
int repetitions = args[0] == "quick" ? 3 : 5;
string[] mixes = { "hadamard-low", "hadamard-high", "mixed-low", "mixed-high" };
var configurations = new List<BackendConfiguration>
{
    new("scalar", new CpuStateVectorBackend(), 1, false)
};
foreach (int workers in new[] { 1, 2, 4, 8 })
{
    foreach (bool simd in new[] { false, true })
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

string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var metadata = new
{
    timestampUtc = TimeProvider.System.GetUtcNow(),
    profile = args[0],
    cpu = CpuName(),
    logicalProcessors = Environment.ProcessorCount,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    runtime = RuntimeInformation.FrameworkDescription,
    buildSdk = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(attribute => attribute.Key == "BuildSdkVersion").Value,
    vectorDoubleLanes = Vector<double>.Count,
    vectorAccelerated = Vector.IsHardwareAccelerated,
    simdLayoutSupported = ParallelCpuStateVectorBackend.IsSimdSupported,
    avx2 = Avx2.IsSupported,
    avx512 = Avx512F.IsSupported,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
    precision = "double Complex",
    sizes,
    depths,
    mixes,
    repetitions,
    warmupsPerBackendAndCase = 2,
    forcedParallelThresholdQubits = 1,
    timing = "Execute only; input reset, allocation, warmup and validation excluded",
    allocations = "Process-wide managed allocation delta, including worker threads; median per Execute",
    workingSet = "Cumulative process high-water mark, not an isolated per-backend peak",
    ordering = "Rotate backend order on every repetition",
    source = "Local working tree; commit/release not implied"
};
File.WriteAllText(output + ".metadata.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(metadata));
using var csv = new StreamWriter(output);
csv.WriteLine("backend,qubits,gates,mix,max_workers,simd_requested,median_ms,min_ms,max_ms,median_allocated_bytes,state_bytes,process_peak_working_set_bytes,max_amplitude_error,max_norm_error,gates_per_second");
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
                    $"{entry.Name},{qubits},{depth},{mix},{entry.Workers},{entry.Simd},{median:F6},{milliseconds[0]:F6},{milliseconds[^1]:F6},{allocations[allocations.Length / 2]},{StateVector.RequiredBytes(qubits)},{process.PeakWorkingSet64},{errors[config]:G17},{normErrors[config]:G17},{depth * 1000.0 / median:F3}"));
                Console.WriteLine(FormattableString.Invariant($"n={qubits}, gates={depth}, {mix}, {entry.Name}: {median:F3} ms"));
            }

            csv.Flush();
            // Bound setup arrays across cases; collection stays outside all timed regions.
            GC.Collect();
        }
    }
}

Console.WriteLine($"Results: {output}");
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

static string CpuName()
{
    if (OperatingSystem.IsWindows())
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "unknown";
    }

    if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
    {
        string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(value => value.StartsWith("model name", StringComparison.Ordinal));
        return line?.Split(':', 2)[1].Trim() ?? "unknown";
    }

    return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
}

internal sealed record BackendConfiguration(string Name, IStateVectorBackend Backend, int Workers, bool Simd);
internal sealed record Sample(double Milliseconds, long AllocatedBytes);
