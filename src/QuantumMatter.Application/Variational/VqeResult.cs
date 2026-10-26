namespace QuantumMatter.Application.Variational;

/// <summary>Immutable last accepted VQE iterate and complete evaluation accounting.</summary>
public sealed class VqeResult
{
    internal VqeResult(double[] parameters, double energy, int iterations, long energies, int gradients,
        long circuits, VqeTerminationReason terminationReason)
    {
        Parameters = Array.AsReadOnly(parameters);
        Energy = energy;
        IterationCount = iterations;
        EnergyEvaluationCount = energies;
        GradientEvaluationCount = gradients;
        CircuitEvaluationCount = circuits;
        TerminationReason = terminationReason;
    }

    /// <summary>Independently owned last accepted parameters; rejected trials are excluded.</summary>
    public IReadOnlyList<double> Parameters { get; }
    /// <summary>Normalized energy at Parameters, the lowest of the accepted iterates for a deterministic backend.</summary>
    public double Energy { get; }
    /// <summary>Number of accepted parameter updates.</summary>
    public int IterationCount { get; }
    /// <summary>Standalone energy calls, including initial energy and evaluated line-search trials.</summary>
    public long EnergyEvaluationCount { get; }
    /// <summary>Complete energy-and-gradient calls; each executes one base and two circuits per rotation occurrence.</summary>
    public int GradientEvaluationCount { get; }
    /// <summary>Total complete circuit executions across standalone energy and gradient calls.</summary>
    public long CircuitEvaluationCount { get; }
    /// <summary>Successful stopping classification, distinct from cancellation or technical failure.</summary>
    public VqeTerminationReason TerminationReason { get; }
}
