using QuantumMatter.Application;
using QuantumMatter.Application.Variational;
using QuantumMatter.Core;
using QuantumMatter.Core.Observables;
using QuantumMatter.Core.Variational;

namespace QuantumMatter.Demo;

internal static class ChannelAssignmentExample
{
    internal const int TransmitterCount = 6;
    private static readonly (int First, int Second, int Weight)[] Links =
    [
        (0, 1, 8), (0, 2, 6), (1, 2, 3), (1, 3, 5), (2, 4, 7),
        (3, 4, 4), (3, 5, 6), (4, 5, 5), (0, 5, 2)
    ];

    internal static void Run(VariationalEnergyEvaluator evaluator, StateVectorSimulator simulator)
    {
        Console.WriteLine("Two-channel assignment for six transmitters A-F (synthetic interference weights).");
        foreach (var (first, second, weight) in Links)
        {
            Console.WriteLine($"  {(char)('A' + first)}-{(char)('A' + second)}: {weight}");
        }

        var optimized = Optimize(evaluator);
        SimulationResult sample = simulator.Run(optimized.Circuit, shots: 2048, seed: 42);
        int assignment = sample.Counts.Keys.Select(bits => Convert.ToInt32(bits, 2))
            .OrderByDescending(Score).ThenBy(value => value).First();
        int separated = Score(assignment);
        int total = Links.Sum(link => link.Weight);
        // Exhaustive enumeration validates the tiny example only; it never seeds the optimizer.
        int exact = Enumerable.Range(0, 1 << TransmitterCount).Max(Score);
        double optimalMass = Enumerable.Range(0, 1 << TransmitterCount)
            .Where(value => Score(value) == exact).Sum(sample.FinalState.Probability);

        Console.WriteLine(FormattableString.Invariant($"Uniform random baseline: expected separated weight {total / 2.0:F2}."));
        Console.WriteLine(FormattableString.Invariant($"Optimized circuit: expected separated weight {-optimized.Energy:F6}."));
        for (int channel = 0; channel < 2; channel++)
        {
            string names = string.Join(", ", Enumerable.Range(0, TransmitterCount)
                .Where(qubit => ((assignment >> qubit) & 1) == channel).Select(qubit => (char)('A' + qubit)));
            Console.WriteLine($"Channel {channel}: {names}");
        }

        Console.WriteLine($"Best sampled assignment: separated weight {separated}/{total}; residual same-channel weight {total - separated}.");
        Console.WriteLine($"Exact optimum (all 64 assignments): {exact}; sampled objective gap: {exact - separated}.");
        Console.WriteLine(FormattableString.Invariant($"Final-state probability of an optimal assignment: {optimalMass:P2}."));
        Console.WriteLine("2048 shots; twelve seeded optimization starts. This heuristic has no general convergence or speedup guarantee.");
    }

    internal static PauliHamiltonian CreateHamiltonian()
    {
        // A cut edge has (1 - Zi*Zj)/2 = 1; minimizing negative cut weight maximizes separation.
        var terms = new List<PauliTerm>
        {
            new(-Links.Sum(link => link.Weight) / 2.0, new PauliString(new string('I', TransmitterCount)))
        };
        foreach (var (first, second, weight) in Links)
        {
            char[] operators = new string('I', TransmitterCount).ToCharArray();
            operators[TransmitterCount - 1 - first] = 'Z';
            operators[TransmitterCount - 1 - second] = 'Z';
            terms.Add(new PauliTerm(weight / 2.0, new PauliString(new string(operators))));
        }

        return new PauliHamiltonian(TransmitterCount, terms);
    }

    internal static int Score(int assignment)
    {
        if ((uint)assignment >= 1 << TransmitterCount) { throw new ArgumentOutOfRangeException(nameof(assignment)); }
        int score = 0;
        foreach (var (first, second, weight) in Links)
        {
            if (((assignment >> first) & 1) != ((assignment >> second) & 1)) { score += weight; }
        }

        return score;
    }

    internal static (QuantumCircuit Circuit, double Energy) Optimize(VariationalEnergyEvaluator evaluator,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = new ParameterizedCircuit(TransmitterCount, 2 * TransmitterCount);
        for (int qubit = 0; qubit < TransmitterCount; qubit++) { template.Ry(qubit, qubit); }
        var entanglers = new QuantumCircuit(TransmitterCount);
        for (int qubit = 0; qubit < TransmitterCount - 1; qubit++) { entanglers.CNot(qubit, qubit + 1); }
        template.Append(entanglers);
        for (int qubit = 0; qubit < TransmitterCount; qubit++) { template.Ry(qubit, TransmitterCount + qubit); }

        PauliHamiltonian hamiltonian = CreateHamiltonian();
        var random = new Random(42);
        double[] bestParameters = new double[template.ParameterCount];
        double bestEnergy = double.PositiveInfinity;
        for (int start = 0; start < 12; start++)
        {
            double[] parameters = Enumerable.Range(0, template.ParameterCount)
                .Select(_ => 2 * Math.PI * random.NextDouble()).ToArray();
            for (int iteration = 0; iteration < 120; iteration++)
            {
                var result = evaluator.EvaluateWithGradient(template, hamiltonian, parameters, cancellationToken);
                for (int parameter = 0; parameter < parameters.Length; parameter++)
                {
                    parameters[parameter] -= 0.08 * result.Gradient[parameter];
                }
            }

            double energy = evaluator.Evaluate(template, hamiltonian, parameters, cancellationToken);
            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                parameters.CopyTo(bestParameters, 0);
            }
        }

        return (template.Bind(bestParameters, cancellationToken), bestEnergy);
    }
}
