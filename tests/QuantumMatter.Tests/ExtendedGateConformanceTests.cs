using System.Numerics;
using QuantumMatter.Core;
using Xunit;

namespace QuantumMatter.Tests;

public abstract partial class StateVectorBackendConformanceTests
{
    public static IEnumerable<object[]> SingleQubitGates => Enum.GetValues<GateKind>()
        .Where(kind => kind is not (GateKind.CNot or GateKind.Swap or GateKind.Toffoli))
        .Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(SingleQubitGates))]
    public void Controlled_gates_match_dense_operators_for_all_basis_states_and_operand_orders(GateKind kind)
    {
        for (int control = 0; control < 4; control++)
        {
            for (int target = 0; target < 4; target++)
            {
                if (control == target)
                {
                    continue;
                }

                var circuit = new QuantumCircuit(4);
                AppendControlled(circuit, kind, control, target, 0.713);
                for (int basis = 0; basis < 16; basis++)
                {
                    var input = new Complex[16];
                    input[basis] = 1;
                    Complex[] expected = DenseMultiply(input, circuit.Operations[0], 4);
                    CreateBackend().Execute(circuit, input);
                    for (int index = 0; index < input.Length; index++)
                    {
                        Near(expected[index], input[index]);
                    }
                }
            }
        }
    }

    [Fact]
    public void Toffoli_matches_truth_table_for_every_operand_order_and_spectator_value()
    {
        for (int first = 0; first < 4; first++)
        {
            for (int second = 0; second < 4; second++)
            {
                for (int target = 0; target < 4; target++)
                {
                    if (first == second || first == target || second == target)
                    {
                        continue;
                    }

                    for (int basis = 0; basis < 16; basis++)
                    {
                        bool enabled = ((basis >> first) & 1) == 1 && ((basis >> second) & 1) == 1;
                        int expected = enabled ? basis ^ (1 << target) : basis;
                        AssertBasis(Simulator.Simulate(BasisCircuit(4, basis).Toffoli(first, second, target)), expected);
                    }
                }
            }
        }
    }

    [Fact]
    public void Controlled_rotations_retain_global_phase_and_differ_from_phase_gates()
    {
        AssertBasis(Simulator.Simulate(new QuantumCircuit(2).H(0).CRz(0, 1, Math.Tau).H(0)), 1);
        AssertBasis(Simulator.Simulate(new QuantumCircuit(2).H(0).CPhase(0, 1, Math.Tau).H(0)), 0);
        var phase = SingleQubitUnitary.Create(Complex.ImaginaryOne, 0, 0, Complex.ImaginaryOne);
        AssertBasis(Simulator.Simulate(new QuantumCircuit(2).H(0).ControlledU(0, 1, phase).Sdg(0).H(0)), 0);
    }

    [Fact]
    public void Custom_unitary_preserves_row_order_and_complex_coefficients()
    {
        SingleQubitUnitary unitary = CustomUnitary();
        AssertBasis(Simulator.Simulate(new QuantumCircuit(1).U(0, unitary)), 1);
        Near(Complex.ImaginaryOne, Simulator.Simulate(new QuantumCircuit(1).X(0).U(0, unitary)).Amplitude(0));
        AssertBasis(Simulator.Simulate(new QuantumCircuit(1).H(0).U(0, unitary).U(0, unitary.Adjoint()).H(0)), 0);
    }

    [Theory]
    [InlineData(123)]
    [InlineData(397)]
    [InlineData(2026)]
    public void Entire_gate_set_matches_dense_oracle_and_inverse_on_entangled_states(int seed)
    {
        var random = new Random(seed);
        const int qubits = 4;
        var circuit = new QuantumCircuit(qubits).H(0).H(1).Ry(2, 0.312).CNot(1, 3);
        var expected = new Complex[1 << qubits];
        expected[0] = 1;
        foreach (GateOperation preparation in circuit.Operations)
        {
            expected = DenseMultiply(expected, preparation, qubits);
        }

        GateKind[] kinds = Enum.GetValues<GateKind>();
        // Each cycle includes every kind, so the seed cannot accidentally omit a gate.
        for (int cycle = 0; cycle < 12; cycle++)
        {
            foreach (GateKind kind in kinds)
            {
                int target = random.Next(qubits);
                int control = (target + random.Next(1, qubits)) % qubits;
                int second = Enumerable.Range(0, qubits).First(q => q != target && q != control);
                double angle = random.NextDouble() * 12 - 6;
                switch (kind)
                {
                    case GateKind.CNot:
                        circuit.CNot(control, target);
                        break;
                    case GateKind.Swap:
                        circuit.Swap(control, target);
                        break;
                    case GateKind.Toffoli:
                        circuit.Toffoli(control, second, target);
                        break;
                    case GateKind.Unitary:
                        SingleQubitUnitary custom = RandomUnitary(random);
                        if (cycle % 2 == 0)
                        {
                            circuit.U(target, custom);
                        }
                        else
                        {
                            circuit.ControlledU(control, target, custom);
                        }
                        break;
                    default:
                        if (cycle % 2 == 0)
                        {
                            Append(circuit, kind, target, angle);
                        }
                        else
                        {
                            AppendControlled(circuit, kind, control, target, angle);
                        }
                        break;
                }

                expected = DenseMultiply(expected, circuit.Operations[^1], qubits);
            }
        }

        StateVector actual = Simulator.Simulate(circuit);
        for (int index = 0; index < expected.Length; index++)
        {
            Near(expected[index], actual.Amplitude(index), 3e-12);
        }

        Assert.InRange(Enumerable.Range(0, actual.Length).Sum(actual.Probability), 1 - 1e-11, 1 + 1e-11);
        foreach (GateOperation operation in circuit.Operations.ToArray().Reverse())
        {
            AppendInverse(circuit, operation);
        }

        AssertBasis(Simulator.Simulate(circuit), 0, 1e-11);
    }

    [Theory]
    [MemberData(nameof(SingleQubitGates))]
    public void Backend_checks_cancellation_before_any_controlled_gate_mutation(GateKind kind)
    {
        var circuit = new QuantumCircuit(2);
        AppendControlled(circuit, kind, 0, 1, 0.7);
        Complex[] state = { 0, 1, 0, 0 };
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => CreateBackend().Execute(circuit, state, source.Token));
        Assert.Equal(new Complex[] { 0, 1, 0, 0 }, state);
    }

    internal static QuantumCircuit AppendControlled(QuantumCircuit circuit, GateKind kind, int control, int target, double angle) => kind switch
    {
        GateKind.H => circuit.CH(control, target),
        GateKind.Y => circuit.CY(control, target),
        GateKind.Z => circuit.CZ(control, target),
        GateKind.S => circuit.CS(control, target),
        GateKind.T => circuit.CT(control, target),
        GateKind.Sdg => circuit.CSdg(control, target),
        GateKind.Tdg => circuit.CTdg(control, target),
        GateKind.Rx => circuit.CRx(control, target, angle),
        GateKind.Ry => circuit.CRy(control, target, angle),
        GateKind.Rz => circuit.CRz(control, target, angle),
        GateKind.Phase => circuit.CPhase(control, target, angle),
        GateKind.Unitary => circuit.ControlledU(control, target, CustomUnitary()),
        _ => circuit.Controlled(kind, control, target, angle)
    };

    private static void AppendInverse(QuantumCircuit circuit, GateOperation operation)
    {
        if (operation.Kind == GateKind.CNot)
        {
            circuit.CNot(operation.Control, operation.Target);
        }
        else if (operation.Kind == GateKind.Swap)
        {
            circuit.Swap(operation.Control, operation.Target);
        }
        else if (operation.Kind == GateKind.Toffoli)
        {
            circuit.Toffoli(operation.Control, operation.SecondControl, operation.Target);
        }
        else if (operation.Kind == GateKind.Unitary)
        {
            SingleQubitUnitary inverse = operation.Unitary!.Adjoint();
            if (operation.Control >= 0)
            {
                circuit.ControlledU(operation.Control, operation.Target, inverse);
            }
            else
            {
                circuit.U(operation.Target, inverse);
            }
        }
        else
        {
            GateKind inverse = operation.Kind switch
            {
                GateKind.S => GateKind.Sdg,
                GateKind.Sdg => GateKind.S,
                GateKind.T => GateKind.Tdg,
                GateKind.Tdg => GateKind.T,
                _ => operation.Kind
            };
            if (operation.Control >= 0)
            {
                circuit.Controlled(inverse, operation.Control, operation.Target, -operation.Angle);
            }
            else
            {
                Append(circuit, inverse, operation.Target, -operation.Angle);
            }
        }
    }

    private static SingleQubitUnitary CustomUnitary() => SingleQubitUnitary.Create(0, Complex.ImaginaryOne, 1, 0);

    private static SingleQubitUnitary RandomUnitary(Random random)
    {
        double theta = random.NextDouble() * Math.Tau;
        Complex alpha = Complex.FromPolarCoordinates(Math.Cos(theta), random.NextDouble() * Math.Tau);
        Complex beta = Complex.FromPolarCoordinates(Math.Sin(theta), random.NextDouble() * Math.Tau);
        Complex phase = Complex.FromPolarCoordinates(1, random.NextDouble() * Math.Tau);
        return SingleQubitUnitary.Create(phase * alpha, phase * beta,
            -phase * Complex.Conjugate(beta), phase * Complex.Conjugate(alpha));
    }
}
