using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--cuda")) { return 2; }
var framework = new FrameworkName(Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
Require(Environment.Version.Major == framework.Version.Major, "Consumer must execute on its declared runtime.");
Validate(new CpuStateVectorBackend());
Validate(new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }));
if (args.Length == 1)
{
    using var cuda = new CudaStateVectorBackend();
    Validate(cuda);
    Console.WriteLine("Observation consumer validated CUDA.");
}

Console.WriteLine("Observation package consumer passed.");
return 0;

static void Validate(IStateVectorBackend backend)
{
    var simulator = new StateVectorSimulator(backend);
    var state = simulator.Simulate(new QuantumCircuit(3).H(0).CNot(0, 2).H(1).S(1));
    Require(Math.Abs(state.MarginalProbability([2], 1) - 0.5) < 1e-12, "Marginal Born probability.");
    Require(Math.Abs(state.ExpectationValue(new PauliString("YIY")) + 1) < 1e-12, "Bell YY sign.");
    Require(Math.Abs(state.ExpectationValue(new PauliString("IYI")) - 1) < 1e-12, "Spectator phase.");
    int outcome = state.Measure([2, 0], new Random(42));
    Require(outcome is 0 or 3, "Packed correlated subset outcome.");
    Require(Math.Abs(state.MarginalProbability([2, 0], outcome) - 1) < 1e-12, "Projected branch.");
    Require(Math.Abs(state.ExpectationValue(new PauliString("IYI")) - 1) < 1e-12, "Spectator coherence after projection.");
    state.Evolve(new QuantumCircuit(3).Sdg(1).H(1), backend);
    Require(Math.Abs(state.Probability(outcome == 0 ? 0 : 5) - 1) < 1e-12, "Continued backend evolution.");
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}
