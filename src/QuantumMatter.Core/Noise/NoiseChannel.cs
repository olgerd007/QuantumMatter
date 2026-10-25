namespace QuantumMatter.Core.Noise;

/// <summary>Stable identifiers for the supported independent single-qubit channels.</summary>
public enum NoiseKind
{
    /// <summary>Probabilistic X error.</summary>
    BitFlip = 0,
    /// <summary>Probabilistic Z error.</summary>
    PhaseFlip = 1,
    /// <summary>Mixture with a maximally mixed target.</summary>
    Depolarizing = 2,
    /// <summary>Zero-temperature decay from one to zero.</summary>
    AmplitudeDamping = 3
}

/// <summary>An immutable single-qubit channel with a finite parameter in [0,1].</summary>
public sealed record NoiseChannel
{
    private NoiseChannel(NoiseKind kind, double probability)
    {
        if (!double.IsFinite(probability) || probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Channel probability must be finite and in [0,1].");
        }

        Kind = kind;
        Probability = probability;
    }

    /// <summary>The channel model.</summary>
    public NoiseKind Kind { get; }
    /// <summary>The validated parameter in [0,1]; gamma for damping, replacement strength for depolarization.</summary>
    public double Probability { get; }

    /// <summary>Creates a probabilistic X channel with error probability in [0,1].</summary>
    public static NoiseChannel BitFlip(double probability) => new(NoiseKind.BitFlip, probability);
    /// <summary>Creates a probabilistic Z channel; probability 0.5 completely dephases a single qubit.</summary>
    public static NoiseChannel PhaseFlip(double probability) => new(NoiseKind.PhaseFlip, probability);

    /// <summary>E(rho) = (1-p) rho + p I/2 on the target; Pauli weights are p/4 each.</summary>
    public static NoiseChannel Depolarizing(double probability) => new(NoiseKind.Depolarizing, probability);

    /// <summary>Zero-temperature energy relaxation: population in |1> decays with probability gamma.</summary>
    public static NoiseChannel AmplitudeDamping(double gamma) => new(NoiseKind.AmplitudeDamping, gamma);
}
