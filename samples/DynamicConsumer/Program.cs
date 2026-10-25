using System.Numerics;
using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Infrastructure;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--cuda")) { return 2; }
var framework = new FrameworkName(Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
Require(Environment.Version.Major == framework.Version.Major, "Consumer must run on its declared major runtime.");
Validate(new CpuStateVectorBackend());
Validate(new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }));
if (args.Length == 1)
{
    using var cuda = new CudaStateVectorBackend();
    Validate(cuda);
    Console.WriteLine($"Dynamic consumer validated CUDA on {cuda.Device.Name}.");
}

Console.WriteLine($"Dynamic package consumer passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
return 0;

static void Validate(IStateVectorBackend backend)
{
    const double theta = 0.73;
    const double phi = -0.61;
    var simulator = new DynamicStateVectorSimulator(backend);
    var teleportation = new DynamicCircuit(3, 3)
        .Append(new QuantumCircuit(3).Ry(0, theta).Rz(0, phi).H(1).CNot(1, 2).CNot(0, 1).H(0))
        .Measure([0, 1], [0, 1]).If(2, 2, new QuantumCircuit(3).X(2)).If(1, 1, new QuantumCircuit(3).Z(2));
    var result = simulator.SimulateTrajectory(teleportation, seed: 42);
    Complex alpha = Math.Cos(theta / 2) * Complex.FromPolarCoordinates(1, -phi / 2);
    Complex beta = Math.Sin(theta / 2) * Complex.FromPolarCoordinates(1, phi / 2);
    Require(Complex.Abs(result.FinalState.Amplitude(result.ClassicalOutcome) - alpha) < 3e-12, "Teleported alpha and phase.");
    Require(Complex.Abs(result.FinalState.Amplitude(result.ClassicalOutcome | 4) - beta) < 3e-12, "Teleported beta and phase.");
    teleportation.Append(new QuantumCircuit(3).Rz(2, -phi).Ry(2, -theta)).Measure([2], [2]);
    var shots = simulator.Run(teleportation, shots: 32, seed: 42);
    Require(shots.Counts.Values.Sum() == 32 && shots.Counts.Keys.All(label => label[0] == '0'), "Feedback restores receiver on every shot.");
    var reset = new DynamicCircuit(1, 2).Append(new QuantumCircuit(1).H(0)).Measure([0], [0]).Reset(0).Measure([0], [1]);
    var counts = simulator.Run(reset, shots: 64, seed: 42).Counts;
    Require(counts.Count == 2 && counts.Keys.All(label => label[0] == '0'), "Reset with preserved earlier classical record.");
    var overwrite = new DynamicCircuit(1, 1).Append(new QuantumCircuit(1).X(0)).Measure([0], [0])
        .Append(new QuantumCircuit(1).X(0)).Measure([0], [0]);
    Require(simulator.Run(overwrite, 8, 42).Counts["0"] == 8, "Classical writes overwrite one with zero.");
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}
