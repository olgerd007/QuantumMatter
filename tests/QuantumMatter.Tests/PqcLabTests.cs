#if NET10_0_OR_GREATER
using System.Security.Cryptography;
using System.Text.Json;
using QuantumMatter.CryptographyLab;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class PqcLabTests
{
    [Fact]
    public void Available_providers_pass_round_trips_tamper_checks_and_emit_raw_timings_without_secrets()
    {
        LabReport report = PqcExperiments.Run(2, 0);
        Assert.Equal(6, report.Primitives.Count);
        foreach (PrimitiveResult result in report.Primitives)
        {
            bool supported = result.Algorithm.StartsWith("ML-KEM", StringComparison.Ordinal)
                ? MLKem.IsSupported : MLDsa.IsSupported;
            Assert.Equal(supported ? "passed" : "unsupported", result.Status);
            if (!supported)
            {
                Assert.Empty(result.Measurements);
                Assert.Null(result.PrivateKeyBytes);
                continue;
            }

            Assert.True(result.PublicKeyBytes > 0);
            Assert.True(result.PrivateKeyBytes > 0);
            Assert.True(result.CiphertextOrSignatureBytes > 0);
            Assert.Equal(3, result.Measurements.Count);
            Assert.All(result.Measurements, measurement =>
            {
                Assert.Equal(2, measurement.Microseconds.Count);
                Assert.Equal(2, measurement.ManagedAllocatedBytes.Count);
                Assert.All(measurement.Microseconds, value => Assert.True(double.IsFinite(value) && value >= 0));
                Assert.All(measurement.ManagedAllocatedBytes, value => Assert.True(value >= 0));
                Assert.True(measurement.P95Microseconds >= measurement.P50Microseconds);
            });
        }

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(report));
        Assert.Equal(1024, document.RootElement.GetProperty("MessageBytes").GetInt32());
        Assert.Equal(6, document.RootElement.GetProperty("Primitives").GetArrayLength());
        Assert.Equal("net10.0", document.RootElement.GetProperty("TargetFramework").GetString());
        foreach (string privateField in new[] { "OperatingSystem", "Architecture", "LogicalProcessors", "ProcessorDescription", "Runtime" })
        {
            Assert.False(document.RootElement.TryGetProperty(privateField, out _));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PqcExperiments.Run(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PqcExperiments.Run(1, -1));
        Assert.Throws<OperationCanceledException>(() => PqcExperiments.Run(1, 0, cancellationToken: new CancellationToken(true)));
    }

    [Fact]
    public void Unsupported_status_never_carries_success_measurements()
    {
        PrimitiveResult result = PqcExperiments.Unsupported("unsupported-test-provider");
        Assert.Equal("unsupported", result.Status);
        Assert.Empty(result.Measurements);
        Assert.Null(result.ProviderType);
        Assert.Null(result.PublicKeyBytes);
    }
}
#endif
