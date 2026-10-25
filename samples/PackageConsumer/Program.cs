using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application;
using QuantumMatter.Application.Algorithms;
using QuantumMatter.Application.Noise;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;

bool enableCuda = false;
string? expectedVersion = null;
for (int index = 0; index < args.Length; index++)
{
    if (args[index] == "--cuda" && !enableCuda)
    {
        enableCuda = true;
    }
    else if (args[index] == "--expected-sdk-version" && expectedVersion is null && index + 1 < args.Length)
    {
        expectedVersion = args[++index];
    }
    else
    {
        Console.Error.WriteLine("Usage: PackageConsumer [--cuda] [--expected-sdk-version VERSION]");
        return 2;
    }
}

if (expectedVersion is not null)
{
    foreach (Assembly assembly in new[] { typeof(StateVector).Assembly, typeof(StateVectorSimulator).Assembly, typeof(CpuStateVectorBackend).Assembly })
    {
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Require(actual == expectedVersion, $"Expected {expectedVersion}, loaded {assembly.GetName().Name} {actual}.");
    }
}

var targetFramework = new FrameworkName(Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
Require(Environment.Version.Major == targetFramework.Version.Major, "Consumer must execute on its declared major runtime.");
Console.WriteLine($"Package consumer runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
ValidateBackend(new CpuStateVectorBackend());
ValidateBackend(new ParallelCpuStateVectorBackend(new ParallelCpuOptions
{
    MaxDegreeOfParallelism = 3,
    MinimumParallelQubits = 1,
    EnableSimd = true
}));
if (enableCuda)
{
    using var cuda = new CudaStateVectorBackend();
    ValidateBackend(cuda);
    Console.WriteLine($"Packaged CUDA backend validated on {cuda.Device.Name}.");
}

var noisy = new NoisyStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
var damping = new NoisyCircuit(1).Append(new QuantumCircuit(1).X(0))
    .ApplyNoise(NoiseChannel.AmplitudeDamping(0.3), 0);
ProbabilityEstimate estimate = noisy.EstimateProbabilities(damping, trajectories: 4000, seed: 42).Estimate(1);
Require(Math.Abs(estimate.Probability - 0.7) < 0.05, "Amplitude damping ensemble probability.");
Require(estimate.StandardError > 0 && estimate.StandardError < 0.02, "Trajectory standard error.");
Console.WriteLine("Package consumer passed: CPU, SIMD selection, algorithms, complex phases, budgets, cancellation and noise.");
return 0;

static void ValidateBackend(IStateVectorBackend backend)
{
    var simulator = new StateVectorSimulator(backend);
    var bell = new QuantumCircuit(2).H(0).CNot(0, 1);
    SimulationResult samples = simulator.Run(bell, shots: 100, seed: 42);
    Require(samples.Counts.Values.Sum() == 100 && samples.Counts.Count == 2, "Bell terminal counts.");
    Require(Complex.Abs(samples.FinalState.Amplitude(3) - Math.Sqrt(0.5)) < 1e-12, "Bell complex amplitude.");
    SingleQubitUnitary phase = SingleQubitUnitary.Create(Complex.ImaginaryOne, 0, 0, Complex.ImaginaryOne);
    Require(Complex.Abs(simulator.Simulate(new QuantumCircuit(1).U(0, phase)).Amplitude(0) - Complex.ImaginaryOne) < 1e-12,
        "Custom-unitary global phase.");
    var qft = new QuantumCircuit(3).X(0).X(2);
    QuantumFourierTransform.Append(qft);
    QuantumFourierTransform.Append(qft, new[] { 0, 1, 2 }, inverse: true);
    Require(Math.Abs(simulator.Simulate(qft).Probability(5) - 1) < 1e-12, "QFT inverse.");
    Require(simulator.Simulate(DeutschJozsa.CreateCircuit(new[] { false, true, true, false })).Probability(3) > 1 - 1e-12,
        "Deutsch-Jozsa parity.");
    Require(simulator.Simulate(GroverSearch.CreateCircuit(3, 5)).Probability(5) > 0.94, "Grover search.");
    var noisy = new NoisyStateVectorSimulator(backend, new CpuNoiseChannelBackend());
    var reset = new NoisyCircuit(1).Append(new QuantumCircuit(1).X(0)).ApplyNoise(NoiseChannel.AmplitudeDamping(1), 0);
    Require(noisy.Run(reset, 4, seed: 42).Counts["0"] == 4, "Hybrid noise trajectory.");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try { simulator.Simulate(bell, canceled.Token); throw new InvalidOperationException("Cancellation was ignored."); }
    catch (OperationCanceledException) { }
    try
    {
        new StateVectorSimulator(backend, new SimulationOptions { MemoryBudgetBytes = 32 }).Simulate(bell);
        throw new InvalidOperationException("Memory budget was ignored.");
    }
    catch (ArgumentOutOfRangeException) { }
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}
