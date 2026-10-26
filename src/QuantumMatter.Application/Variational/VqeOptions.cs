namespace QuantumMatter.Application.Variational;

/// <summary>Immutable limits and backtracking settings for deterministic pure-state VQE.</summary>
public sealed record VqeOptions
{
    /// <summary>Maximum accepted parameter updates; zero evaluates only the initial energy.</summary>
    public int MaximumIterations { get; init; } = 200;

    /// <summary>Maximum complete circuit executions, including every parameter-shift circuit; at least one.</summary>
    public long MaximumCircuitEvaluations { get; init; } = 100_000;

    /// <summary>Maximum trial steps per iteration, including unrepresentable candidates.</summary>
    public int MaximumLineSearchIterations { get; init; } = 40;

    /// <summary>Nonnegative absolute infinity-norm gradient tolerance; stationarity does not imply a global minimum.</summary>
    public double GradientTolerance { get; init; } = 1e-6;

    /// <summary>Positive finite initial step along the negative gradient, restarted for each iteration.</summary>
    public double InitialStepSize { get; init; } = 1;

    /// <summary>Positive finite lower bound on attempted steps, no larger than InitialStepSize.</summary>
    public double MinimumStepSize { get; init; } = 1e-12;

    /// <summary>Finite step reduction factor strictly between zero and one.</summary>
    public double BacktrackingFactor { get; init; } = 0.5;

    /// <summary>Finite Armijo sufficient-decrease coefficient strictly between zero and one.</summary>
    public double SufficientDecrease { get; init; } = 0.1;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCircuitEvaluations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumLineSearchIterations);
        if (!double.IsFinite(GradientTolerance) || GradientTolerance < 0) { throw new ArgumentOutOfRangeException(nameof(GradientTolerance)); }
        if (!double.IsFinite(InitialStepSize) || InitialStepSize <= 0) { throw new ArgumentOutOfRangeException(nameof(InitialStepSize)); }
        if (!double.IsFinite(MinimumStepSize) || MinimumStepSize <= 0 || MinimumStepSize > InitialStepSize) { throw new ArgumentOutOfRangeException(nameof(MinimumStepSize)); }
        if (!double.IsFinite(BacktrackingFactor) || BacktrackingFactor <= 0 || BacktrackingFactor >= 1) { throw new ArgumentOutOfRangeException(nameof(BacktrackingFactor)); }
        if (!double.IsFinite(SufficientDecrease) || SufficientDecrease <= 0 || SufficientDecrease >= 1) { throw new ArgumentOutOfRangeException(nameof(SufficientDecrease)); }
    }
}
