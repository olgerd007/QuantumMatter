using System.Collections.ObjectModel;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;

namespace QuantumMatter.Application.Dynamic;

/// <summary>Executes finite dynamic circuits with a fresh quantum state and zero classical register per trajectory.</summary>
public sealed class DynamicStateVectorSimulator
{
    private readonly IStateVectorBackend _backend;
    private readonly SimulationOptions _options;

    /// <summary>Injects a caller-owned backend and a positive host amplitude-array budget.</summary>
    public DynamicStateVectorSimulator(IStateVectorBackend backend, SimulationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _options = options ?? new SimulationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MemoryBudgetBytes);
    }

    /// <summary>Returns one conditional trajectory without any implicit terminal measurement.</summary>
    /// <remarks>Seeds are reproducible within the same runtime/backend, not guaranteed across versions.</remarks>
    public DynamicTrajectoryResult SimulateTrajectory(DynamicCircuit circuit, int? seed = null,
        CancellationToken cancellationToken = default)
    {
        Validate(circuit, cancellationToken);
        var state = new StateVector(circuit.QubitCount, _options.MemoryBudgetBytes);
        int classical = state.EvolveDynamic(circuit, _backend, CreateRandom(seed), cancellationToken);
        return new DynamicTrajectoryResult(state, circuit.ClassicalBitCount, classical);
    }

    /// <summary>Re-executes every instruction for each positive shot and counts the final classical register.</summary>
    /// <remarks>No ensemble FinalState or implicit quantum measurement is returned. Unwritten classical bits remain zero.</remarks>
    public DynamicSimulationResult Run(DynamicCircuit circuit, int shots = 1024, int? seed = null,
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
            int outcome = state.EvolveDynamic(circuit, _backend, random, cancellationToken);
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

    private void Validate(DynamicCircuit circuit, CancellationToken cancellationToken)
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
