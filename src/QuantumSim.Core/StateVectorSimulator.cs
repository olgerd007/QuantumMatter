using System.Numerics;

namespace QuantumSim.Core;

public sealed record SimulationResult(StateVector FinalState, IReadOnlyDictionary<string, int> Counts);

public sealed class StateVectorSimulator
{
    public StateVector Simulate(QuantumCircuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        var state = new StateVector(circuit.QubitCount);
        foreach (var op in circuit.Operations) Apply(state, op);
        return state;
    }

    public SimulationResult Run(QuantumCircuit circuit, int shots = 1024, int? seed = null)
    {
        if (shots < 1) throw new ArgumentOutOfRangeException(nameof(shots));
        var state = Simulate(circuit);
        // Terminal measurements only. Sample independently from the same final distribution.
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var probabilities = new double[state.Length];
        double total = 0;
        for (int i = 0; i < state.Length; i++) { total += state.Probability(i); probabilities[i] = total; }
        for (int shot = 0; shot < shots; shot++)
        {
            double draw = random.NextDouble() * total;
            int index = Array.BinarySearch(probabilities, draw);
            if (index < 0) index = ~index;
            // Handle zero-probability entries at exact boundaries.
            while (index < probabilities.Length - 1 && probabilities[index] <= draw) index++;
            var bitString = Convert.ToString(index, 2).PadLeft(circuit.QubitCount, '0');
            counts[bitString] = counts.GetValueOrDefault(bitString) + 1;
        }
        return new SimulationResult(state, counts);
    }

    private static void Apply(StateVector s, GateOperation op)
    {
        Complex i = Complex.ImaginaryOne;
        double c = Math.Cos(op.Angle / 2), sin = Math.Sin(op.Angle / 2);
        double h = 1 / Math.Sqrt(2);
        switch (op.Kind)
        {
            case GateKind.H: s.ApplyMatrix(op.Target, h, h, h, -h); break;
            case GateKind.X: s.ApplyMatrix(op.Target, 0, 1, 1, 0); break;
            case GateKind.Y: s.ApplyMatrix(op.Target, 0, -i, i, 0); break;
            case GateKind.Z: s.ApplyMatrix(op.Target, 1, 0, 0, -1); break;
            case GateKind.S: s.ApplyMatrix(op.Target, 1, 0, 0, i); break;
            case GateKind.T: s.ApplyMatrix(op.Target, 1, 0, 0, Complex.Exp(i * (Math.PI / 4))); break;
            case GateKind.Rx: s.ApplyMatrix(op.Target, c, -i * sin, -i * sin, c); break;
            case GateKind.Ry: s.ApplyMatrix(op.Target, c, -sin, sin, c); break;
            case GateKind.Rz: s.ApplyMatrix(op.Target, Complex.Exp(-i * op.Angle / 2), 0, 0, Complex.Exp(i * op.Angle / 2)); break;
            case GateKind.CNot: s.ApplyCNot(op.Control, op.Target); break;
            case GateKind.Swap: s.ApplySwap(op.Control, op.Target); break;
            default: throw new NotSupportedException($"Unsupported gate: {op.Kind}");
        }
    }
}
