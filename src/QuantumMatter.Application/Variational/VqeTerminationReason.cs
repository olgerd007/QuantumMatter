namespace QuantumMatter.Application.Variational;

/// <summary>Why a successful VQE call stopped; failures and cancellation throw instead.</summary>
public enum VqeTerminationReason
{
    /// <summary>The current gradient infinity norm is within tolerance; no global-minimum guarantee.</summary>
    GradientToleranceReached = 0,
    /// <summary>The maximum number of accepted updates was reached.</summary>
    IterationLimitReached = 1,
    /// <summary>The remaining execution budget cannot fund the next complete evaluation.</summary>
    EvaluationLimitReached = 2,
    /// <summary>No representable sufficient-decrease step was found within the configured search limits.</summary>
    LineSearchStalled = 3
}
