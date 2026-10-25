using System.Collections.ObjectModel;
using QuantumMatter.Core;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Application.Noise;

/// <summary>Sequential stochastic trajectories with bounded host arrays and explicit backend injection.</summary>
public sealed class NoisyStateVectorSimulator
{
    private readonly IStateVectorBackend _unitaryBackend;
    private readonly INoiseChannelBackend _noiseBackend;
    private readonly SimulationOptions _options;

    /// <summary>Injects caller-owned unitary/noise backends and a positive host-array budget.</summary>
    public NoisyStateVectorSimulator(IStateVectorBackend unitaryBackend, INoiseChannelBackend noiseBackend,
        SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(unitaryBackend);
        ArgumentNullException.ThrowIfNull(noiseBackend);
        _unitaryBackend = unitaryBackend;
        _noiseBackend = noiseBackend;
        _options = options ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MemoryBudgetBytes);
    }

    /// <summary>Returns one unmeasured conditional pure state; it does not represent the mixed ensemble.</summary>
    public StateVector SimulateTrajectory(NoisyCircuit circuit, int? seed = null, CancellationToken cancellationToken = default)
    {
        Validate(circuit, estimating: false, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        Evolve(circuit, state, CreateRandom(seed), cancellationToken);
        return state;
    }

    /// <summary>Resets/evolves a fresh trajectory per positive shot, then measures it once.</summary>
    public NoisySimulationResult Run(NoisyCircuit circuit, int shots = 1024, int? seed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shots, 1);
        Validate(circuit, estimating: false, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        Random random = CreateRandom(seed);
        var outcomes = new Dictionary<int, int>();
        for (int shot = 0; shot < shots; shot++)
        {
            state.Reset(cancellationToken);
            Evolve(circuit, state, random, cancellationToken);
            int outcome = state.MeasureAll(random, cancellationToken);
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((int outcome, int count) in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Add(Convert.ToString(outcome, 2).PadLeft(circuit.QubitCount, '0'), count);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new NoisySimulationResult(shots, new ReadOnlyDictionary<string, int>(counts));
    }

    /// <summary>Averages Born probabilities, not amplitudes. Needs at least two trajectories to estimate variance.</summary>
    public NoisyProbabilityResult EstimateProbabilities(NoisyCircuit circuit, int trajectories = 1024, int? seed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(trajectories, 2);
        Validate(circuit, estimating: true, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        var means = new double[state.Length];
        var variance = new double[state.Length];
        Random random = CreateRandom(seed);
        for (int trajectory = 0; trajectory < trajectories; trajectory++)
        {
            state.Reset(cancellationToken);
            Evolve(circuit, state, random, cancellationToken);
            double total = TotalMass(state, cancellationToken);
            for (int index = 0; index < state.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                double probability = state.Probability(index) / total;
                double delta = probability - means[index];
                means[index] += delta / (trajectory + 1);
                variance[index] += delta * (probability - means[index]);
            }
        }

        // Reuse the M2 buffer for standard errors; no extra result-sized allocation.
        double divisor = (double)trajectories * (trajectories - 1);
        for (int index = 0; index < variance.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            variance[index] = Math.Sqrt(Math.Max(0, variance[index]) / divisor);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new NoisyProbabilityResult(circuit.QubitCount, trajectories, means, variance);
    }

    private void Evolve(NoisyCircuit circuit, StateVector state, Random random, CancellationToken cancellationToken) =>
        state.EvolveTrajectory(circuit, _unitaryBackend, _noiseBackend, random, cancellationToken);

    private static Random CreateRandom(int? seed) => seed.HasValue ? new Random(seed.Value) : Random.Shared;

    private void Validate(NoisyCircuit circuit, bool estimating, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        cancellationToken.ThrowIfCancellationRequested();
        long required = (estimating ? 32L : 16L) << circuit.QubitCount;
        if (required > _options.MemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(circuit),
                $"Execution requires {required} bytes; the budget is {_options.MemoryBudgetBytes} bytes.");
        }
    }

    private static double TotalMass(StateVector state, CancellationToken cancellationToken)
    {
        double total = 0;
        double compensation = 0;
        for (int index = 0; index < state.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double corrected = state.Probability(index) - compensation;
            double next = total + corrected;
            compensation = (next - total) - corrected;
            total = next;
        }

        if (!double.IsFinite(total) || total <= 0)
        {
            throw new InvalidOperationException("The state has no finite positive probability mass.");
        }

        return total;
    }
}
