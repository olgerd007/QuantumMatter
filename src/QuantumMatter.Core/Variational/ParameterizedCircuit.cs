namespace QuantumMatter.Core.Variational;

/// <summary>A mutable template of unitary snapshots and parameter-indexed uncontrolled Rx/Ry/Rz rotations.</summary>
/// <remarks>Parameters may be shared or unused. Finish construction before binding/execution; concurrent mutation is unsupported.</remarks>
public sealed class ParameterizedCircuit
{
    private readonly List<TemplateOperation> _operations = [];
    private readonly List<int> _rotationParameters = [];

    /// <summary>Creates a 1–30-qubit template with a nonnegative parameter count, without allocating a state.</summary>
    public ParameterizedCircuit(int qubits, int parameterCount)
    {
        StateVector.RequiredBytes(qubits);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        QubitCount = qubits;
        ParameterCount = parameterCount;
        RotationParameterIndices = _rotationParameters.AsReadOnly();
    }

    /// <summary>The fixed register width.</summary>
    public int QubitCount { get; }
    /// <summary>The exact number of binding values, including unused indices.</summary>
    public int ParameterCount { get; }
    /// <summary>The number of gates after binding without a shift.</summary>
    public int OperationCount => _operations.Count;
    /// <summary>A live read-only parameter-index list in symbolic rotation occurrence order.</summary>
    public IReadOnlyList<int> RotationParameterIndices { get; }

    /// <summary>Snapshots every gate in a size-compatible constant unitary circuit.</summary>
    public ParameterizedCircuit Append(QuantumCircuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (circuit.QubitCount != QubitCount) { throw new ArgumentException("Circuit sizes must match.", nameof(circuit)); }
        foreach (GateOperation operation in circuit.Operations) { _operations.Add(new TemplateOperation(operation, -1)); }
        return this;
    }

    /// <summary>Appends an uncontrolled X rotation whose angle is the specified parameter.</summary>
    public ParameterizedCircuit Rx(int target, int parameterIndex) => AddRotation(GateKind.Rx, target, parameterIndex);
    /// <summary>Appends an uncontrolled Y rotation whose angle is the specified parameter.</summary>
    public ParameterizedCircuit Ry(int target, int parameterIndex) => AddRotation(GateKind.Ry, target, parameterIndex);
    /// <summary>Appends an uncontrolled Z rotation whose angle is the specified parameter.</summary>
    public ParameterizedCircuit Rz(int target, int parameterIndex) => AddRotation(GateKind.Rz, target, parameterIndex);

    /// <summary>Binds exactly ParameterCount finite angles into a new independently owned circuit.</summary>
    public QuantumCircuit Bind(ReadOnlySpan<double> parameters, CancellationToken cancellationToken = default) =>
        BindCore(parameters, -1, 0, cancellationToken);

    /// <summary>Binds a circuit with an additional same-axis rotation after one symbolic rotation occurrence.</summary>
    /// <remarks>Only that occurrence is shifted, even for shared parameters. The finite shift is composed without adding it to a possibly huge angle.</remarks>
    public QuantumCircuit BindWithRotationShift(ReadOnlySpan<double> parameters, int rotationIndex, double shift,
        CancellationToken cancellationToken = default)
    {
        if ((uint)rotationIndex >= (uint)_rotationParameters.Count) { throw new ArgumentOutOfRangeException(nameof(rotationIndex)); }
        if (!double.IsFinite(shift)) { throw new ArgumentOutOfRangeException(nameof(shift)); }
        return BindCore(parameters, rotationIndex, shift, cancellationToken);
    }

    private ParameterizedCircuit AddRotation(GateKind kind, int target, int parameterIndex)
    {
        if ((uint)target >= (uint)QubitCount) { throw new ArgumentOutOfRangeException(nameof(target)); }
        if ((uint)parameterIndex >= (uint)ParameterCount) { throw new ArgumentOutOfRangeException(nameof(parameterIndex)); }
        _operations.Add(new TemplateOperation(new GateOperation(kind, target), parameterIndex));
        _rotationParameters.Add(parameterIndex);
        return this;
    }

    private QuantumCircuit BindCore(ReadOnlySpan<double> parameters, int shiftedRotation, double shift, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameters.Length != ParameterCount) { throw new ArgumentException("Binding size must match ParameterCount.", nameof(parameters)); }
        for (int index = 0; index < parameters.Length; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            if (!double.IsFinite(parameters[index])) { throw new ArgumentOutOfRangeException(nameof(parameters), "Parameters must be finite."); }
        }

        var result = new QuantumCircuit(QubitCount);
        int rotation = 0;
        for (int index = 0; index < _operations.Count; index++)
        {
            if ((index & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            TemplateOperation entry = _operations[index];
            GateOperation operation = entry.Operation;
            if (entry.ParameterIndex >= 0)
            {
                result.AppendValidated(operation with { Angle = parameters[entry.ParameterIndex] });
                if (rotation == shiftedRotation) { result.AppendValidated(operation with { Angle = shift }); }
                rotation++;
            }
            else { result.AppendValidated(operation); }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private readonly record struct TemplateOperation(GateOperation Operation, int ParameterIndex);
}
