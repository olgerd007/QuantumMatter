using System.Collections.ObjectModel;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;

namespace QuantumMatter.Application.Dynamic;

/// <summary>Runs independent noisy dynamic trajectories with explicit unitary and noise backend injection.</summary>
public sealed class NoisyDynamicStateVectorSimulator
{
    private readonly IStateVectorBackend _unitaryBackend;
    private readonly INoiseChannelBackend _noiseBackend;
    private readonly SimulationOptions _options;

    /// <summary>Injects caller-owned backends and a positive host amplitude-array budget.</summary>
    public NoisyDynamicStateVectorSimulator(IStateVectorBackend unitaryBackend, INoiseChannelBackend noiseBackend,
        SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(unitaryBackend);
        ArgumentNullException.ThrowIfNull(noiseBackend);
        _unitaryBackend = unitaryBackend;
        _noiseBackend = noiseBackend;
        _options = options ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MemoryBudgetBytes);
    }

    /// <summary>Returns one conditional state and its explicit classical register without implicit readout.</summary>
    /// <remarks>The state describes one sampled history, not the mixed ensemble; seeds are runtime/backend specific.</remarks>
    public DynamicTrajectoryResult SimulateTrajectory(NoisyDynamicCircuit circuit, int? seed = null,
        CancellationToken cancellationToken = default)
    {
        Validate(circuit, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        int classical = state.EvolveNoisyDynamic(circuit, _unitaryBackend, _noiseBackend, CreateRandom(seed), cancellationToken);
        return new DynamicTrajectoryResult(state, circuit.ClassicalBitCount, classical);
    }

    /// <summary>Re-executes every instruction with fresh quantum/classical state for each positive shot.</summary>
    /// <remarks>Counts represent only final classical memory. No terminal measurement or ensemble FinalState is added.</remarks>
    public DynamicSimulationResult Run(NoisyDynamicCircuit circuit, int shots = 1024, int? seed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shots, 1);
        Validate(circuit, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        Random random = CreateRandom(seed);
        var outcomes = new Dictionary<int, int>();
        for (int shot = 0; shot < shots; shot++)
        {
            state.Reset(cancellationToken);
            int outcome = state.EvolveNoisyDynamic(circuit, _unitaryBackend, _noiseBackend, random, cancellationToken);
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((int outcome, int count) in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Add(Convert.ToString(outcome, 2).PadLeft(circuit.ClassicalBitCount, '0'), count);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DynamicSimulationResult(circuit.ClassicalBitCount, shots, new ReadOnlyDictionary<string, int>(counts));
    }

    private void Validate(NoisyDynamicCircuit circuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        cancellationToken.ThrowIfCancellationRequested();
        long required = StateVector.RequiredBytes(circuit.QubitCount);
        if (required > _options.MemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(circuit), $"Execution requires {required} bytes; the budget is {_options.MemoryBudgetBytes} bytes.");
        }
    }

    private static Random CreateRandom(int? seed) => seed.HasValue ? new Random(seed.Value) : Random.Shared;
}
