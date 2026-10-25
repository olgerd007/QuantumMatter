using QuantumMatter.Application.Dynamic;
using QuantumMatter.Core;
using QuantumMatter.Core.Dynamic;

namespace QuantumMatter.Demo;

internal static class DynamicExamples
{
    internal static void Run(DynamicStateVectorSimulator simulator)
    {
        const double theta = 0.73;
        const double phi = -0.61;
        var teleportation = new DynamicCircuit(3, 3)
            .Append(new QuantumCircuit(3).Ry(0, theta).Rz(0, phi).H(1).CNot(1, 2).CNot(0, 1).H(0))
            .Measure([0, 1], [0, 1])
            .If(2, 2, new QuantumCircuit(3).X(2))
            .If(1, 1, new QuantumCircuit(3).Z(2));
        var trajectory = simulator.SimulateTrajectory(teleportation, seed: 42);
        Console.WriteLine("Teleportation: measure q0/q1, then correct q2 using classical bits c0/c1.");
        Console.WriteLine($"Classical register: {Convert.ToString(trajectory.ClassicalOutcome, 2).PadLeft(3, '0')}");
        Console.WriteLine(FormattableString.Invariant($"Receiver <ZII> = {trajectory.FinalState.ExpectationValue(new PauliString("ZII")):F6}; expected cos(theta) = {Math.Cos(theta):F6}"));
        teleportation.Append(new QuantumCircuit(3).Rz(2, -phi).Ry(2, -theta)).Measure([2], [2]);
        var verified = simulator.Run(teleportation, shots: 128, seed: 42);
        Console.WriteLine("After inverting the receiver's preparation, c2 is zero in every shot:");
        foreach (var (bits, count) in verified.Counts) { Console.WriteLine($"{bits}: {count}"); }

        var reset = new DynamicCircuit(1, 2).Append(new QuantumCircuit(1).H(0))
            .Measure([0], [0]).Reset(0).Measure([0], [1]);
        Console.WriteLine("Reset: c0 records the earlier outcome; c1 is always zero after reset.");
        foreach (var (bits, count) in simulator.Run(reset, shots: 128, seed: 42).Counts) { Console.WriteLine($"{bits}: {count}"); }
    }
}
