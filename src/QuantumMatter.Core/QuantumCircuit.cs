namespace QuantumMatter.Core;

/// <summary>Stable operation identifiers. Matrices use column vectors and finite radian angles.</summary>
public enum GateKind
{
    /// <summary>Hadamard basis change.</summary>
    H = 0,
    /// <summary>Pauli X, exchanging zero and one.</summary>
    X = 1,
    /// <summary>Pauli Y, including imaginary phases.</summary>
    Y = 2,
    /// <summary>Pauli Z, negating the one branch.</summary>
    Z = 3,
    /// <summary>Phase diag(1,i).</summary>
    S = 4,
    /// <summary>Phase diag(1,exp(i*pi/4)).</summary>
    T = 5,
    /// <summary>Rotation exp(-i*angle*X/2).</summary>
    Rx = 6,
    /// <summary>Rotation exp(-i*angle*Y/2).</summary>
    Ry = 7,
    /// <summary>Rotation exp(-i*angle*Z/2).</summary>
    Rz = 8,
    /// <summary>Controlled X.</summary>
    CNot = 9,
    /// <summary>Exchange two qubit positions.</summary>
    Swap = 10,
    /// <summary>Identity.</summary>
    I = 11,
    /// <summary>Adjoint of S.</summary>
    Sdg = 12,
    /// <summary>Adjoint of T.</summary>
    Tdg = 13,
    /// <summary>Phase diag(1,exp(i*angle)).</summary>
    Phase = 14,
    /// <summary>Validated custom single-qubit matrix.</summary>
    Unitary = 15,
    /// <summary>X controlled by two distinct qubits.</summary>
    Toffoli = 16
}

/// <summary>Immutable operation data emitted by validated circuit builders; not a standalone executable circuit.</summary>
/// <param name="Kind">Operation identifier.</param>
/// <param name="Target">Target qubit, using little-endian bit positions.</param>
/// <param name="Control">Control or first SWAP operand; -1 means no control.</param>
/// <param name="Angle">Finite angle in radians for parameterized gates.</param>
/// <param name="SecondControl">Second Toffoli control; -1 when absent.</param>
/// <param name="Unitary">Immutable matrix for a custom gate; null for built-in gates.</param>
public readonly record struct GateOperation(
    GateKind Kind, int Target, int Control = -1, double Angle = 0,
    int SecondControl = -1, SingleQubitUnitary? Unitary = null)
{
    /// <summary>Retains the original four-component operation deconstruction.</summary>
    public void Deconstruct(out GateKind kind, out int target, out int control, out double angle)
    {
        kind = Kind;
        target = Target;
        control = Control;
        angle = Angle;
    }
}

/// <summary>Mutable, validated unitary circuit. Qubit zero is the least significant basis-index bit.</summary>
/// <remarks>Gates execute in insertion order. Do not mutate the circuit during execution.</remarks>
public sealed class QuantumCircuit
{
    private readonly List<GateOperation> _operations = [];
    private readonly IReadOnlyList<GateOperation> _view;

    /// <summary>Creates an empty circuit with 1 through 30 qubits; this does not allocate a state.</summary>
    public QuantumCircuit(int qubits)
    {
        if (qubits is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(qubits), "Number of qubits must be between 1 and 30.");
        }

        QubitCount = qubits;
        _view = _operations.AsReadOnly();
    }

    /// <summary>The fixed register size.</summary>
    public int QubitCount { get; }
    /// <summary>A live, read-only view of validated operations in execution order.</summary>
    public IReadOnlyList<GateOperation> Operations => _view;

    internal QuantumCircuit Copy()
    {
        var copy = new QuantumCircuit(QubitCount);
        copy._operations.AddRange(_operations);
        return copy;
    }

    private QuantumCircuit Add(GateKind kind, int target, int? control = null, double angle = 0,
        int? secondControl = null, SingleQubitUnitary? unitary = null)
    {
        Validate(target);
        if (control.HasValue)
        {
            Validate(control.Value);
            if (target == control)
            {
                throw new ArgumentException("The two qubits must differ.", nameof(control));
            }
        }

        if (secondControl.HasValue)
        {
            Validate(secondControl.Value);
            if (secondControl == target || secondControl == control)
            {
                throw new ArgumentException("All three qubits must differ.", nameof(secondControl));
            }
        }

        if (!double.IsFinite(angle))
        {
            throw new ArgumentOutOfRangeException(nameof(angle));
        }

        _operations.Add(new GateOperation(kind, target, control ?? -1, angle, secondControl ?? -1, unitary));
        return this;
    }

    private void Validate(int qubit)
    {
        if ((uint)qubit >= (uint)QubitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(qubit));
        }
    }

    /// <summary>Appends a Hadamard gate on an in-range target.</summary>
    public QuantumCircuit H(int q) => Add(GateKind.H, q);
    /// <summary>Appends Pauli X, exchanging target-zero and target-one amplitudes.</summary>
    public QuantumCircuit X(int q) => Add(GateKind.X, q);
    /// <summary>Appends Pauli Y, preserving its imaginary phases.</summary>
    public QuantumCircuit Y(int q) => Add(GateKind.Y, q);
    /// <summary>Appends Pauli Z, negating target-one amplitudes.</summary>
    public QuantumCircuit Z(int q) => Add(GateKind.Z, q);
    /// <summary>Appends diag(1,i).</summary>
    public QuantumCircuit S(int q) => Add(GateKind.S, q);
    /// <summary>Appends diag(1,exp(i*pi/4)).</summary>
    public QuantumCircuit T(int q) => Add(GateKind.T, q);
    /// <summary>Appends exp(-i*angle*X/2), with a finite radian angle.</summary>
    public QuantumCircuit Rx(int q, double angle) => Add(GateKind.Rx, q, angle: angle);
    /// <summary>Appends exp(-i*angle*Y/2), with a finite radian angle.</summary>
    public QuantumCircuit Ry(int q, double angle) => Add(GateKind.Ry, q, angle: angle);
    /// <summary>Appends exp(-i*angle*Z/2), with a finite radian angle.</summary>
    public QuantumCircuit Rz(int q, double angle) => Add(GateKind.Rz, q, angle: angle);
    /// <summary>Flips the target on the control-one branch; operands must be distinct and in range.</summary>
    public QuantumCircuit CNot(int control, int target) => Add(GateKind.CNot, target, control);
    /// <summary>Exchanges distinct in-range qubit positions in either operand order.</summary>
    public QuantumCircuit Swap(int a, int b) => Add(GateKind.Swap, b, a);

    /// <summary>Appends identity on a validated target.</summary>
    public QuantumCircuit I(int q) => Add(GateKind.I, q);
    /// <summary>Appends the adjoint of S.</summary>
    public QuantumCircuit Sdg(int q) => Add(GateKind.Sdg, q);
    /// <summary>Appends the adjoint of T.</summary>
    public QuantumCircuit Tdg(int q) => Add(GateKind.Tdg, q);
    /// <summary>Appends diag(1,exp(i*angle)); differs from Rz by a global phase.</summary>
    public QuantumCircuit Phase(int q, double angle) => Add(GateKind.Phase, q, angle: angle);

    /// <summary>Controls a built-in single-qubit gate on |1>. Use ControlledU for a custom matrix.</summary>
    public QuantumCircuit Controlled(GateKind kind, int control, int target, double angle = 0)
    {
        if (kind is not (GateKind.H or GateKind.X or GateKind.Y or GateKind.Z or GateKind.S or GateKind.T
            or GateKind.Rx or GateKind.Ry or GateKind.Rz or GateKind.I or GateKind.Sdg or GateKind.Tdg or GateKind.Phase))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A built-in single-qubit gate is required.");
        }

        return Add(kind, target, control, angle);
    }

    /// <summary>Appends control-one Hadamard on distinct operands.</summary>
    public QuantumCircuit CH(int control, int target) => Controlled(GateKind.H, control, target);
    /// <summary>Appends control-one Pauli Y on distinct operands.</summary>
    public QuantumCircuit CY(int control, int target) => Controlled(GateKind.Y, control, target);
    /// <summary>Appends control-one Pauli Z on distinct operands.</summary>
    public QuantumCircuit CZ(int control, int target) => Controlled(GateKind.Z, control, target);
    /// <summary>Appends control-one S on distinct operands.</summary>
    public QuantumCircuit CS(int control, int target) => Controlled(GateKind.S, control, target);
    /// <summary>Appends control-one T on distinct operands.</summary>
    public QuantumCircuit CT(int control, int target) => Controlled(GateKind.T, control, target);
    /// <summary>Appends control-one S adjoint on distinct operands.</summary>
    public QuantumCircuit CSdg(int control, int target) => Controlled(GateKind.Sdg, control, target);
    /// <summary>Appends control-one T adjoint on distinct operands.</summary>
    public QuantumCircuit CTdg(int control, int target) => Controlled(GateKind.Tdg, control, target);
    /// <summary>Appends controlled Rx, retaining rotation global phase on the controlled branch.</summary>
    public QuantumCircuit CRx(int control, int target, double angle) => Controlled(GateKind.Rx, control, target, angle);
    /// <summary>Appends controlled Ry with a finite radian angle.</summary>
    public QuantumCircuit CRy(int control, int target, double angle) => Controlled(GateKind.Ry, control, target, angle);
    /// <summary>Appends controlled Rz with a finite radian angle.</summary>
    public QuantumCircuit CRz(int control, int target, double angle) => Controlled(GateKind.Rz, control, target, angle);
    /// <summary>Appends controlled diag(1,exp(i*angle)), preserving relative phase.</summary>
    public QuantumCircuit CPhase(int control, int target, double angle) => Controlled(GateKind.Phase, control, target, angle);

    /// <summary>Appends an immutable, prevalidated 2x2 unitary matrix on the target.</summary>
    public QuantumCircuit U(int target, SingleQubitUnitary unitary)
    {
        ArgumentNullException.ThrowIfNull(unitary);
        return Add(GateKind.Unitary, target, unitary: unitary);
    }

    /// <summary>Appends a custom unitary on the control-one branch, including all matrix phases.</summary>
    public QuantumCircuit ControlledU(int control, int target, SingleQubitUnitary unitary)
    {
        ArgumentNullException.ThrowIfNull(unitary);
        return Add(GateKind.Unitary, target, control, unitary: unitary);
    }

    /// <summary>Flips the target when both controls are one; all operands must be distinct and in range.</summary>
    public QuantumCircuit Toffoli(int firstControl, int secondControl, int target) =>
        Add(GateKind.Toffoli, target, firstControl, secondControl: secondControl);
}
