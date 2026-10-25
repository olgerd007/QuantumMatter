using System.Numerics;
using QuantumMatter.Core.Noise;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class NoiseChannelTests
{
    private readonly CpuNoiseChannelBackend _backend = new();

    public static TheoryData<NoiseKind, double> InvalidProbabilities
    {
        get
        {
            var cases = new TheoryData<NoiseKind, double>();
            foreach (NoiseKind kind in Enum.GetValues<NoiseKind>())
            {
                foreach (double probability in new[] { -1d, -double.Epsilon, 1.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    cases.Add(kind, probability);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidProbabilities))]
    public void Channel_parameters_reject_nonfinite_and_out_of_range_values(NoiseKind kind, double probability) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Channel(kind, probability));

    [Theory]
    [InlineData(NoiseKind.BitFlip)]
    [InlineData(NoiseKind.PhaseFlip)]
    [InlineData(NoiseKind.Depolarizing)]
    [InlineData(NoiseKind.AmplitudeDamping)]
    public void Zero_noise_is_exact_identity_and_does_not_draw_randomness(NoiseKind kind)
    {
        Complex[] state = InitialState(8);
        Complex[] expected = (Complex[])state.Clone();
        _backend.Apply(Channel(kind, 0), 2, state, new NoDrawRandom());
        Assert.Equal(expected, state);
        Assert.Equal(Channel(kind, 0.5), Channel(kind, 0.5));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.25, 2)]
    [InlineData(0.5, 3)]
    [InlineData(0.75, 0)]
    public void Complete_depolarization_has_four_equal_Pauli_branches_with_strict_boundaries(double draw, int pauli)
    {
        Complex[] expected = DenseApply(InitialState(4), 0, Pauli(pauli));
        Complex[] state = InitialState(4);
        _backend.Apply(NoiseChannel.Depolarizing(1), 0, state, new FixedRandom(draw));
        Near(expected, state);
    }

    [Theory]
    [InlineData(NoiseKind.BitFlip)]
    [InlineData(NoiseKind.PhaseFlip)]
    [InlineData(NoiseKind.Depolarizing)]
    [InlineData(NoiseKind.AmplitudeDamping)]
    public void Conditional_states_and_weighted_density_match_independent_dense_Kraus_operators(NoiseKind kind)
    {
        const double probability = 0.37;
        for (int qubits = 1; qubits <= 4; qubits++)
        {
            for (int target = 0; target < qubits; target++)
            {
                Complex[] initial = InitialState(1 << qubits);
                var expectedDensity = new Complex[initial.Length, initial.Length];
                var actualDensity = new Complex[initial.Length, initial.Length];
                double cumulative = 0;
                foreach (Complex[,] kraus in Kraus(kind, probability))
                {
                    Complex[] unnormalized = DenseApply(initial, target, kraus);
                    double weight = Norm(unnormalized);
                    Complex[] actual = (Complex[])initial.Clone();
                    _backend.Apply(Channel(kind, probability), target, actual, new FixedRandom(cumulative + weight / 2));
                    cumulative += weight;
                    Complex[] conditional = unnormalized.Select(value => value / Math.Sqrt(weight)).ToArray();
                    Near(conditional, actual);
                    Assert.InRange(Math.Abs(Norm(actual) - 1), 0, 2e-12);
                    for (int row = 0; row < initial.Length; row++)
                    {
                        for (int column = 0; column < initial.Length; column++)
                        {
                            expectedDensity[row, column] += unnormalized[row] * Complex.Conjugate(unnormalized[column]);
                            actualDensity[row, column] += weight * actual[row] * Complex.Conjugate(actual[column]);
                        }
                    }
                }

                Assert.InRange(Math.Abs(cumulative - 1), 0, 2e-12);
                double trace = 0;
                for (int row = 0; row < initial.Length; row++)
                {
                    trace += actualDensity[row, row].Real;
                    for (int column = 0; column < initial.Length; column++)
                    {
                        Assert.InRange(Complex.Abs(actualDensity[row, column] - expectedDensity[row, column]), 0, 2e-12);
                    }
                }

                Assert.InRange(Math.Abs(trace - 1), 0, 2e-12);
            }
        }
    }

    [Fact]
    public void Damping_branch_boundary_and_impossible_branches_are_handled_without_division_by_zero()
    {
        // Equal superposition, gamma=0.5 -> jump probability 0.25; boundary selects no-jump.
        Complex[] state = [Math.Sqrt(0.5), Complex.ImaginaryOne * Math.Sqrt(0.5)];
        _backend.Apply(NoiseChannel.AmplitudeDamping(0.5), 0, state, new FixedRandom(0.25));
        Near(new Complex[] { Math.Sqrt(2d / 3), Complex.ImaginaryOne / Math.Sqrt(3) }, state);
        Complex[] ground = [Complex.ImaginaryOne, 0];
        _backend.Apply(NoiseChannel.AmplitudeDamping(1), 0, ground, new NoDrawRandom());
        Assert.Equal(new Complex[] { Complex.ImaginaryOne, 0 }, ground);
        Complex[] excited = [0, Complex.ImaginaryOne];
        _backend.Apply(NoiseChannel.AmplitudeDamping(1), 0, excited, new NoDrawRandom());
        Assert.Equal(new Complex[] { Complex.ImaginaryOne, 0 }, excited);
        Complex[] tiny = [0, Complex.ImaginaryOne];
        _backend.Apply(NoiseChannel.AmplitudeDamping(double.Epsilon), 0, tiny, new FixedRandom(0));
        Near(new Complex[] { Complex.ImaginaryOne, 0 }, tiny);
    }

    [Theory]
    [InlineData(NoiseKind.BitFlip)]
    [InlineData(NoiseKind.PhaseFlip)]
    public void Error_probability_boundary_selects_identity_and_unit_probability_needs_no_draw(NoiseKind kind)
    {
        Complex[] state = InitialState(2);
        Complex[] expected = (Complex[])state.Clone();
        _backend.Apply(Channel(kind, 0.5), 0, state, new FixedRandom(0.5));
        Assert.Equal(expected, state);
        _backend.Apply(Channel(kind, 1), 0, state, new NoDrawRandom());
        Near(DenseApply(expected, 0, Pauli(kind == NoiseKind.BitFlip ? 1 : 3)), state);
    }

    [Fact]
    public void Invalid_dimensions_targets_and_random_sources_are_rejected_before_mutation()
    {
        NoiseChannel channel = NoiseChannel.BitFlip(1);
        Assert.Throws<ArgumentNullException>(() => _backend.Apply(null!, 0, new Complex[2], new Random()));
        Assert.Throws<ArgumentNullException>(() => _backend.Apply(channel, 0, new Complex[2], null!));
        foreach (int size in new[] { 0, 1, 3, 6 })
        {
            Assert.Throws<ArgumentException>(() => _backend.Apply(channel, 0, new Complex[size], new Random()));
        }

        Complex[] state = [1, 0];
        Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Apply(channel, -1, state, new Random()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _backend.Apply(channel, 1, state, new Random()));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => _backend.Apply(channel, 0, state, new Random(), source.Token));
        Assert.Equal(new Complex[] { 1, 0 }, state);
        Assert.Throws<InvalidOperationException>(() => _backend.Apply(NoiseChannel.AmplitudeDamping(0.5), 0, new Complex[2], new Random()));
        Assert.Throws<InvalidOperationException>(() => _backend.Apply(NoiseChannel.AmplitudeDamping(0.5), 0,
            new Complex[] { double.NaN, 0 }, new Random()));
    }

    [Theory]
    [InlineData(NoiseKind.BitFlip)]
    [InlineData(NoiseKind.PhaseFlip)]
    [InlineData(NoiseKind.Depolarizing)]
    [InlineData(NoiseKind.AmplitudeDamping)]
    public void Cancellation_from_random_source_is_observed_before_writing_and_does_not_poison_backend(NoiseKind kind)
    {
        using var source = new CancellationTokenSource();
        Complex[] state = InitialState(8);
        Complex[] expected = (Complex[])state.Clone();
        Assert.Throws<OperationCanceledException>(() => _backend.Apply(Channel(kind, 0.5), 1, state,
            new CancellingRandom(source), source.Token));
        Assert.Equal(expected, state);
        _backend.Apply(Channel(kind, 0.5), 1, state, new Random(42));
        Assert.InRange(Math.Abs(Norm(state) - 1), 0, 1e-12);
    }

    [Theory]
    [InlineData(NoiseKind.BitFlip)]
    [InlineData(NoiseKind.PhaseFlip)]
    [InlineData(NoiseKind.Depolarizing)]
    [InlineData(NoiseKind.AmplitudeDamping)]
    public void Offset_buffers_preserve_sentinels_and_one_backend_supports_concurrent_calls(NoiseKind kind)
    {
        Parallel.For(0, 8, seed =>
        {
            Complex[] input = InitialState(32);
            Complex[] expected = (Complex[])input.Clone();
            _backend.Apply(Channel(kind, 0.4), 4, expected, new Random(seed));
            var storage = new Complex[36];
            Array.Fill(storage, new Complex(3, 7));
            input.CopyTo(storage, 2);
            _backend.Apply(Channel(kind, 0.4), 4, storage.AsMemory(2, 32), new Random(seed));
            Near(expected, storage.Skip(2).Take(32).ToArray());
            Assert.All(storage.Take(2).Concat(storage.TakeLast(2)), value => Assert.Equal(new Complex(3, 7), value));
        });
    }

    internal static NoiseChannel Channel(NoiseKind kind, double probability) => kind switch
    {
        NoiseKind.BitFlip => NoiseChannel.BitFlip(probability),
        NoiseKind.PhaseFlip => NoiseChannel.PhaseFlip(probability),
        NoiseKind.Depolarizing => NoiseChannel.Depolarizing(probability),
        NoiseKind.AmplitudeDamping => NoiseChannel.AmplitudeDamping(probability),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static IEnumerable<Complex[,]> Kraus(NoiseKind kind, double p)
    {
        if (kind == NoiseKind.AmplitudeDamping)
        {
            yield return new Complex[,] { { 0, Math.Sqrt(p) }, { 0, 0 } };
            yield return new Complex[,] { { 1, 0 }, { 0, Math.Sqrt(1 - p) } };
        }
        else if (kind == NoiseKind.Depolarizing)
        {
            foreach (int pauli in new[] { 1, 2, 3, 0 })
            {
                yield return Scale(Pauli(pauli), Math.Sqrt(pauli == 0 ? 1 - 3 * p / 4 : p / 4));
            }
        }
        else
        {
            yield return Scale(Pauli(kind == NoiseKind.BitFlip ? 1 : 3), Math.Sqrt(p));
            yield return Scale(Pauli(0), Math.Sqrt(1 - p));
        }
    }

    private static Complex[,] Pauli(int kind) => kind switch
    {
        0 => new Complex[,] { { 1, 0 }, { 0, 1 } },
        1 => new Complex[,] { { 0, 1 }, { 1, 0 } },
        2 => new Complex[,] { { 0, -Complex.ImaginaryOne }, { Complex.ImaginaryOne, 0 } },
        _ => new Complex[,] { { 1, 0 }, { 0, -1 } }
    };

    private static Complex[,] Scale(Complex[,] matrix, double scale)
    {
        var result = new Complex[2, 2];
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                result[row, column] = matrix[row, column] * scale;
            }
        }

        return result;
    }

    private static Complex[] DenseApply(Complex[] state, int target, Complex[,] local)
    {
        // Construct the dense tensor operator independently of production pair/permutation indexing.
        int bit = 1 << target;
        var dense = new Complex[state.Length, state.Length];
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++)
            {
                if ((row & ~bit) == (column & ~bit))
                {
                    dense[row, column] = local[(row & bit) == 0 ? 0 : 1, (column & bit) == 0 ? 0 : 1];
                }
            }
        }

        var result = new Complex[state.Length];
        for (int row = 0; row < state.Length; row++)
        {
            for (int column = 0; column < state.Length; column++)
            {
                result[row] += dense[row, column] * state[column];
            }
        }

        return result;
    }

    private static Complex[] InitialState(int size)
    {
        var random = new Random(712);
        var values = Enumerable.Range(0, size).Select(_ => new Complex(random.NextDouble() - 0.5, random.NextDouble() - 0.5)).ToArray();
        double scale = Math.Sqrt(Norm(values));
        return values.Select(value => value / scale).ToArray();
    }

    private static double Norm(Complex[] state) => state.Sum(value => value.Real * value.Real + value.Imaginary * value.Imaginary);
    private static void Near(Complex[] expected, Complex[] actual)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.InRange(Complex.Abs(expected[index] - actual[index]), 0, 2e-12);
        }
    }

    private sealed class FixedRandom(double draw) : Random { public override double NextDouble() => draw; }
    private sealed class NoDrawRandom : Random { public override double NextDouble() => throw new InvalidOperationException("Unexpected random draw."); }
    private sealed class CancellingRandom(CancellationTokenSource source) : Random
    {
        public override double NextDouble() { source.Cancel(); return 0; }
    }
}
