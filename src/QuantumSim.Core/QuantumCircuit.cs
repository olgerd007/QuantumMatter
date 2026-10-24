namespace QuantumSim.Core;

public enum GateKind { H, X, Y, Z, S, T, Rx, Ry, Rz, CNot, Swap }

public readonly record struct GateOperation(GateKind Kind, int Target, int Control = -1, double Angle = 0);

public sealed class QuantumCircuit
{
    private readonly List<GateOperation> _operations = new();
    public int QubitCount { get; }
    public IReadOnlyList<GateOperation> Operations => _operations.AsReadOnly();

    public QuantumCircuit(int qubits)
    {
        if (qubits is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(qubits), "Number of qubits must be between 1 and 30.");
        QubitCount = qubits;
    }

    private QuantumCircuit Add(GateKind kind, int target, int control = -1, double angle = 0)
    {
        Validate(target);
        if (control >= 0)
        {
            Validate(control);
            if (target == control) throw new ArgumentException("Control and target must differ.");
        }
        if (!double.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        _operations.Add(new GateOperation(kind, target, control, angle));
        return this;
    }

    private void Validate(int qubit)
    {
        if ((uint)qubit >= (uint)QubitCount) throw new ArgumentOutOfRangeException(nameof(qubit));
    }

    public QuantumCircuit H(int q) => Add(GateKind.H, q);
    public QuantumCircuit X(int q) => Add(GateKind.X, q);
    public QuantumCircuit Y(int q) => Add(GateKind.Y, q);
    public QuantumCircuit Z(int q) => Add(GateKind.Z, q);
    public QuantumCircuit S(int q) => Add(GateKind.S, q);
    public QuantumCircuit T(int q) => Add(GateKind.T, q);
    public QuantumCircuit Rx(int q, double angle) => Add(GateKind.Rx, q, angle: angle);
    public QuantumCircuit Ry(int q, double angle) => Add(GateKind.Ry, q, angle: angle);
    public QuantumCircuit Rz(int q, double angle) => Add(GateKind.Rz, q, angle: angle);
    public QuantumCircuit CNot(int control, int target) => Add(GateKind.CNot, target, control);
    public QuantumCircuit Swap(int a, int b) => Add(GateKind.Swap, b, a);
}
