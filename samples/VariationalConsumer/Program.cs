using System.Reflection;
using System.Runtime.Versioning;
using QuantumMatter.Application;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;
using QuantumMatter.Infrastructure;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--cuda")) { return 2; }
var framework = new FrameworkName(Assembly.GetExecutingAssembly().GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
Require(Environment.Version.Major == framework.Version.Major, "Consumer must run on its declared runtime.");
Validate(new CpuStateVectorBackend());
Validate(new ParallelCpuStateVectorBackend(new ParallelCpuOptions { MinimumParallelQubits = 1, EnableSimd = true }));
if (args.Length == 1)
{
    using var cuda = new CudaStateVectorBackend();
    Validate(cuda);
    Console.WriteLine("Variational consumer validated CUDA.");
}

Console.WriteLine("Variational package consumer passed.");
return 0;

static void Validate(IStateVectorBackend backend)
{
    var evaluator = new VariationalEnergyEvaluator(backend);
    var single = new ParameterizedCircuit(1, 1).Ry(0, 0);
    var h = new PauliHamiltonian(1,
        [new PauliTerm(-0.2, new PauliString("I")), new PauliTerm(0.7, new PauliString("Z")), new PauliTerm(0.3, new PauliString("X"))]);
    const double theta = 0.73;
    var result = evaluator.EvaluateWithGradient(single, h, [theta]);
    Near(-0.2 + 0.7 * Math.Cos(theta) + 0.3 * Math.Sin(theta), result.Energy, "Hamiltonian energy.");
    Near(-0.7 * Math.Sin(theta) + 0.3 * Math.Cos(theta), result.Gradient[0], "Hamiltonian gradient.");
    var z = new PauliHamiltonian(1, [new PauliTerm(1, new PauliString("Z"))]);
    var shared = new ParameterizedCircuit(1, 2).Ry(0, 0).Ry(0, 0);
    result = evaluator.EvaluateWithGradient(shared, z, [theta, 9]);
    Near(Math.Cos(2 * theta), result.Energy, "Shared parameter energy.");
    Near(-2 * Math.Sin(2 * theta), result.Gradient[0], "Occurrence-wise shared parameter gradient.");
    Require(result.Gradient[1] == 0, "Unused parameter gradient.");
    result = evaluator.EvaluateWithGradient(single, z, [1e100]);
    Near(-Math.Sin(1e100), result.Gradient[0], "A large-angle shift must remain effective.");
    var offset = new PauliHamiltonian(1, [new PauliTerm(1e16, new PauliString("I")), new PauliTerm(1, new PauliString("Z"))]);
    Near(-Math.Sin(theta), evaluator.EvaluateWithGradient(single, offset, [theta]).Gradient[0], "Identity offsets must not erase gradients.");
    Near(1, new StateVector(1).ExpectationEnergy(z), "Direct state energy.");
    Require(single.BindWithRotationShift([theta], 0, Math.PI / 2).Operations.Count == 2, "Independent shift binding.");
    var budgeted = new VariationalEnergyEvaluator(backend, new SimulationOptions { MemoryBudgetBytes = 64 });
    Near(-Math.Sin(theta), budgeted.EvaluateWithGradient(single, z, [theta]).Gradient[0], "Exact state/parameter/term array budget.");
}

static void Near(double expected, double actual, string message) => Require(Math.Abs(expected - actual) < 5e-12, message);
static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}
