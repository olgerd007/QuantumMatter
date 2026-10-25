using QuantumMatter.Application;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Application.Noise;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Demo;
using QuantumMatter.Infrastructure;

string example = args.Length == 0 ? "bell" : args[0].ToLowerInvariant();
string backend = args.Length < 2 ? "scalar" : args[1].ToLowerInvariant();
if (args.Length > 2 || example is not ("bell" or "deutsch-jozsa" or "qft" or "grover" or "noise" or "observations" or "dynamic" or "noisy-dynamic" or "variational" or "all")
    || backend is not ("scalar" or "parallel" or "parallel-simd" or "cuda"))
{
    Console.Error.WriteLine("Usage: QuantumMatter.Demo [bell|deutsch-jozsa|qft|grover|noise|observations|dynamic|noisy-dynamic|variational|all] [scalar|parallel|parallel-simd|cuda]");
    return 2;
}

IStateVectorBackend execution;
try
{
    execution = backend switch
    {
        "parallel" => new ParallelCpuStateVectorBackend(),
        "parallel-simd" => new ParallelCpuStateVectorBackend(new ParallelCpuOptions { EnableSimd = true }),
        "cuda" => new CudaStateVectorBackend(),
        _ => new CpuStateVectorBackend()
    };
}
catch (PlatformNotSupportedException error) when (backend == "cuda")
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

using IDisposable? ownedBackend = execution as IDisposable;
var simulator = new StateVectorSimulator(execution);
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
        NoiseExamples.Run(new NoisyStateVectorSimulator(execution, new CpuNoiseChannelBackend()));
        break;
    case "observations":
        ObservationExamples.Run(simulator);
        break;
    case "dynamic":
        DynamicExamples.Run(new DynamicStateVectorSimulator(execution));
        break;
    case "noisy-dynamic":
        NoisyDynamicExamples.Run(new NoisyDynamicStateVectorSimulator(execution, new CpuNoiseChannelBackend()));
        break;
    case "variational":
        VariationalExamples.Run(new VariationalEnergyEvaluator(execution));
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
