using System.Numerics;

namespace QuantumMatter.Core;

/// <summary>An immutable row-major 2x2 unitary matrix, validated at construction.</summary>
public sealed record SingleQubitUnitary
{
    /// <summary>Absolute column norm and orthogonality tolerance used at construction.</summary>
    public const double ValidationTolerance = 1e-12;

    private SingleQubitUnitary(Complex m00, Complex m01, Complex m10, Complex m11)
    {
        M00 = m00;
        M01 = m01;
        M10 = m10;
        M11 = m11;
    }

    /// <summary>Row zero, column zero.</summary>
    public Complex M00 { get; }
    /// <summary>Row zero, column one.</summary>
    public Complex M01 { get; }
    /// <summary>Row one, column zero.</summary>
    public Complex M10 { get; }
    /// <summary>Row one, column one.</summary>
    public Complex M11 { get; }

    /// <summary>Copies a finite 2x2 matrix and validates U-dagger U against identity.</summary>
    public static SingleQubitUnitary Create(Complex[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 2 || matrix.GetLength(1) != 2)
        {
            throw new ArgumentException("A single-qubit matrix must have exactly two rows and two columns.", nameof(matrix));
        }

        return Create(matrix[0, 0], matrix[0, 1], matrix[1, 0], matrix[1, 1]);
    }

    /// <summary>Validates four finite row-major entries and creates an immutable unitary.</summary>
    public static SingleQubitUnitary Create(Complex m00, Complex m01, Complex m10, Complex m11)
    {
        ValidateFinite(m00, nameof(m00));
        ValidateFinite(m01, nameof(m01));
        ValidateFinite(m10, nameof(m10));
        ValidateFinite(m11, nameof(m11));

        // Orthonormal columns are equivalent to U-dagger U = I for a square matrix.
        double firstNorm = SquaredMagnitude(m00) + SquaredMagnitude(m10);
        double secondNorm = SquaredMagnitude(m01) + SquaredMagnitude(m11);
        if (Math.Abs(firstNorm - 1) > ValidationTolerance || Math.Abs(secondNorm - 1) > ValidationTolerance)
        {
            throw new ArgumentException("Matrix columns must have unit norm.");
        }

        Complex innerProduct = Complex.Conjugate(m00) * m01 + Complex.Conjugate(m10) * m11;
        if (Complex.Abs(innerProduct) > ValidationTolerance)
        {
            throw new ArgumentException("Matrix columns must be orthogonal.");
        }

        return new SingleQubitUnitary(m00, m01, m10, m11);
    }

    /// <summary>Returns the conjugate transpose, preserving the validated unitary contract.</summary>
    public SingleQubitUnitary Adjoint() => new(
        Complex.Conjugate(M00), Complex.Conjugate(M10),
        Complex.Conjugate(M01), Complex.Conjugate(M11));

    private static void ValidateFinite(Complex value, string parameterName)
    {
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Matrix entries must be finite.");
        }
    }

    private static double SquaredMagnitude(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
}
