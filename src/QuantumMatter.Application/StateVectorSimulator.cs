using System.Collections.ObjectModel;
using QuantumMatter.Core;

namespace QuantumMatter.Application;

/// <summary>A unitary run's uncollapsed mutable state and immutable terminal measurement counts.</summary>
/// <param name="FinalState">The owned pure state; later mutation does not recompute Counts.</param>
/// <param name="Counts">Zero-padded basis labels mapped to shot totals.</param>
public sealed record SimulationResult(StateVector FinalState, IReadOnlyDictionary<string, int> Counts);

/// <summary>Reusable synchronous unitary simulation and independent terminal sampling.</summary>
public sealed class StateVectorSimulator
{
    private readonly IStateVectorBackend _backend;
    private readonly SimulationOptions _options;

    /// <summary>Injects a backend owned by the caller and a positive host-array budget.</summary>
    public StateVectorSimulator(IStateVectorBackend backend, SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _options = options ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MemoryBudgetBytes);
    }

    /// <summary>Creates |0...0> and evolves it, rejecting the state budget before allocation.</summary>
    public StateVector Simulate(QuantumCircuit circuit, CancellationToken cancellationToken = default)
    {
        Validate(circuit, sampling: false, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        state.Evolve(circuit, _backend, cancellationToken);
        return state;
    }

    /// <summary>Evolves once and samples positive terminal shots without collapse; budgets state plus CDF.</summary>
    /// <remarks>Seeds are reproducible within the same runtime/backend, not guaranteed across versions.</remarks>
    public SimulationResult Run(QuantumCircuit circuit, int shots = 1024, int? seed = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shots, 1);
        Validate(circuit, sampling: true, cancellationToken);
        var state = Simulate(circuit, cancellationToken);
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var outcomes = new Dictionary<int, int>();
        var cumulative = new double[state.Length];
        double total = 0;
        int lastSupported = -1;
        for (int index = 0; index < state.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double probability = state.Probability(index);
            total += probability;
            cumulative[index] = total;
            if (probability > 0)
            {
                lastSupported = index;
            }
        }

        if (!double.IsFinite(total) || total <= 0)
        {
            throw new InvalidOperationException("The state has no finite positive probability mass.");
        }

        for (int shot = 0; shot < shots; shot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double draw = random.NextDouble() * total;
            // Strict upper bound skips zero-mass plateaus, including exact CDF boundaries.
            int low = 0;
            int high = cumulative.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (cumulative[middle] <= draw)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            int outcome = low < cumulative.Length ? low : lastSupported;
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (outcome, count) in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bits = Convert.ToString(outcome, 2).PadLeft(circuit.QubitCount, '0');
            counts.Add(bits, count);
        }

        return new SimulationResult(state, new ReadOnlyDictionary<string, int>(counts));
    }

    private void Validate(QuantumCircuit circuit, bool sampling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        cancellationToken.ThrowIfCancellationRequested();
        long required = StateVector.RequiredBytes(circuit.QubitCount);
        if (sampling)
        {
            required += 8L << circuit.QubitCount;
        }

        if (required > _options.MemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(circuit), $"Execution requires {required} bytes; the budget is {_options.MemoryBudgetBytes} bytes.");
        }
    }
}
