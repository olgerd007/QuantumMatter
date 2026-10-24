using QuantumSim.Core;

var circuit = new QuantumCircuit(2)
    .H(0)
    .CNot(control: 0, target: 1);

var simulator = new StateVectorSimulator();
var result = simulator.Run(circuit, shots: 1000, seed: 42);

Console.WriteLine("Bell state: (|00> + |11>) / sqrt(2)");
for (int index = 0; index < result.FinalState.Length; index++)
{
    string bits = Convert.ToString(index, 2).PadLeft(circuit.QubitCount, '0');
    Console.WriteLine($"|{bits}> probability = {result.FinalState.Probability(index):P2}");
}
Console.WriteLine("Sampled counts:");
foreach (var (bits, count) in result.Counts)
    Console.WriteLine($"|{bits}> = {count}");
