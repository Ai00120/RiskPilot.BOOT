using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// M8-owned read-only security scan: the host-free <see cref="SecurityScanner"/> rule engine,
    /// the source contract/fail-closed behaviour, and the M8 probe that owns and reports it.
    /// The only double is the in-memory read-only source; the scan logic under test is production.
    /// </summary>
    public class SecurityScanTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
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

            throw new InvalidOperationException("check not found: " + checkId);
        }

        // ---------------- scanner: fail-closed ----------------

        [Fact]
        public void Scan_NullSource_IsNotExecuted_WithSourceUnavailable()
        {
            var scan = SecurityScanner.Scan(null);
            Assert.False(scan.Executed);
            Assert.False(scan.SourceAvailable);
            Assert.Equal(SecurityScanReasonCodes.SourceUnavailable, scan.UnavailableReason);
            Assert.Contains(SecurityScanReasonCodes.SourceUnavailable, scan.ReasonCodes);
        }

        [Fact]
        public void Scan_UnavailableSource_IsNotExecuted()
        {
            var scan = SecurityScanner.Scan(new FakeSecurityScanSource { IsAvailable = false });
            Assert.False(scan.Executed);
            Assert.Equal(SecurityScanReasonCodes.SourceUnavailable, scan.UnavailableReason);
        }

        [Fact]
        public void Scan_NoFiles_IsNotExecuted_WithNoSourceFiles()
        {
            var scan = SecurityScanner.Scan(new FakeSecurityScanSource());
            Assert.False(scan.Executed);
            Assert.Equal(SecurityScanReasonCodes.NoSourceFiles, scan.UnavailableReason);
        }

        [Fact]
        public void Scan_UnreadableFile_DoesNotProduceACleanPass()
        {
            // The AddOn and definition files are present, but one file in the set cannot be read:
            // the scan must not claim a clean PASS over an incompletely covered source set.
            var scan = SecurityScanner.Scan(new UnreadableSource("Core/Broken.cs"));

            Assert.True(scan.Executed);
            Assert.Contains(SecurityScanReasonCodes.Exception, scan.ReasonCodes);
            Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
        }

        // ---------------- scanner: forbidden patterns ----------------

        [Fact]
        public void Scan_ForbiddenOrderApi_IsAHit()
        {
            var source = FakeSecurityScanSource.Clean()
                .Add("Core/Engine/Probes/M9PostTradeAuditProbe.cs", "public class X { void Go() { account.SubmitOrder(); } }");
            var scan = SecurityScanner.Scan(source);

            Assert.True(scan.Executed);
            Assert.True(scan.HitCount > 0);
            Assert.Contains(scan.Hits, h => h.RuleId == "SUBMIT_ORDER");
            Assert.Contains(SecurityScanReasonCodes.ForbiddenApi, scan.ReasonCodes);
        }

        [Fact]
        public void Scan_ForbiddenReflection_OutsideAssemblyInfo_IsAHit()
        {
            var source = FakeSecurityScanSource.Clean()
                .Add("Core/Something.cs", "public class X { void Go() { var m = GetType().GetMethod(\"Y\"); } }");
            var scan = SecurityScanner.Scan(source);
            Assert.Contains(scan.Hits, h => h.Category == SecurityScanCategories.Reflection);
        }

        [Fact]
        public void Scan_ReflectionInsideAssemblyInfo_IsAllowed()
        {
            var source = FakeSecurityScanSource.Clean()
                .Add("Properties/AssemblyInfo.cs", "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]");
            var scan = SecurityScanner.Scan(source);
            Assert.DoesNotContain(scan.Hits, h => h.RuleId == "SYSTEM_REFLECTION");
        }

        [Fact]
        public void Scan_ForbiddenApiInCommentOrString_IsNotAHit()
        {
            var source = FakeSecurityScanSource.Clean()
                .Add("Core/Safe.cs", "// account.SubmitOrder() is documented but never called\nvar s = \"SubmitOrder\";");
            var scan = SecurityScanner.Scan(source);
            Assert.Equal(0, scan.HitCount);
        }

        // ---------------- scanner: identity confinement ----------------

        [Fact]
        public void Scan_IdentityLiteral_OutsideDefinitionFile_IsALeak()
        {
            var literal = ExpectedAccountIdentity.AccountName;
            var source = FakeSecurityScanSource.Clean()
                .Add("Core/Report/ReportSerializer.cs", "var name = \"" + literal + "\";");
            var scan = SecurityScanner.Scan(source);

            Assert.Contains(scan.Hits, h => h.RuleId == "IDENTITY_LITERAL");
            Assert.Contains(SecurityScanReasonCodes.IdentityLeak, scan.ReasonCodes);
        }

        [Fact]
        public void Scan_IdentityLiteral_InDefinitionFile_IsNotALeak()
        {
            var scan = SecurityScanner.Scan(FakeSecurityScanSource.Clean());
            Assert.DoesNotContain(scan.Hits, h => h.RuleId == "IDENTITY_LITERAL");
        }

        [Fact]
        public void Scan_DefinitionFileMissing_IsADefect()
        {
            var source = new FakeSecurityScanSource()
                .Add("Host/DiagnosticAddOn.cs", "public class DiagnosticAddOn { public const bool TradingDisabled = true; }");
            var scan = SecurityScanner.Scan(source);

            Assert.False(scan.IdentityDefinitionPresent);
            Assert.Contains(SecurityScanReasonCodes.DefinitionMissing, scan.ReasonCodes);
            Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
        }

        [Fact]
        public void Scan_DefinitionFileWithoutLiteral_IsADefect()
        {
            var source = FakeSecurityScanSource.Clean();
            source.Add("Core/Identity/AccountIdentityVerifier.cs", "public class Empty {}");
            var scan = SecurityScanner.Scan(source);

            Assert.True(scan.IdentityDefinitionPresent);
            Assert.False(scan.IdentityDefinitionReviewed);
            Assert.Contains(SecurityScanReasonCodes.DefinitionLiteralMissing, scan.ReasonCodes);
        }

        // ---------------- scanner: trading-disabled invariant ----------------

        [Fact]
        public void Scan_TradingDisabledDeclarationMissing_IsADefect()
        {
            var source = new FakeSecurityScanSource()
                .Add("Host/DiagnosticAddOn.cs", "public class DiagnosticAddOn { }")
                .Add("Core/Identity/AccountIdentityVerifier.cs", FakeSecurityScanSource.Clean().ReadFile("Core/Identity/AccountIdentityVerifier.cs"));
            var scan = SecurityScanner.Scan(source);

            Assert.True(scan.TradingDisabledObservable);
            Assert.False(scan.TradingDisabledDeclared);
            Assert.Contains(SecurityScanReasonCodes.TradingDisabledMissing, scan.ReasonCodes);
        }

        [Fact]
        public void Scan_AddOnFileAbsent_TradingDisabledIsUnobservable()
        {
            var source = new FakeSecurityScanSource()
                .Add("Core/Identity/AccountIdentityVerifier.cs", FakeSecurityScanSource.Clean().ReadFile("Core/Identity/AccountIdentityVerifier.cs"));
            var scan = SecurityScanner.Scan(source);

            Assert.False(scan.TradingDisabledObservable);
            Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
        }

        // ---------------- scanner: clean source ----------------

        [Fact]
        public void Scan_CleanSource_Passes()
        {
            var scan = SecurityScanner.Scan(FakeSecurityScanSource.Clean());

            Assert.True(scan.Executed);
            Assert.True(scan.SourceAvailable);
            Assert.Equal(0, scan.HitCount);
            Assert.True(scan.RuleCount > 0);
            Assert.Equal(2, scan.FilesScanned);
            Assert.True(scan.TradingDisabledDeclared);
            Assert.True(scan.IdentityDefinitionReviewed);
            Assert.Contains(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
        }

        // ---------------- M8 probe: ownership + reporting ----------------

        [Fact]
        public void M8_SecurityScan_PassesOnCleanSource()
        {
            var provider = new FakeDataProvider { SecurityScanSource = FakeSecurityScanSource.Clean() };
            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));

            var scan = Find(results, "M8.SECURITY_SCAN");
            Assert.Equal(DiagnosticStatus.PASS, scan.Status);
            Assert.Contains("SECURITY_SCAN_PASSED", scan.ReasonCodes);
        }

        [Fact]
        public void M8_SecurityScan_FailsOnForbiddenApi()
        {
            var provider = new FakeDataProvider
            {
                SecurityScanSource = FakeSecurityScanSource.Clean()
                    .Add("Core/Engine/Probes/M9PostTradeAuditProbe.cs", "public class X { void Go() { Connection.Connect(); } }")
            };
            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M8.SECURITY_SCAN").Status);
        }

        [Fact]
        public void M8_SecurityScan_IsInconclusiveWhenSourceUnavailable()
        {
            var provider = new FakeDataProvider { SecurityScanSource = new FakeSecurityScanSource { IsAvailable = false } };
            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M8.SECURITY_SCAN").Status);
        }

        [Fact]
        public void M8_SecurityScan_IsReportedEvenWithoutASafetyObservation()
        {
            // The scan is owned by M8, so a pass with no host safety observation must still report it.
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot(),
                SecurityScanSource = FakeSecurityScanSource.Clean()
            };
            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M8.SECURITY_SCAN").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M8.TRADING_DISABLED").Status);
        }

        [Fact]
        public void M8_SecurityScan_ResultIsPublishedToTheSession_ForExportWithoutReExecution()
        {
            var provider = new FakeDataProvider { SecurityScanSource = FakeSecurityScanSource.Clean() };
            var context = Context(provider);
            new M8SafetyAcceptanceProbe().Evaluate(context);

            Assert.NotNull(context.Session.SecurityScan);
            Assert.True(context.Session.SecurityScan.Executed);
        }

        // ---------------- host file-system source (real adapter, temp dir) ----------------

        [Fact]
        public void FileSystemSource_ReadsProductionSource_ReadOnlyAndPasses()
        {
            var root = Path.Combine(Path.GetTempPath(), "rp-scan-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = new RiskPilot.Diagnostics.Nt8.Host.FileSystemSecurityScanSource(root);
                Directory.CreateDirectory(Path.Combine(root, "Host"));
                Directory.CreateDirectory(Path.Combine(root, "Core", "Identity"));
                File.WriteAllText(Path.Combine(root, "Host", "DiagnosticAddOn.cs"),
                    "public sealed partial class DiagnosticAddOn { public const bool TradingDisabled = true; }");
                File.WriteAllText(Path.Combine(root, "Core", "Identity", "AccountIdentityVerifier.cs"),
                    "public static class ExpectedAccountIdentity { public const string AccountName = \""
                    + ExpectedAccountIdentity.AccountName + "\"; }");

                // The source is constructed before the files exist only when the folder is empty;
                // recreate it now that the production files are present.
                var scan = SecurityScanner.Scan(new RiskPilot.Diagnostics.Nt8.Host.FileSystemSecurityScanSource(root));

                Assert.True(scan.Executed);
                Assert.Equal(0, scan.HitCount);
                Assert.True(scan.TradingDisabledDeclared);
                Assert.Contains(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        [Fact]
        public void FileSystemSource_MissingRoot_IsUnavailable()
        {
            var source = new RiskPilot.Diagnostics.Nt8.Host.FileSystemSecurityScanSource(
                Path.Combine(Path.GetTempPath(), "rp-missing-" + Guid.NewGuid().ToString("N")));
            Assert.False(source.IsAvailable);
            Assert.False(SecurityScanner.Scan(source).Executed);
        }

        /// <summary>Read-only source whose <see cref="ReadFile"/> cannot read the named path.</summary>
        private sealed class UnreadableSource : RiskPilot.Diagnostics.Nt8.Core.Contract.ISecurityScanSource
        {
            private readonly FakeSecurityScanSource _inner;
            private readonly string _unreadable;

            public UnreadableSource(string unreadable)
            {
                _inner = FakeSecurityScanSource.Clean().Add(unreadable, "public class Broken {}");
                _unreadable = unreadable;
            }

            public bool IsAvailable { get { return true; } }

            public bool IsComplete { get { return true; } }

            public IReadOnlyList<string> GetSourceFiles() { return _inner.GetSourceFiles(); }

            public string ReadFile(string relativePath)
            {
                return relativePath == _unreadable ? null : _inner.ReadFile(relativePath);
            }

            public bool FileExists(string relativePath)
            {
                return relativePath != _unreadable && _inner.FileExists(relativePath);
            }
        }
    }
}
