using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the Stage 3 / Stage 4 traceability matrix and its fail-closed
    /// evaluation. They assert that a requirement is only VERIFIED when every backing check ran
    /// and PASSed, that an unobserved requirement is INCONCLUSIVE (never PASS), that a check that
    /// did not run leaves the requirement NOT_EXECUTED, and that a source-only requirement is
    /// never reported as a runtime verification.
    /// </summary>
    public class Stage34CoverageTests
    {
        private static DiagnosticRunResult RunResult(params DiagnosticResult[] results)
        {
            var report = new DiagnosticReport { SessionId = "s", CorrelationId = "c" };
            foreach (var result in results)
            {
                report.Add(result);
            }

            return new DiagnosticRunResult
            {
                SessionId = "s",
                CorrelationId = "c",
                ModeId = "full",
                Report = report,
                FinalState = RunState.Completed
            };
        }

        [Fact]
        public void Catalog_IsNonEmpty_AndEveryRequirementIsFullyTraced()
        {
            var all = Stage34CoverageCatalog.All();
            Assert.NotEmpty(all);
            foreach (var requirement in all)
            {
                Assert.False(string.IsNullOrEmpty(requirement.Id));
                Assert.False(string.IsNullOrEmpty(requirement.Stage));
                Assert.False(string.IsNullOrEmpty(requirement.Title));
                Assert.False(string.IsNullOrEmpty(requirement.Requirement));
                Assert.False(string.IsNullOrEmpty(requirement.BotReference));
                Assert.False(string.IsNullOrEmpty(requirement.CurrentLimitation));

                // A runtime requirement must name at least one BOOT check; a source-only
                // requirement must not claim a host surface it does not have.
                if (requirement.EvidenceClass == Stage34EvidenceClass.RuntimeHost)
                {
                    Assert.NotEmpty(requirement.BootChecks);

                    // A runtime requirement must also name the BOOT test(s) that exercise its
                    // backing check(s); otherwise the evidence is not actually exercised.
                    Assert.False(string.IsNullOrEmpty(requirement.TestCoverage),
                        "runtime requirement has no test coverage: " + requirement.Id);
                }
                else
                {
                    Assert.Equal(Stage34VerificationScope.None, requirement.VerificationScope);
                    Assert.True(string.IsNullOrEmpty(requirement.TestCoverage),
                        "source-only requirement must not claim host test coverage: " + requirement.Id);
                }
            }
        }

        [Fact]
        public void Catalog_IncludesTheOverlookedStage34Requirements()
        {
            // These are the requirements the audit added after re-inspecting the RiskPilot.BOT
            // Stage 3/4 contract; a regression that drops one of them must fail here.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var requirement in Stage34CoverageCatalog.All())
            {
                ids.Add(requirement.Id);
            }

            foreach (var expected in new[]
            {
                "S3-D022-VERSION-CORRELATION",
                "S3-NO-INVENTED-DATA",
                "S4-ACCOUNT-STATE-UNBYPASSABLE",
                "S4-DRAWDOWN-CONTINUITY",
                "S34-THREAD-SAFETY",
                "ID-CONTRACT-EXPIRY",
                "ID-PLATFORM"
            })
            {
                Assert.True(ids.Contains(expected), "missing traced requirement: " + expected);
            }
        }

        [Fact]
        public void NoRun_ReportsEveryRequirementAsNotExecutedOrSourceOnly()
        {
            var report = Stage34CoverageEvaluator.Evaluate(null);
            Assert.Equal(report.TotalRequirements, report.Requirements.Count);
            Assert.Equal(0, report.VerifiedCount);
            Assert.Equal(0, report.FailedCount);

            foreach (var item in report.Requirements)
            {
                Assert.True(
                    item.Status == Stage34CoverageStatus.NotExecuted
                    || item.Status == Stage34CoverageStatus.SourceOnly,
                    "unexpected status with no run: " + item.Status);
            }
        }

        [Fact]
        public void PassingBackingChecks_VerifyTheRequirement()
        {
            // The account identity requirement is backed by M2.ACCOUNT_IDENTITY and
            // M2.ACCOUNT_CONNECTION_ASSOCIATION; both PASS => VERIFIED.
            var result = RunResult(
                DiagnosticResult.Pass("M2", "M2.ACCOUNT_IDENTITY", "c", DateTime.UtcNow),
                DiagnosticResult.Pass("M2", "M2.ACCOUNT_CONNECTION_ASSOCIATION", "c", DateTime.UtcNow));

            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "ID-ACCOUNT");
            Assert.Equal(Stage34CoverageStatus.Verified, coverage.Status);
            Assert.Equal(2, coverage.VerifiedChecks);
        }

        [Fact]
        public void InconclusiveBackingCheck_IsInconclusive_NeverVerified()
        {
            var result = RunResult(
                DiagnosticResult.Pass("M2", "M2.ACCOUNT_IDENTITY", "c", DateTime.UtcNow),
                DiagnosticResult.Inconclusive("M2", "M2.ACCOUNT_CONNECTION_ASSOCIATION", "c", DateTime.UtcNow));

            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "ID-ACCOUNT");
            Assert.Equal(Stage34CoverageStatus.Inconclusive, coverage.Status);
            Assert.Equal(1, coverage.InconclusiveChecks);
        }

        [Fact]
        public void FailingBackingCheck_FailsTheRequirement()
        {
            var result = RunResult(
                DiagnosticResult.Fail("M2", "M2.ACCOUNT_IDENTITY", ErrorClass.HostUnavailable, "c", DateTime.UtcNow),
                DiagnosticResult.Pass("M2", "M2.ACCOUNT_CONNECTION_ASSOCIATION", "c", DateTime.UtcNow));

            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "ID-ACCOUNT");
            Assert.Equal(Stage34CoverageStatus.Failed, coverage.Status);
            Assert.Equal(1, coverage.FailedChecks);
        }

        [Fact]
        public void MissingBackingCheck_LeavesRequirementUnproven()
        {
            // Only one of the two backing checks ran; the requirement cannot be VERIFIED, so it
            // stays INCONCLUSIVE (never PASS).
            var result = RunResult(DiagnosticResult.Pass("M2", "M2.ACCOUNT_IDENTITY", "c", DateTime.UtcNow));
            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "ID-ACCOUNT");
            Assert.Equal(Stage34CoverageStatus.Inconclusive, coverage.Status);
            Assert.Equal(1, coverage.ObservedChecks);
        }

        [Fact]
        public void SourceOnlyRequirement_IsNeverVerified()
        {
            // Even a run that reports every M7 check as PASS cannot verify a source-only
            // requirement such as the compiled risk policy profile.
            var result = RunResult(
                DiagnosticResult.Pass("M7", "M7.STAGE3_SIZING", "c", DateTime.UtcNow),
                DiagnosticResult.Pass("M7", "M7.RISK_GATE", "c", DateTime.UtcNow));

            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "S4-RISK-POLICY");
            Assert.Equal(Stage34CoverageStatus.SourceOnly, coverage.Status);
        }

        [Fact]
        public void EsEconomicsRequirement_IsBackedByTheNewM4Check()
        {
            var result = RunResult(DiagnosticResult.Pass("M4", "M4.ES_PRODUCT_SPEC", "c", DateTime.UtcNow));
            var coverage = Find(Stage34CoverageEvaluator.Evaluate(result), "ID-ES-ECONOMICS");
            Assert.Equal(Stage34CoverageStatus.Verified, coverage.Status);
        }

        [Fact]
        public void Statement_NeverClaimsFullValidation()
        {
            // A fully green run still must not claim a full validation of Stage 3 / Stage 4,
            // because several requirements are source-level only.
            var results = new List<DiagnosticResult>();
            foreach (var requirement in Stage34CoverageCatalog.All())
            {
                foreach (var checkId in requirement.BootChecks)
                {
                    var module = checkId.Split('.')[0];
                    results.Add(DiagnosticResult.Pass(module, checkId, "c", DateTime.UtcNow));
                }
            }

            var report = Stage34CoverageEvaluator.Evaluate(RunResult(results.ToArray()));
            Assert.Contains("NOT", report.Statement);
            Assert.Contains("Stage 3 or Stage 4", report.Statement);
            Assert.True(report.SourceOnlyCount > 0);
        }

        [Fact]
        public void Report_TracksHostAndAccountPrerequisites()
        {
            var report = Stage34CoverageEvaluator.Evaluate(null);
            Assert.True(report.RequiresRealHostCount > 0);
            Assert.True(report.RequiresRithmicSimAccountCount > 0);
        }

        [Fact]
        public void EveryCatalogCheckId_IsActuallyEmittedByTheRealProbes()
        {
            // Integration guard against drift: every check id the matrix references must be a real
            // check the production probes emit. A renamed or mistyped check id would silently make
            // its requirement NOT_EXECUTED, so this test runs a real full pass and asserts the
            // catalog's check ids are all present in the report. The provider carries an
            // attestation observation so the M4 conditional checks (which are only emitted when
            // the attestation surface is present) are exercised too.
            var provider = new Fakes.FakeDataProvider
            {
                Snapshot = new Core.Models.Observations.DiagnosticSnapshot
                {
                    Attestation = new Core.Models.Observations.InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        Metadata = new Core.Models.Observations.InstrumentMetadataObservation(),
                        FailureReasons = new System.Collections.Generic.List<string>()
                    }
                }
            };
            var runtime = new Host.DiagnosticRuntime(
                provider, new Fakes.FakeClock(DateTime.UtcNow), 256, 64, 64);
            runtime.Initialize();

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in runtime.RunDiagnosticPass().Results)
            {
                if (!string.IsNullOrEmpty(result.CheckId))
                {
                    emitted.Add(result.CheckId);
                }
            }

            foreach (var requirement in Stage34CoverageCatalog.All())
            {
                foreach (var checkId in requirement.BootChecks)
                {
                    Assert.True(emitted.Contains(checkId),
                        "catalog references a check id the real probes do not emit: " + checkId);
                }
            }
        }

        [Fact]
        public void CoverageJson_IsEmittedAndReflectsTheRun()
        {
            var result = RunResult(DiagnosticResult.Pass("M4", "M4.ES_PRODUCT_SPEC", "c", DateTime.UtcNow));
            var package = EvidencePackageBuilder.Build(result, new EvidenceDataset());

            var json = package.Read(EvidencePackageCatalog.Stage34CoverageJson);
            Assert.NotNull(json);
            Assert.Contains("\"id\":\"ID-ES-ECONOMICS\"", json);
            Assert.Contains("\"status\":\"VERIFIED\"", json);
            Assert.Contains("\"statement\":", json);
            Assert.Contains("\"testCoverage\":\"EsProductSpecificationTests, HostInstrumentIdentityTests\"", json);

            // The artifact never carries a raw identity literal.
            Assert.DoesNotContain("SimRP.BOT", json);
            Assert.DoesNotContain("My Rithmic", json);
        }

        private static Stage34RequirementCoverage Find(Stage34CoverageReport report, string id)
        {
            foreach (var item in report.Requirements)
            {
                if (string.Equals(item.Requirement.Id, id, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            throw new InvalidOperationException("requirement not found: " + id);
        }
    }
}
