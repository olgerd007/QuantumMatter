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
    Console.WriteLine($"VQE consumer validated CUDA on {cuda.Device.Name}.");
}
Console.WriteLine($"VQE package consumer passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
return 0;

static void Validate(IStateVectorBackend backend)
{
    var single = new ParameterizedCircuit(1, 1).Ry(0, 0);
    var h = new PauliHamiltonian(1, [new(-0.2, new PauliString("I")), new(0.7, new PauliString("Z")), new(0.3, new PauliString("X"))]);
    var solver = new VqeSolver(backend);
    double[] initial = [0.73];
    var result = solver.Solve(single, h, initial);
    Near(-0.2 - Math.Sqrt(0.58), result.Energy);
    Require(result.TerminationReason == VqeTerminationReason.GradientToleranceReached, "One-qubit stationarity.");
    Require(initial[0] == 0.73 && result.Parameters.Count == 1, "Owned parameters.");
    Require(result.CircuitEvaluationCount == result.EnergyEvaluationCount + 3 * result.GradientEvaluationCount, "Execution accounting.");
    var z = new PauliHamiltonian(1, [new(1, new PauliString("Z"))]);
    var limited = new VqeSolver(backend, new VqeOptions { MaximumCircuitEvaluations = 5 }).Solve(single, z, [0.7]);
    Require(limited.TerminationReason == VqeTerminationReason.EvaluationLimitReached && limited.CircuitEvaluationCount == 5, "Execution limit.");
    Require(solver.Solve(single, z, [1e100]).TerminationReason == VqeTerminationReason.LineSearchStalled, "Representability stall.");
    var budgeted = new VqeSolver(backend, new VqeOptions { MaximumIterations = 1 }, new SimulationOptions { MemoryBudgetBytes = 88 });
    Require(budgeted.Solve(single, z, [0.7]).IterationCount == 1, "Exact host-array budget.");
    var two = new ParameterizedCircuit(2, 1).Append(new QuantumCircuit(2).X(0)).Ry(1, 0).Append(new QuantumCircuit(2).CNot(1, 0));
    var heisenberg = new PauliHamiltonian(2, [new(1, new PauliString("XX")), new(1, new PauliString("YY")), new(1, new PauliString("ZZ"))]);
    result = solver.Solve(two, heisenberg, [0.2]);
    Near(-3, result.Energy);
    Near(-1 + 2 * Math.Sin(result.Parameters[0]), result.Energy);
    Require(result.TerminationReason == VqeTerminationReason.GradientToleranceReached, "Two-qubit stationarity.");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try { solver.Solve(single, z, [0.7], canceled.Token); throw new InvalidOperationException("Cancellation must throw."); }
    catch (OperationCanceledException) { }
}

static void Near(double expected, double actual) => Require(Math.Abs(expected - actual) < 2e-10, "Analytic VQE energy.");
static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
