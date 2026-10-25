using QuantumMatter.Application;
using QuantumMatter.Core;

namespace QuantumMatter.Demo;

internal static class ObservationExamples
{
    internal static void Run(StateVectorSimulator simulator)
    {
        StateVector state = simulator.Simulate(new QuantumCircuit(3).H(0).CNot(0, 2).H(1).S(1));
        Console.WriteLine("Bell pair on q0/q2, with spectator q1 in (|0> + i|1>)/sqrt(2).");
        Console.WriteLine(FormattableString.Invariant($"P(q2=1) = {state.MarginalProbability([2], 1):F6}"));
        Console.WriteLine(FormattableString.Invariant($"<XIX> = {state.ExpectationValue(new PauliString("XIX")):F6}"));
        Console.WriteLine(FormattableString.Invariant($"<IYI> = {state.ExpectationValue(new PauliString("IYI")):F6}"));
        int outcome = state.Measure([2, 0], new Random(42));
        Console.WriteLine($"Measured [q2,q0]: packed outcome {Convert.ToString(outcome, 2).PadLeft(2, '0')} (q0 shown first).");
        Console.WriteLine(FormattableString.Invariant($"Spectator coherence survives: <IYI> = {state.ExpectationValue(new PauliString("IYI")):F6}"));
    }
}
