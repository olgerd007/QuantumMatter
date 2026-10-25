using System.Numerics;
using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class NoisyDynamicDensityTests
{
    private static readonly Complex[,] H = { { Math.Sqrt(0.5), Math.Sqrt(0.5) }, { Math.Sqrt(0.5), -Math.Sqrt(0.5) } };
    private static readonly Complex[,] S = { { 1, 0 }, { 0, Complex.ImaginaryOne } };
    private static readonly Complex[,] Z = { { 1, 0 }, { 0, -1 } };
    private static readonly Complex[,] P0 = { { 1, 0 }, { 0, 0 } };
    private static readonly Complex[,] P1 = { { 0, 0 }, { 0, 1 } };
    private static readonly Complex[,] R1 = { { 0, 1 }, { 0, 0 } };

    public static IEnumerable<object[]> Cases()
    {
        foreach (NoiseKind kind in Enum.GetValues<NoiseKind>())
        {
            foreach (string backend in new[] { "scalar", "parallel", "simd"
#if CUDA_TESTS
                , "cuda"
#endif
            }) { yield return [kind, backend]; }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_channel_and_measurement_branch_matches_dense_Kraus_feedback_and_reset(NoiseKind kind, string backendName)
    {
        const int qubits = 3;
        const double probability = 0.37;
        Complex[] initial = StateObservationTests.RandomState(qubits, new Random(852));
        IStateVectorBackend backend = NoisyDynamicCircuitTests.Backend(backendName);
        using IDisposable? owned = backend as IDisposable;
        for (int target = 0; target < qubits; target++)
        {
            int spectator = (target + 1) % qubits;
            var program = new NoisyDynamicCircuit(qubits, 3).ApplyNoise(NoiseChannelTests.Channel(kind, probability), target)
                .Measure([target], [2]).If(4, 4, new QuantumCircuit(qubits).H(spectator).S(spectator))
                .Reset(target).ApplyNoise(NoiseChannel.PhaseFlip(1), spectator).Measure([target], [0]);
            Complex[][] channelBranches = NoiseChannelTests.Kraus(kind, probability).Select(k => Apply(Local(k, qubits, target), initial)).ToArray();
            double total = channelBranches.Sum(Mass);
            var expectedDensity = new Complex[2, 8, 8];
            var actualDensity = new Complex[2, 8, 8];
            double noisePrefix = 0;
            foreach (Complex[] noiseBranch in channelBranches)
            {
                double noiseMass = Mass(noiseBranch);
                double noiseDraw = (noisePrefix + noiseMass / 2) / total;
                for (int outcome = 0; outcome < 2; outcome++)
                {
                    Complex[] projected = Apply(Local(outcome == 0 ? P0 : P1, qubits, target), noiseBranch);
                    double branchMass = Mass(projected);
                    // A damping jump already fixes the target to zero; its outcome-one branch is impossible.
                    if (branchMass == 0) { continue; }
                    int sampled = Enumerable.Range(0, 8).First(basis => ((basis >> target) & 1) == outcome && Probability(noiseBranch[basis]) > 0);
                    double measureDraw = (noiseBranch.Take(sampled).Sum(Probability) + Probability(noiseBranch[sampled]) / 2) / noiseMass;
                    if (outcome == 1)
                    {
                        projected = Apply(Local(S, qubits, spectator), Apply(Local(H, qubits, spectator), projected));
                    }

                    Complex[] expected = Apply(Local(outcome == 0 ? P0 : R1, qubits, target), projected);
                    expected = Apply(Local(Z, qubits, spectator), expected);
                    var state = new StateVector(qubits);
                    state.Evolve(new QuantumCircuit(qubits), new InputBackend(initial));
                    var random = new SequenceRandom([noiseDraw, measureDraw, 0.37, 0.61]);
                    Assert.Equal(outcome << 2, state.EvolveNoisyDynamic(program, backend, new CpuNoiseChannelBackend(), random));
                    Assert.Equal(4, random.Draws);
                    Assert.InRange(Math.Abs(state.Snapshot().Sum(Probability) - 1), 0, 4e-12);
                    NoisyDynamicCircuitTests.AssertAmplitudes(expected.Select(value => value / Math.Sqrt(branchMass)).ToArray(), state.Snapshot());
                    Accumulate(expectedDensity, outcome, expected, 1);
                    Accumulate(actualDensity, outcome, state.Snapshot(), branchMass);
                }

                noisePrefix += noiseMass;
            }

            double trace = 0;
            for (int outcome = 0; outcome < 2; outcome++)
            {
                for (int row = 0; row < 8; row++)
                {
                    trace += expectedDensity[outcome, row, row].Real;
                    for (int column = 0; column < 8; column++)
                    {
                        Assert.InRange(Complex.Abs(expectedDensity[outcome, row, column] - actualDensity[outcome, row, column]), 0, 4e-12);
                    }
                }
            }

            Assert.InRange(Math.Abs(trace - 1), 0, 4e-12);
        }
    }

    [Fact]
    public void Interleaved_noisy_classical_histories_match_independent_unnormalized_branch_enumeration()
    {
        var program = new NoisyDynamicCircuit(2, 2).Append(new QuantumCircuit(2).Ry(0, 0.86).H(1).S(1).CNot(0, 1))
            .ApplyNoise(NoiseChannel.BitFlip(0.27), 1).Measure([1], [0])
            .ApplyNoise(NoiseChannel.AmplitudeDamping(0.41), 0)
            .If(1, 1, new QuantumCircuit(2).Ry(0, -0.4).CNot(1, 0)).Measure([0], [1])
            .ApplyNoise(NoiseChannel.Depolarizing(0.33), 1).Reset(0).Measure([1], [0])
            .If(3, 2, new QuantumCircuit(2).H(1)).ApplyNoise(NoiseChannel.PhaseFlip(0.21), 1).Measure([1], [1]);
        Complex[,] cnot01 = { { 1, 0, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 }, { 0, 1, 0, 0 } };
        Complex[,] cnot10 = { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 0, 1 }, { 0, 0, 1, 0 } };
        Complex[] initial = Apply(cnot01, Apply(Local(S, 2, 1), Apply(Local(H, 2, 1), Apply(Local(Ry(0.86), 2, 0), [1, 0, 0, 0]))));
        List<Branch> branches = Noise([new Branch(0, initial)], NoiseKind.BitFlip, 0.27, 1);
        branches = Measure(branches, 1, 0);
        branches = Noise(branches, NoiseKind.AmplitudeDamping, 0.41, 0);
        branches = branches.Select(b => (b.Classical & 1) == 1
            ? b with { State = Apply(cnot10, Apply(Local(Ry(-0.4), 2, 0), b.State)) } : b).ToList();
        branches = Measure(branches, 0, 1);
        branches = Noise(branches, NoiseKind.Depolarizing, 0.33, 1);
        branches = branches.SelectMany(b => new[] { b with { State = Apply(Local(P0, 2, 0), b.State) }, b with { State = Apply(Local(R1, 2, 0), b.State) } }).ToList();
        branches = Measure(branches, 1, 0);
        branches = branches.Select(b => b.Classical == 2 ? b with { State = Apply(Local(H, 2, 1), b.State) } : b).ToList();
        branches = Noise(branches, NoiseKind.PhaseFlip, 0.21, 1);
        branches = Measure(branches, 1, 1);
        double[] expected = new double[4];
        foreach (Branch branch in branches) { expected[branch.Classical] += Mass(branch.State); }
        Assert.InRange(Math.Abs(expected.Sum() - 1), 0, 4e-12);
        var simulator = new NoisyDynamicStateVectorSimulator(new CpuStateVectorBackend(), new CpuNoiseChannelBackend());
        DynamicSimulationResult samples = simulator.Run(program, 16000, 772);
        Assert.Equal(16000, samples.Counts.Values.Sum());
        for (int outcome = 0; outcome < 4; outcome++)
        {
            string label = Convert.ToString(outcome, 2).PadLeft(2, '0');
            // Fixed seed and a bound above six worst-case binomial standard deviations.
            Assert.InRange(Math.Abs(samples.Counts.GetValueOrDefault(label) / 16000d - expected[outcome]), 0, 0.03);
        }
    }

    private static List<Branch> Noise(List<Branch> input, NoiseKind kind, double probability, int target) =>
        input.SelectMany(b => NoiseChannelTests.Kraus(kind, probability).Select(k => b with { State = Apply(Local(k, 2, target), b.State) })).ToList();

    private static List<Branch> Measure(List<Branch> input, int qubit, int destination) =>
        input.SelectMany(b => new[] { 0, 1 }.Select(outcome => new Branch((b.Classical & ~(1 << destination)) | (outcome << destination),
            Apply(Local(outcome == 0 ? P0 : P1, 2, qubit), b.State)))).ToList();

    private static Complex[,] Local(Complex[,] local, int qubits, int target)
    {
        int size = 1 << qubits;
        var result = new Complex[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                bool identityOnSpectators = true;
                for (int qubit = 0; qubit < qubits; qubit++)
                {
                    if (qubit != target && ((row >> qubit) & 1) != ((column >> qubit) & 1)) { identityOnSpectators = false; }
                }

                if (identityOnSpectators) { result[row, column] = local[(row >> target) & 1, (column >> target) & 1]; }
            }
        }

        return result;
    }

    private static Complex[] Apply(Complex[,] matrix, Complex[] input)
    {
        var output = new Complex[input.Length];
        for (int row = 0; row < input.Length; row++)
        {
            for (int column = 0; column < input.Length; column++) { output[row] += matrix[row, column] * input[column]; }
        }

        return output;
    }

    private static Complex[,] Ry(double angle) => new Complex[,] { { Math.Cos(angle / 2), -Math.Sin(angle / 2) }, { Math.Sin(angle / 2), Math.Cos(angle / 2) } };
    private static double Probability(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
    private static double Mass(Complex[] state) => state.Sum(Probability);
    private static void Accumulate(Complex[,,] density, int outcome, Complex[] state, double weight)
    {
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++) { density[outcome, row, column] += weight * state[row] * Complex.Conjugate(state[column]); }
        }
    }

    private sealed record Branch(int Classical, Complex[] State);
    private sealed class SequenceRandom(double[] values) : Random
    {
        public int Draws { get; private set; }
        public override double NextDouble() => values[Draws++];
    }

    private sealed class InputBackend(Complex[] input) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default) => input.CopyTo(amplitudes.Span);
    }
}
