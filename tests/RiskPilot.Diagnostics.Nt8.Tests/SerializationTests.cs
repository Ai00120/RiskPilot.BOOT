using System;
using System.Text.Json;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class SerializationTests
    {
        private static DiagnosticReport SampleReport()
        {
            var report = new DiagnosticReport
            {
                SessionId = SessionId.New(DateTime.UtcNow),
                CorrelationId = "corr12345678",
                GeneratedAtUtc = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc),
                SessionStartedAtUtc = new DateTime(2026, 11, 12, 7, 59, 0, DateTimeKind.Utc),
                EnvironmentClass = "SIMULATION"
            };

            report.Add(DiagnosticResult
                .Pass("M1", "M1.API_SURFACE", "corr12345678", report.GeneratedAtUtc)
                .WithReasonCode("ALL_REQUIRED_APIS_PRESENT")
                .WithEvidence(DiagnosticEvidence.Observable("NinjaTrader Public API", "present=7", report.GeneratedAtUtc)));

            report.Add(DiagnosticResult
                .Inconclusive("M2", "M2.PROVIDER_IDENTITY", "corr12345678", report.GeneratedAtUtc)
                .WithReasonCode("PROVIDER_NOT_EXPECTED"));

            report.AddUnobservable("HOST_VERSION: no public API.");
            return report;
        }

        [Fact]
        public void Serialize_ProducesValidJson()
        {
            var json = ReportSerializer.Serialize(SampleReport());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("rpdiag-1.0", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("INCONCLUSIVE", root.GetProperty("aggregateStatus").GetString());
            Assert.True(root.GetProperty("tradingDisabled").GetBoolean());
            Assert.Equal(2, root.GetProperty("results").GetArrayLength());
        }

        [Fact]
        public void Serialize_IncludesCountersObject()
        {
            var json = ReportSerializer.Serialize(SampleReport());
            using var document = JsonDocument.Parse(json);
            var counters = document.RootElement.GetProperty("counters");
            Assert.Equal(0, counters.GetProperty("dropped").GetInt64());
        }

        [Fact]
        public void Serialize_EscapesSpecialCharacters()
        {
            var report = new DiagnosticReport { SessionId = SessionId.New(DateTime.UtcNow) };
            report.Add(DiagnosticResult
                .Pass("M1", "M1.API_SURFACE", "c", report.GeneratedAtUtc)
                .WithRemediation("line1\nline2\t\"quoted\"\\slash"));

            var json = ReportSerializer.Serialize(report);
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement.GetProperty("results")[0].GetProperty("remediationHint").GetString();
            Assert.Contains("\n", value);
            Assert.Contains("\"quoted\"", value);
        }

        [Fact]
        public void Serialize_IsCultureInvariantForNumbers()
        {
            var report = new DiagnosticReport { SessionId = SessionId.New(DateTime.UtcNow) };
            report.Add(DiagnosticResult.Pass("M1", "M1.X", "c", report.GeneratedAtUtc));
            var json = ReportSerializer.Serialize(report);

            // No comma decimal separators should ever appear in the numeric fields.
            using var document = JsonDocument.Parse(json);
            Assert.Equal(0, document.RootElement.GetProperty("counters").GetProperty("enqueued").GetInt64());
        }

        [Fact]
        public void Serialize_EmptyReport_AggregatesToNotApplicable()
        {
            var report = new DiagnosticReport { SessionId = SessionId.New(DateTime.UtcNow) };
            Assert.Equal(DiagnosticStatus.NOT_APPLICABLE, report.AggregateStatus());
            var json = ReportSerializer.Serialize(report);
            using var document = JsonDocument.Parse(json);
            Assert.Equal("NOT_APPLICABLE", document.RootElement.GetProperty("aggregateStatus").GetString());
        }
    }
}
