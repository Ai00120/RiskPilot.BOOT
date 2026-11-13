using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Version/compatibility documentation alignment (Prompt 10). These tests pin the declared
    /// NinjaTrader target to the reported environment (8.1.9.0) while keeping the minimum, target
    /// and tested versions distinct and never presenting an unverified target as verified.
    /// </summary>
    public class NinjaTraderCompatibilityTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(System.DateTime.UtcNow), System.DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(System.DateTime.UtcNow), "corr00000000");
        }

        private static DiagnosticResult Find(IEnumerable<DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new System.InvalidOperationException("check not found: " + checkId);
        }

        [Fact]
        public void DeclaredTarget_IsNinjaTrader8190()
        {
            Assert.Equal("8.1.9.0", NinjaTraderCompatibility.TargetVersion);
            Assert.Equal("8.1.8.4", NinjaTraderCompatibility.MinimumSupportedVersion);
        }

        [Fact]
        public void MinimumAndTarget_AreKeptDistinct()
        {
            Assert.NotEqual(NinjaTraderCompatibility.MinimumSupportedVersion, NinjaTraderCompatibility.TargetVersion);
        }

        [Fact]
        public void Compatibility_IsNotVerifiedWithoutATestedHostBuild()
        {
            // The offline build environment has no NinjaTrader assemblies, so no host build may be
            // claimed as tested and the target must never be presented as verified.
            Assert.Empty(NinjaTraderCompatibility.TestedVersions);
            Assert.False(NinjaTraderCompatibility.CompatibilityVerifiedAtRuntime);
        }

        [Fact]
        public void CompatibilityStatement_DistinguishesTargetFromVerification()
        {
            var statement = NinjaTraderCompatibility.CompatibilityStatement();

            Assert.Contains(NinjaTraderCompatibility.TargetVersion, statement);
            Assert.Contains(NinjaTraderCompatibility.MinimumSupportedVersion, statement);
            Assert.Contains("NOT verified", statement);
        }

        [Fact]
        public void TargetWithVerificationStatus_NeverClaimsVerified()
        {
            Assert.Contains("not verified at runtime", NinjaTraderCompatibility.TargetWithVerificationStatus());
        }

        [Fact]
        public void M1_HostCompatibility_IsInconclusiveAndReportsTargetAndMinimum()
        {
            var results = new M1EnvironmentProbe().Evaluate(Context(new FakeDataProvider()));
            var check = Find(results, "M1.HOST_COMPATIBILITY");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, check.Status);
            Assert.Contains(NinjaTraderCompatibility.TargetVersion, check.Observation);
            Assert.Contains(NinjaTraderCompatibility.MinimumSupportedVersion, check.Observation);
            Assert.Contains("VERIFIED_AT_RUNTIME=NO", check.ReasonCodes);
        }

        [Fact]
        public void EnvironmentJson_CarriesCompatibilityBlock_WithDistinctVersions()
        {
            var dataset = new EvidenceDataset
            {
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation }
            };

            var package = EvidencePackageBuilder.Build(null, dataset);
            var environment = package.Read(EvidencePackageCatalog.EnvironmentJson);

            Assert.Contains("\"ninjaTraderCompatibility\"", environment);
            Assert.Contains("\"targetVersion\":\"" + NinjaTraderCompatibility.TargetVersion + "\"", environment);
            Assert.Contains("\"minimumSupportedVersion\":\"" + NinjaTraderCompatibility.MinimumSupportedVersion + "\"", environment);
            Assert.Contains("\"verifiedAtRuntime\":false", environment);
        }

        [Fact]
        public void EnvironmentJson_CompatibilityBlock_SurvivesSecretScan()
        {
            // A version string has the shape of an IPv4 address; the redactor must preserve the
            // tool's own declared versions so the compatibility evidence is not corrupted, while a
            // genuine IP address is still masked.
            var dataset = new EvidenceDataset
            {
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation }
            };

            var package = EvidencePackageBuilder.Build(null, dataset);
            Assert.True(package.IsSecretFree(out var offender), "secret found in " + offender);
            Assert.DoesNotContain(SecretRedactor.Mask, package.Read(EvidencePackageCatalog.EnvironmentJson));
        }

        [Theory]
        [InlineData("8.1.9.0")]
        [InlineData("8.1.8.4")]
        public void Redactor_PreservesDeclaredVersions(string version)
        {
            Assert.Equal(version, SecretRedactor.Redact(version));
        }

        [Fact]
        public void Redactor_StillMasksGenuineIpAddresses()
        {
            Assert.Equal("host " + SecretRedactor.Mask + " connected", SecretRedactor.Redact("host 192.168.1.50 connected"));
            // A near-miss version-like value that is not a declared version is still masked.
            Assert.Equal(SecretRedactor.Mask, SecretRedactor.Redact("9.9.9.9"));
        }
    }
}
