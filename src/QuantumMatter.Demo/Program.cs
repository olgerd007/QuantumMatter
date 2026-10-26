using QuantumMatter.Application;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Application.Noise;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Demo;
using QuantumMatter.Demo.Cryptography;
using QuantumMatter.Infrastructure;

string example = args.Length == 0 ? "bell" : args[0].ToLowerInvariant();
string backend = args.Length < 2 ? "scalar" : args[1].ToLowerInvariant();
string presetName = args.Length < 3 ? "default" : args[2].ToLowerInvariant();
if (args.Length > 3 || example is not ("bell" or "deutsch-jozsa" or "qft" or "grover" or "noise" or "observations" or "dynamic" or "noisy-dynamic" or "variational" or "channel-assignment" or "limits" or "bb84" or "bb84-noise" or "chsh" or "grover-key" or "shor" or "cryptography" or "all")
    || backend is not ("scalar" or "parallel" or "parallel-simd" or "cuda")
    || presetName is not ("default" or "workstation"))
{
    Console.Error.WriteLine("Usage: QuantumMatter.Demo [bell|deutsch-jozsa|qft|grover|noise|observations|dynamic|noisy-dynamic|variational|channel-assignment|limits|bb84|bb84-noise|chsh|grover-key|shor|cryptography|all] [scalar|parallel|parallel-simd|cuda] [default|workstation]");
    return 2;
}

DemoExecutionPreset preset = DemoExecutionPreset.Create(presetName);
if (backend == "parallel-simd") { preset = preset with { ParallelCpu = preset.ParallelCpu with { EnableSimd = true } }; }
if (example == "limits")
{
    Console.WriteLine($"Execution preset: {presetName}");
    preset.PrintLimits();
    return 0;
}

if (presetName != "default") { Console.WriteLine($"Execution preset: {presetName}"); }
IStateVectorBackend execution;
try
{
    execution = backend switch
    {
        "parallel" => new ParallelCpuStateVectorBackend(preset.ParallelCpu),
        "parallel-simd" => new ParallelCpuStateVectorBackend(preset.ParallelCpu),
        "cuda" => new CudaStateVectorBackend(preset.Cuda),
        _ => new CpuStateVectorBackend()
    };
}
catch (PlatformNotSupportedException error) when (backend == "cuda")
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

using IDisposable? ownedBackend = execution as IDisposable;
var simulator = new StateVectorSimulator(execution, preset.Simulation);
switch (example)
{
    case "bell":
        RunBell(simulator);
        break;
    case "deutsch-jozsa":
        AlgorithmExamples.RunDeutschJozsa(simulator);
        break;
    case "qft":
        AlgorithmExamples.RunQft(simulator);
        break;
    case "grover":
        AlgorithmExamples.RunGrover(simulator);
        break;
    case "noise":
        NoiseExamples.Run(new NoisyStateVectorSimulator(execution, new CpuNoiseChannelBackend(), preset.Simulation));
        break;
    case "observations":
        ObservationExamples.Run(simulator);
        break;
    case "dynamic":
        DynamicExamples.Run(new DynamicStateVectorSimulator(execution, preset.Simulation));
        break;
    case "noisy-dynamic":
        NoisyDynamicExamples.Run(new NoisyDynamicStateVectorSimulator(execution, new CpuNoiseChannelBackend(), preset.Simulation));
        break;
    case "variational":
        VariationalExamples.Run(new VariationalEnergyEvaluator(execution, preset.Simulation));
        break;
    case "channel-assignment":
        ChannelAssignmentExample.Run(new VariationalEnergyEvaluator(execution, preset.Simulation), simulator);
        break;
    case "bb84":
    case "bb84-noise":
        CryptographyExamples.RunBb84(execution, new CpuNoiseChannelBackend(), preset.Simulation, example == "bb84-noise");
        break;
    case "chsh":
        CryptographyExamples.RunChsh(execution, new CpuNoiseChannelBackend(), preset.Simulation);
        break;
    case "grover-key":
        CryptographyExamples.RunToyGrover(simulator);
        break;
    case "shor":
        CryptographyExamples.RunShor(simulator);
        break;
    case "cryptography":
        CryptographyExamples.RunBb84(execution, new CpuNoiseChannelBackend(), preset.Simulation, false);
        CryptographyExamples.RunBb84(execution, new CpuNoiseChannelBackend(), preset.Simulation, true);
        CryptographyExamples.RunChsh(execution, new CpuNoiseChannelBackend(), preset.Simulation);
        CryptographyExamples.RunToyGrover(simulator);
        CryptographyExamples.RunShor(simulator);
        break;
    case "all":
        RunBell(simulator);
        Console.WriteLine();
        AlgorithmExamples.RunDeutschJozsa(simulator);
        Console.WriteLine();
        AlgorithmExamples.RunQft(simulator);
        Console.WriteLine();
        AlgorithmExamples.RunGrover(simulator);
        break;
}

return 0;

static void RunBell(StateVectorSimulator simulator)
{
    var circuit = new QuantumCircuit(2).H(0).CNot(control: 0, target: 1);
    SimulationResult result = simulator.Run(circuit, shots: 1000, seed: 42);

    Console.WriteLine("Bell state: (|00> + |11>) / sqrt(2)");
    for (int index = 0; index < result.FinalState.Length; index++)
    {
        string bits = Convert.ToString(index, 2).PadLeft(circuit.QubitCount, '0');
        Console.WriteLine($"|{bits}> probability = {result.FinalState.Probability(index):P2}");
    }

    Console.WriteLine("Sampled counts:");
    foreach (var (bits, count) in result.Counts)
    {
        Console.WriteLine($"|{bits}> = {count}");
    }
}
