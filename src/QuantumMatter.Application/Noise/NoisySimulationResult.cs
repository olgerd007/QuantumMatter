namespace QuantumMatter.Application.Noise;

/// <summary>One terminal measurement per independent trajectory. There is no single final ensemble ket.</summary>
public sealed class NoisySimulationResult
{
    internal NoisySimulationResult(int shots, IReadOnlyDictionary<string, int> counts)
    {
        Shots = shots;
        Counts = counts;
    }

    /// <summary>The number of independent trajectories measured once each.</summary>
    public int Shots { get; }
    /// <summary>Immutable terminal counts keyed by zero-padded basis labels.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; }
}

/// <summary>A computational-basis probability estimate and its trajectory sampling uncertainty.</summary>
/// <param name="Probability">Mean normalized Born probability, not a mean amplitude.</param>
/// <param name="StandardError">Estimated noise-trajectory standard error, excluding terminal measurement error.</param>
public readonly record struct ProbabilityEstimate(double Probability, double StandardError);

/// <summary>Computational-basis ensemble estimates; standard errors reflect trajectory sampling only.</summary>
public sealed class NoisyProbabilityResult
{
    private readonly double[] _probabilities;
    private readonly double[] _standardErrors;

    internal NoisyProbabilityResult(int qubits, int trajectories, double[] probabilities, double[] standardErrors)
    {
        QubitCount = qubits;
        Trajectories = trajectories;
        _probabilities = probabilities;
        _standardErrors = standardErrors;
    }

    /// <summary>The register size.</summary>
    public int QubitCount { get; }
    /// <summary>The number of independently evolved trajectories, at least two.</summary>
    public int Trajectories { get; }
    /// <summary>The number of computational-basis estimates.</summary>
    public int Length => _probabilities.Length;
    /// <summary>Returns an immutable estimate at an in-range basis index.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The basis index is outside [0,Length).</exception>
    public ProbabilityEstimate Estimate(int basisIndex)
    {
        if ((uint)basisIndex >= (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(basisIndex));
        }

        return new(_probabilities[basisIndex], _standardErrors[basisIndex]);
    }
}
