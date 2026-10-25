using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;

namespace QuantumMatter.Demo;

internal static class VariationalExamples
{
    public static void Run(VariationalEnergyEvaluator evaluator)
    {
        var hamiltonian = new PauliHamiltonian(1,
            [new PauliTerm(-0.2, new PauliString("I")), new PauliTerm(0.7, new PauliString("Z")), new PauliTerm(0.3, new PauliString("X"))]);
        var circuit = new ParameterizedCircuit(1, 1).Ry(0, 0);
        double[] parameters = [0.73];
        var initial = evaluator.EvaluateWithGradient(circuit, hamiltonian, parameters);
        Console.WriteLine($"Initial energy: {initial.Energy:F8}; gradient: {initial.Gradient[0]:F8}.");
        // A caller-owned optimization loop illustrates composition; the SDK supplies no optimizer policy.
        for (int iteration = 0; iteration < 48; iteration++)
        {
            var current = evaluator.EvaluateWithGradient(circuit, hamiltonian, parameters);
            parameters[0] -= 0.4 * current.Gradient[0];
        }

        double final = evaluator.Evaluate(circuit, hamiltonian, parameters);
        double exactGroundEnergy = -0.2 - Math.Sqrt(0.7 * 0.7 + 0.3 * 0.3);
        Console.WriteLine($"Final energy: {final:F8}; analytic ground energy: {exactGroundEnergy:F8}.");
        if (Math.Abs(final - exactGroundEnergy) > 1e-5) { throw new InvalidOperationException("The small analytic example did not converge."); }
    }
}
