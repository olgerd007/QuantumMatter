using System.Numerics;
using QuantumMatter.Application;
using QuantumMatter.Core;
using QuantumMatter.Infrastructure;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class MeasurementTests
{
    private readonly StateVectorSimulator _simulator = new(new CpuStateVectorBackend());

    [Fact]
    public void Seeded_sampling_is_repeatable_and_leaves_final_state_uncollapsed()
    {
        var circuit = new QuantumCircuit(3).H(0).H(1).H(2);
        SimulationResult first = _simulator.Run(circuit, 4000, 73);
        SimulationResult second = _simulator.Run(circuit, 4000, 73);
        Assert.Equal(first.Counts.ToArray(), second.Counts.ToArray());
        Assert.Equal(4000, first.Counts.Values.Sum());
        Assert.Equal(8, first.Counts.Count);
        Assert.All(first.Counts.Values, count => Assert.InRange(count, 400, 600));
        Assert.All(Enumerable.Range(0, 8), index => Assert.InRange(first.FinalState.Probability(index), 0.125 - 1e-12, 0.125 + 1e-12));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)first.Counts).Clear());
    }

    [Fact]
    public void Bitstrings_are_little_endian_and_zero_padded()
    {
        Assert.Equal(10, _simulator.Run(new QuantumCircuit(4).X(0), 10).Counts["0001"]);
        Assert.Equal(10, _simulator.Run(new QuantumCircuit(4).X(3), 10).Counts["1000"]);
    }

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.5, 3)]
    [InlineData(0.9999999999999999, 3)]
    public void Measurement_skips_zero_mass_and_uses_strict_cdf_boundaries(double draw, int expected)
    {
        var state = _simulator.Simulate(new QuantumCircuit(2).X(0).H(1));
        Assert.Equal(expected, state.MeasureAll(new FixedRandom(draw)));
        Assert.Equal(1, state.Probability(expected));
        Assert.Equal(expected, state.MeasureAll(new FixedRandom(0.5)));
    }

    [Fact]
    public void Measurement_scales_draw_by_actual_mass_after_roundoff()
    {
        var state = new StateVector(2);
        state.Evolve(new QuantumCircuit(2), new AmplitudeBackend(new Complex[] { Math.Sqrt(1 - 1e-12), 0, 0, 0 }));
        Assert.Equal(0, state.MeasureAll(new FixedRandom(Math.BitDecrement(1.0))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_backend_probability_mass_is_rejected(double amplitude)
    {
        var backend = new AmplitudeBackend(new Complex[] { amplitude, 0 });
        var state = new StateVector(1);
        state.Evolve(new QuantumCircuit(1), backend);
        Assert.Throws<InvalidOperationException>(() => state.MeasureAll(new FixedRandom(0)));
        Assert.Throws<InvalidOperationException>(() => new StateVectorSimulator(backend).Run(new QuantumCircuit(1)));
    }

    [Fact]
    public void Backend_can_evolve_an_existing_arbitrary_complex_state()
    {
        var state = new StateVector(1);
        state.Evolve(new QuantumCircuit(1), new AmplitudeBackend(new[] { new Complex(0.5, 0.5), new Complex(0.5, -0.5) }));
        state.Evolve(new QuantumCircuit(1).H(0), new CpuStateVectorBackend());
        Assert.True(Complex.Abs(state.Amplitude(0) - Math.Sqrt(0.5)) < 1e-12);
        Assert.True(Complex.Abs(state.Amplitude(1) - Complex.ImaginaryOne * Math.Sqrt(0.5)) < 1e-12);
    }

    private sealed class FixedRandom(double draw) : Random
    {
        public override double NextDouble() => draw;
    }

    private sealed class AmplitudeBackend(Complex[] values) : IStateVectorBackend
    {
        public void Execute(QuantumCircuit circuit, Memory<Complex> amplitudes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.CopyTo(amplitudes.Span);
        }
    }
}
