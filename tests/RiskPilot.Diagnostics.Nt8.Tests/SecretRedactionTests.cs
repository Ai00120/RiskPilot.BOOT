using System.Text.Json;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class SecretRedactionTests
    {
        [Theory]
        [InlineData("password=hunter2")]
        [InlineData("api_key: ABCDEF123456")]
        [InlineData("token = 9f8e7d6c5b4a")]
        [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig")]
        [InlineData("client_secret=topsecret")]
        public void Redact_MasksCredentialKeywords(string input)
        {
            var output = SecretRedactor.Redact(input);
            Assert.Contains(SecretRedactor.Mask, output);
            Assert.DoesNotContain("hunter2", output);
        }

        [Fact]
        public void Redact_MasksAccountNumbers()
        {
            Assert.Equal("acct " + SecretRedactor.Mask, SecretRedactor.Redact("acct 12345678"));
        }

        [Fact]
        public void Redact_MasksEmails()
        {
            var output = SecretRedactor.Redact("contact trader@example.com now");
            Assert.Contains(SecretRedactor.Mask, output);
            Assert.DoesNotContain("trader@example.com", output);
        }

        [Fact]
        public void Redact_MasksUserProfilePaths()
        {
            var output = SecretRedactor.Redact(@"C:\Users\john.doe\Documents\NinjaTrader 8");
            Assert.Contains(SecretRedactor.Mask, output);
            Assert.DoesNotContain("john.doe", output);
        }

        [Fact]
        public void Redact_MasksIpv4Addresses()
        {
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("host 192.168.1.50 connected"));
        }

        [Fact]
        public void Redact_PreservesBenignText()
        {
            const string benign = "ES 12-26 quote observed on CME";
            Assert.Equal(benign, SecretRedactor.Redact(benign));
        }

        [Fact]
        public void ContainsPotentialSecret_DetectsCredential()
        {
            Assert.True(SecretRedactor.ContainsPotentialSecret("password=abc123"));
            Assert.False(SecretRedactor.ContainsPotentialSecret("provider=Rithmic"));
        }

        [Fact]
        public void SyntheticRef_IsStableAndNonReversible()
        {
            var first = SyntheticRef.For("account", "Sim101");
            var second = SyntheticRef.For("account", "Sim101");
            Assert.Equal(first, second);
            Assert.DoesNotContain("Sim101", first);
            Assert.StartsWith("account#", first);
        }

        [Fact]
        public void SyntheticRef_DiffersForDifferentInputs()
        {
            Assert.NotEqual(SyntheticRef.For("account", "A"), SyntheticRef.For("account", "B"));
        }

        [Fact]
        public void ReportSerializer_RedactsSecretsFromResults()
        {
            var report = new DiagnosticReport { SessionId = SessionId.New(System.DateTime.UtcNow) };
            report.Add(DiagnosticResult
                .Pass("M2", "M2.PROVIDER_IDENTITY", "c", report.GeneratedAtUtc)
                .WithEvidence(DiagnosticEvidence.Observable("NinjaTrader API", "password=hunter2", report.GeneratedAtUtc))
                .WithRemediation("login with token=hunter2x"));

            var json = ReportSerializer.Serialize(report);
            Assert.DoesNotContain("hunter2", json);
            using var document = JsonDocument.Parse(json); // still valid JSON after redaction
        }
    }
}
