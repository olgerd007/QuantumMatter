using System.Numerics;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class DynamicDensityTests
{
    private static readonly Complex[,] Identity = { { 1, 0 }, { 0, 1 } };
    private static readonly Complex[,] ZeroProjector = { { 1, 0 }, { 0, 0 } };
    private static readonly Complex[,] OneProjector = { { 0, 0 }, { 0, 1 } };
    private static readonly Complex[,] ResetOne = { { 0, 1 }, { 0, 0 } };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void Reset_trajectories_match_independent_dense_Kraus_density_matrices(int qubits, int target)
    {
        Complex[] initial = StateObservationTests.RandomState(qubits, new Random(673));
        Complex[,] expected = new Complex[initial.Length, initial.Length];
        AccumulateOuter(expected, Apply(Local(ZeroProjector, qubits, target), initial), 1);
        AccumulateOuter(expected, Apply(Local(ResetOne, qubits, target), initial), 1);
        Complex[,] actual = new Complex[initial.Length, initial.Length];
        for (int outcome = 0; outcome < 2; outcome++)
        {
            int sampledBasis = Enumerable.Range(0, initial.Length).First(basis => ((basis >> target) & 1) == outcome);
            double draw = initial.Take(sampledBasis).Sum(Probability) + Probability(initial[sampledBasis]) / 2;
            double branchMass = initial.Where((_, basis) => ((basis >> target) & 1) == outcome).Sum(Probability);
            var state = new StateVector(qubits);
            state.Evolve(new QuantumCircuit(qubits), new InputBackend(initial));
            Assert.Equal(0, state.EvolveDynamic(new DynamicCircuit(qubits, 1).Reset(target), new CpuStateVectorBackend(), new FixedRandom(draw)));
            Assert.InRange(Math.Abs(state.Snapshot().Sum(Probability) - 1), 0, 3e-12);
            Assert.All(Enumerable.Range(0, state.Length).Where(basis => ((basis >> target) & 1) != 0), basis => Assert.Equal(0, state.Probability(basis)));
            AccumulateOuter(actual, state.Snapshot(), branchMass);
        }

        for (int row = 0; row < initial.Length; row++)
        {
            for (int column = 0; column < initial.Length; column++)
            {
                Assert.InRange(Complex.Abs(expected[row, column] - actual[row, column]), 0, 3e-12);
            }
        }
    }

    [Fact]
    public void Interleaved_measurement_feedback_and_reset_match_exact_branch_enumeration()
    {
        const double theta = 0.86;
        var circuit = new DynamicCircuit(2, 2)
            .Append(new QuantumCircuit(2).Ry(0, theta).H(1).S(1).CNot(0, 1))
            .Measure([0], [1])
            .If(2, 0, new QuantumCircuit(2).H(1).Rz(0, 0.2))
            .If(2, 2, new QuantumCircuit(2).Ry(1, -0.4).CNot(1, 0))
            .Reset(0).Measure([1], [0]).If(3, 1, new QuantumCircuit(2).Z(1));

        Complex[,] h = { { Math.Sqrt(0.5), Math.Sqrt(0.5) }, { Math.Sqrt(0.5), -Math.Sqrt(0.5) } };
        Complex[,] s = { { 1, 0 }, { 0, Complex.ImaginaryOne } };
        Complex[,] rz = { { Complex.FromPolarCoordinates(1, -0.1), 0 }, { 0, Complex.FromPolarCoordinates(1, 0.1) } };
        Complex[,] cnot01 = { { 1, 0, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 }, { 0, 1, 0, 0 } };
        Complex[,] cnot10 = { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 } };
        Complex[] initial = [1, 0, 0, 0];
        initial = Apply(Local(Ry(theta), 2, 0), initial);
        initial = Apply(Local(h, 2, 1), initial);
        initial = Apply(Local(s, 2, 1), initial);
        initial = Apply(cnot01, initial);
        List<Branch> branches = MeasureBranches([new Branch(0, initial)], 0, 1);
        branches = branches.Select(branch => new Branch(branch.Classical, branch.Classical == 0
            ? Apply(Local(rz, 2, 0), Apply(Local(h, 2, 1), branch.State))
            : Apply(cnot10, Apply(Local(Ry(-0.4), 2, 1), branch.State)))).ToList();
        branches = branches.SelectMany(branch => new[]
        {
            new Branch(branch.Classical, Apply(Local(ZeroProjector, 2, 0), branch.State)),
            new Branch(branch.Classical, Apply(Local(ResetOne, 2, 0), branch.State))
        }).ToList();
        branches = MeasureBranches(branches, 1, 0);
        // Unnormalized branch norms carry their full history probability; no production measurement/kernel is used.
        double[] expected = new double[4];
        foreach (Branch branch in branches) { expected[branch.Classical] += branch.State.Sum(Probability); }
        Assert.InRange(Math.Abs(expected.Sum() - 1), 0, 3e-12);
        var simulator = new DynamicStateVectorSimulator(new CpuStateVectorBackend());
        var samples = simulator.Run(circuit, 12000, 6824);
        Assert.Equal(12000, samples.Counts.Values.Sum());
        for (int outcome = 0; outcome < 4; outcome++)
        {
            string label = Convert.ToString(outcome, 2).PadLeft(2, '0');
            double actual = samples.Counts.GetValueOrDefault(label) / 12000d;
            // More than six worst-case binomial standard deviations, with a fixed seed.
            Assert.InRange(Math.Abs(expected[outcome] - actual), 0, 0.03);
        }

        for (int seed = 0; seed < 32; seed++)
        {
            var trajectory = simulator.SimulateTrajectory(circuit, seed);
            int expectedBasis = (trajectory.ClassicalOutcome & 1) << 1;
            Assert.InRange(Math.Abs(trajectory.FinalState.Probability(expectedBasis) - 1), 0, 3e-12);
        }
    }

    private static List<Branch> MeasureBranches(List<Branch> input, int qubit, int classicalBit)
    {
        var output = new List<Branch>();
        foreach (Branch branch in input)
        {
            for (int outcome = 0; outcome < 2; outcome++)
            {
                int classical = (branch.Classical & ~(1 << classicalBit)) | (outcome << classicalBit);
                output.Add(new Branch(classical, Apply(Local(outcome == 0 ? ZeroProjector : OneProjector, 2, qubit), branch.State)));
            }
        }

        return output;
    }

    private static Complex[,] Ry(double angle) => new Complex[,]
    {
        { Math.Cos(angle / 2), -Math.Sin(angle / 2) },
        { Math.Sin(angle / 2), Math.Cos(angle / 2) }
    };

    private static Complex[,] Local(Complex[,] factor, int qubits, int target)
    {
        Complex[,] matrix = { { 1 } };
        for (int qubit = qubits - 1; qubit >= 0; qubit--)
        {
            Complex[,] next = qubit == target ? factor : Identity;
            int size = matrix.GetLength(0);
            var product = new Complex[size * 2, size * 2];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++) { product[2 * row + a, 2 * column + b] = matrix[row, column] * next[a, b]; }
                    }
                }
            }

            matrix = product;
        }

        return matrix;
    }

    private static Complex[] Apply(Complex[,] matrix, Complex[] state)
    {
        var result = new Complex[state.Length];
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++) { result[row] += matrix[row, column] * state[column]; }
        }

        return result;
    }

    private static void AccumulateOuter(Complex[,] matrix, Complex[] state, double weight)
    {
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++) { matrix[row, column] += weight * state[row] * Complex.Conjugate(state[column]); }
        }
    }

    private static double Probability(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
    private sealed record Branch(int Classical, Complex[] State);
    private sealed class FixedRandom(double value) : Random { public override double NextDouble() => value; }
    private sealed class InputBackend(Complex[] values) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => values.CopyTo(amplitudes.Span);
    }
}
