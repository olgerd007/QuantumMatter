using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;

namespace QuantumMatter.Demo;

internal static class VqeExamples
{
    public static void Run(VqeSolver solver)
    {
        var oneQubit = new PauliHamiltonian(1,
            [new(-0.2, new PauliString("I")), new(0.7, new PauliString("Z")), new(0.3, new PauliString("X"))]);
        Show("One-qubit Pauli sum", solver.Solve(new ParameterizedCircuit(1, 1).Ry(0, 0), oneQubit, [0.73]), -0.2 - Math.Sqrt(0.58));
        var heisenberg = new PauliHamiltonian(2,
            [new(1, new PauliString("XX")), new(1, new PauliString("YY")), new(1, new PauliString("ZZ"))]);
        var entangled = new ParameterizedCircuit(2, 1).Append(new QuantumCircuit(2).X(0))
            .Ry(1, 0).Append(new QuantumCircuit(2).CNot(1, 0));
        Show("Two-qubit Heisenberg singlet", solver.Solve(entangled, heisenberg, [0.2]), -3);
    }

    private static void Show(string label, VqeResult result, double exact)
    {
        Console.WriteLine($"{label}: E={result.Energy:F10}; analytic ground energy={exact:F10}.");
        Console.WriteLine($"Stop={result.TerminationReason}; accepted updates={result.IterationCount}; circuit executions={result.CircuitEvaluationCount}.");
        if (Math.Abs(result.Energy - exact) > 1e-9 || result.TerminationReason != VqeTerminationReason.GradientToleranceReached)
        { throw new InvalidOperationException("The analytic VQE example did not reach its expected stationary ground energy."); }
    }
}
