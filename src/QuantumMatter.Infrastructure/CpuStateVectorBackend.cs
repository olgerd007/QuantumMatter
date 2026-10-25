using System.Numerics;
using QuantumMatter.Core;

namespace QuantumMatter.Infrastructure;

/// <summary>Portable scalar reference implementation; stateless and reusable across simulations.</summary>
public sealed class CpuStateVectorBackend : IStateVectorBackend
{
    /// <inheritdoc />
    public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (amplitudes.Length != 1 << circuit.QubitCount)
        {
            throw new ArgumentException("Buffer length must equal 2^qubitCount.", nameof(amplitudes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Span<Complex> state = amplitudes.Span;
        foreach (GateOperation operation in circuit.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int controlMask = operation.Control >= 0 ? 1 << operation.Control : 0;
            if (operation.SecondControl >= 0)
            {
                controlMask |= 1 << operation.SecondControl;
            }

            switch (operation.Kind)
            {
                case GateKind.I:
                    break;
                case GateKind.CNot:
                case GateKind.Toffoli:
                case GateKind.X:
                    ApplyX(state, operation.Target, controlMask, cancellationToken);
                    break;
                case GateKind.Swap:
                    ApplySwap(state, operation.Control, operation.Target, cancellationToken);
                    break;
                case GateKind.H:
                    double h = Math.Sqrt(0.5);
                    ApplyMatrix(state, operation.Target, h, h, h, -h, controlMask, cancellationToken);
                    break;
                case GateKind.Y:
                    ApplyMatrix(state, operation.Target, 0, -Complex.ImaginaryOne, Complex.ImaginaryOne, 0, controlMask, cancellationToken);
                    break;
                case GateKind.Z:
                    ApplyDiagonal(state, operation.Target, 1, -1, controlMask, cancellationToken);
                    break;
                case GateKind.S:
                    ApplyDiagonal(state, operation.Target, 1, Complex.ImaginaryOne, controlMask, cancellationToken);
                    break;
                case GateKind.Sdg:
                    ApplyDiagonal(state, operation.Target, 1, -Complex.ImaginaryOne, controlMask, cancellationToken);
                    break;
                case GateKind.T:
                case GateKind.Tdg:
                case GateKind.Phase:
                    double phase = operation.Kind switch
                    {
                        GateKind.T => Math.PI / 4,
                        GateKind.Tdg => -Math.PI / 4,
                        _ => operation.Angle
                    };
                    ApplyDiagonal(state, operation.Target, 1, Complex.FromPolarCoordinates(1, phase), controlMask, cancellationToken);
                    break;
                case GateKind.Rx:
                case GateKind.Ry:
                    double cosine = Math.Cos(operation.Angle / 2);
                    double sine = Math.Sin(operation.Angle / 2);
                    Complex upper = operation.Kind == GateKind.Rx ? -Complex.ImaginaryOne * sine : -sine;
                    Complex lower = operation.Kind == GateKind.Rx ? upper : sine;
                    ApplyMatrix(state, operation.Target, cosine, upper, lower, cosine, controlMask, cancellationToken);
                    break;
                case GateKind.Rz:
                    ApplyDiagonal(state, operation.Target,
                        Complex.FromPolarCoordinates(1, -operation.Angle / 2),
                        Complex.FromPolarCoordinates(1, operation.Angle / 2), controlMask, cancellationToken);
                    break;
                case GateKind.Unitary:
                    SingleQubitUnitary unitary = operation.Unitary!;
                    ApplyMatrix(state, operation.Target, unitary.M00, unitary.M01, unitary.M10, unitary.M11, controlMask, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported gate: {operation.Kind}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ApplyMatrix(Span<Complex> state, int target,
        Complex a, Complex b, Complex c, Complex d, int controlMask, CancellationToken cancellationToken)
    {
        int bit = 1 << target;
        // Enumerate each pair once, inserting a zero at the target bit.
        for (int pair = 0; pair < state.Length / 2; pair++)
        {
            if ((pair & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            if ((first & controlMask) != controlMask)
            {
                continue;
            }

            int second = first | bit;
            Complex x = state[first];
            Complex y = state[second];
            state[first] = a * x + b * y;
            state[second] = c * x + d * y;
        }
    }

    private static void ApplyX(Span<Complex> state, int target, int controlMask, CancellationToken cancellationToken)
    {
        int bit = 1 << target;
        for (int pair = 0; pair < state.Length / 2; pair++)
        {
            if ((pair & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int low = pair & (bit - 1);
            int first = ((pair - low) << 1) | low;
            if ((first & controlMask) != controlMask)
            {
                continue;
            }

            int second = first | bit;
            (state[first], state[second]) = (state[second], state[first]);
        }
    }

    private static void ApplyDiagonal(Span<Complex> state, int target,
        Complex zeroPhase, Complex onePhase, int controlMask, CancellationToken cancellationToken)
    {
        int bit = 1 << target;
        for (int index = 0; index < state.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if ((index & controlMask) == controlMask)
            {
                state[index] *= (index & bit) == 0 ? zeroPhase : onePhase;
            }
        }
    }

    private static void ApplySwap(Span<Complex> state, int control, int target, CancellationToken cancellationToken)
    {
        int controlBit = 1 << control;
        int targetBit = 1 << target;
        for (int index = 0; index < state.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if ((index & controlBit) != 0 && (index & targetBit) == 0)
            {
                int other = index ^ controlBit ^ targetBit;
                (state[index], state[other]) = (state[other], state[index]);
            }
        }
    }
}
