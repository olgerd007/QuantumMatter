using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--cuda")) { return 2; }
var framework = new FrameworkName(Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
Require(Environment.Version.Major == framework.Version.Major, "Consumer must use its declared major runtime.");
Validate(new CpuStateVectorBackend());
Validate(new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }));
if (args.Length == 1)
{
    using var cuda = new CudaStateVectorBackend();
    Validate(cuda);
    Console.WriteLine("Noisy dynamic consumer validated CUDA.");
}

Console.WriteLine("Noisy dynamic package consumer passed.");
return 0;

static void Validate(IStateVectorBackend backend)
{
    var noise = new CpuNoiseChannelBackend();
    var simulator = new NoisyDynamicStateVectorSimulator(backend, noise);
    var phases = new NoisyDynamicCircuit(2, 2).Append(new QuantumCircuit(2).X(0).H(1).S(1))
        .ApplyNoise(NoiseChannel.AmplitudeDamping(1), 0).Measure([0], [1])
        .If(2, 0, new QuantumCircuit(2).X(0)).ApplyNoise(NoiseChannel.PhaseFlip(1), 1)
        .Reset(0).Measure([0], [0]);
    var trajectory = simulator.SimulateTrajectory(phases, 92);
    Require(trajectory.ClassicalOutcome == 0, "Damping, feedback and reset readout.");
    Require(Complex.Abs(trajectory.FinalState.Amplitude(0) - Math.Sqrt(0.5)) < 4e-12, "Spectator zero amplitude.");
    Require(Complex.Abs(trajectory.FinalState.Amplitude(2) + Complex.ImaginaryOne * Math.Sqrt(0.5)) < 4e-12, "Spectator relative phase.");
    var history = new NoisyDynamicCircuit(1, 2).Append(new QuantumCircuit(1).X(0)).Measure([0], [0])
        .ApplyNoise(NoiseChannel.BitFlip(1), 0).If(1, 1, new QuantumCircuit(1).X(0))
        .Reset(0).Measure([0], [1]);
    Require(simulator.Run(history, 16, 9).Counts["01"] == 16, "Classical history survives noise and reset.");
    history.Measure([0], [0]);
    Require(simulator.Run(history, 16, 9).Counts["00"] == 16, "Classical overwrite clears an earlier one.");
    var sampled = new NoisyDynamicCircuit(1, 1).ApplyNoise(NoiseChannel.BitFlip(0.37), 0).Measure([0], [0]);
    var first = simulator.Run(sampled, 256, 17);
    Require(first.Counts.Count == 2 && first.Counts.Values.Sum() == 256, "Fresh noise branch for each shot.");
    Require(first.Counts.SequenceEqual(simulator.Run(sampled, 256, 17).Counts), "Seeded reproducibility.");
    var state = new StateVector(1);
    Require(state.EvolveNoisyDynamic(new NoisyDynamicCircuit(1, 1).ApplyNoise(NoiseChannel.BitFlip(1), 0),
        backend, noise, new Random(42)) == 0 && state.Probability(1) == 1, "Existing-state evolution has no implicit readout.");
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}
