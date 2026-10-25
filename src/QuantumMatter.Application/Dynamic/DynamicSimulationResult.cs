using QuantumMatter.Core;

namespace QuantumMatter.Application.Dynamic;

/// <summary>A single conditional pure trajectory and the classical bits generated during it.</summary>
public sealed class DynamicTrajectoryResult
{
    internal DynamicTrajectoryResult(StateVector state, int classicalBits, int classicalOutcome)
    {
        FinalState = state;
        ClassicalBitCount = classicalBits;
        ClassicalOutcome = classicalOutcome;
    }

    /// <summary>The owned mutable conditional state; it does not represent a mixed ensemble.</summary>
    public StateVector FinalState { get; }
    /// <summary>The classical register width.</summary>
    public int ClassicalBitCount { get; }
    /// <summary>The final packed classical register, with bit zero least significant.</summary>
    public int ClassicalOutcome { get; }
}

/// <summary>Immutable counts of final classical registers from independent dynamic trajectories.</summary>
public sealed class DynamicSimulationResult
{
    internal DynamicSimulationResult(int classicalBits, int shots, IReadOnlyDictionary<string, int> counts)
    {
        ClassicalBitCount = classicalBits;
        Shots = shots;
        Counts = counts;
    }

    /// <summary>The classical register width used to zero-pad count labels.</summary>
    public int ClassicalBitCount { get; }
    /// <summary>The positive number of independent trajectories.</summary>
    public int Shots { get; }
    /// <summary>Immutable final classical-register counts; the highest classical bit is printed first.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; }
}
