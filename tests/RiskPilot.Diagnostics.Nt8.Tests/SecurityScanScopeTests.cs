using System;
using System.Collections.Generic;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the M8 security-scan <b>scope</b>: the file-system source must scan
    /// only the RiskPilot.BOOT installation directory, must never traverse into a sibling AddOn
    /// directory or a parent directory, and must fail closed (INCONCLUSIVE, never PASS) when the
    /// BOOT directory is missing, invalid or only partially covered.
    /// <para>
    /// These exercise the real production <see cref="FileSystemSecurityScanSource"/> and
    /// <see cref="SecurityScanner"/> over a temporary directory tree; only the file-system
    /// location is arranged by the test, never the scan logic.
    /// </para>
    /// </summary>
    public class SecurityScanScopeTests
    {
        private static string TempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "rp-scope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Writes the minimal clean production files the scan needs to reach a PASS.</summary>
        private static void WriteCleanBoot(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "Host"));
            Directory.CreateDirectory(Path.Combine(root, "Core", "Identity"));
            File.WriteAllText(Path.Combine(root, "Host", "DiagnosticAddOn.cs"),
                "public sealed partial class DiagnosticAddOn { public const bool TradingDisabled = true; }");
            File.WriteAllText(Path.Combine(root, "Core", "Identity", "AccountIdentityVerifier.cs"),
                "public static class ExpectedAccountIdentity { public const string AccountName = \""
                + ExpectedAccountIdentity.AccountName + "\"; }");
        }

        private static void WriteForbidden(string path, string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "public class Evil { void Go() { account.SubmitOrder(); } }");
        }

        private static void TryDelete(string root)
        {
            RestoreAccess(root);
            try { Directory.Delete(root, true); } catch (Exception) { }
        }

        /// <summary>
        /// Restores read/write/execute permissions on every directory under <paramref name="root"/>
        /// (top-down, tolerating unreadable entries) so a directory the test locked can be deleted.
        /// </summary>
        private static void RestoreAccess(string directory)
        {
            if (IsPosix)
            {
                try { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
                catch (Exception) { }
            }

            string[] subdirectories;
            try { subdirectories = Directory.GetDirectories(directory); }
            catch (Exception) { return; }

            foreach (var sub in subdirectories)
            {
                RestoreAccess(sub);
            }
        }

        private static bool IsPosix
        {
            get { return Environment.OSVersion.Platform != PlatformID.Win32NT; }
        }

        /// <summary>
        /// Removes all permissions from a file or directory so it cannot be read/listed. On Windows
        /// the access-control route is not portable here, so the mode is left unchanged (the test
        /// then skips its assertions).
        /// </summary>
        private static void DenyAccess(string path)
        {
            if (!IsPosix)
            {
                return;
            }

            File.SetUnixFileMode(path, UnixFileMode.None);
        }

        // ---------------- correct BOOT directory ----------------

        [Fact]
        public void CorrectBootDirectory_IsScannedAndPasses()
        {
            var root = TempRoot();
            try
            {
                WriteCleanBoot(root);
                var source = new FileSystemSecurityScanSource(root);

                Assert.True(source.IsAvailable);
                Assert.True(source.IsComplete);

                var scan = SecurityScanner.Scan(source);
                Assert.True(scan.Executed);
                Assert.True(scan.SourceComplete);
                Assert.Equal(0, scan.HitCount);
                Assert.Contains(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void RealRepositoryBootDirectory_IsScannedCompletely_AndPasses()
        {
            var boot = LocateRepositoryBootDirectory();
            if (boot == null)
            {
                return; // repo layout not present in this checkout
            }

            var source = new FileSystemSecurityScanSource(boot);
            Assert.True(source.IsAvailable);
            Assert.True(source.IsComplete);

            var scan = SecurityScanner.Scan(source);
            Assert.True(scan.Executed);
            Assert.True(scan.SourceComplete);
            Assert.Equal(0, scan.HitCount);
            Assert.True(scan.TradingDisabledDeclared);
            Assert.True(scan.IdentityDefinitionReviewed);
            Assert.Contains(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
        }

        /// <summary>Walks up from the test assembly location to the repo's BOOT source folder.</summary>
        private static string LocateRepositoryBootDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "RiskPilot.Diagnostics.Nt8");
                if (File.Exists(Path.Combine(candidate, "Host", "DiagnosticAddOn.cs")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        // ---------------- unrelated sibling AddOn directories ----------------

        [Fact]
        public void SiblingAddOnDirectory_IsNeverScanned()
        {
            var parent = TempRoot();
            try
            {
                var boot = Path.Combine(parent, "RiskPilot.Diagnostics.Nt8");
                var sibling = Path.Combine(parent, "SomeOtherVendorAddOn");
                WriteCleanBoot(boot);
                WriteForbidden(Path.Combine(sibling, "Evil.cs"), sibling);

                var source = new FileSystemSecurityScanSource(boot);
                var scan = SecurityScanner.Scan(source);

                // The sibling's forbidden code must not appear as a BOOT finding.
                Assert.Equal(0, scan.HitCount);
                Assert.DoesNotContain(source.GetSourceFiles(), f => f.Contains("SomeOtherVendorAddOn"));
                Assert.DoesNotContain(source.GetSourceFiles(), f => f.Contains("Evil.cs"));
            }
            finally
            {
                TryDelete(parent);
            }
        }

        [Fact]
        public void CustomCompileLayout_ResolvesTheBootChildOfAddOns_NotTheAddOnsParent()
        {
            var parent = TempRoot();
            try
            {
                var documents = Path.Combine(parent, "Documents");
                var addOns = Path.Combine(documents, "NinjaTrader 8", "bin", "Custom", "AddOns");
                var boot = Path.Combine(addOns, "RiskPilot.Diagnostics.Nt8");
                WriteCleanBoot(boot);
                WriteForbidden(Path.Combine(addOns, "ThirdParty", "Evil.cs"), Path.Combine(addOns, "ThirdParty"));

                var resolved = FileSystemSecurityScanSource.ResolveDefaultRoot(null, documents);

                // The resolved root is the BOOT child, never the bare AddOns parent.
                Assert.Equal(Path.GetFullPath(boot), Path.GetFullPath(resolved));
                Assert.NotEqual(Path.GetFullPath(addOns), Path.GetFullPath(resolved));

                var scan = SecurityScanner.Scan(new FileSystemSecurityScanSource(resolved));
                Assert.Equal(0, scan.HitCount);
                Assert.True(scan.SourceComplete);
            }
            finally
            {
                TryDelete(parent);
            }
        }

        [Fact]
        public void BareAddOnsDirectory_WithNoBootChild_ResolvesToNothing()
        {
            var parent = TempRoot();
            try
            {
                var documents = Path.Combine(parent, "Documents");
                var addOns = Path.Combine(documents, "NinjaTrader 8", "bin", "Custom", "AddOns");
                // An unrelated AddOn with forbidden code sits directly under AddOns.
                WriteForbidden(Path.Combine(addOns, "OtherAddOn", "Evil.cs"), Path.Combine(addOns, "OtherAddOn"));

                var resolved = FileSystemSecurityScanSource.ResolveDefaultRoot(null, documents);

                // No BOOT child exists, so nothing is resolved: the bare AddOns parent is never
                // selected, so the unrelated AddOn can never be scanned.
                Assert.Equal(string.Empty, resolved);
            }
            finally
            {
                TryDelete(parent);
            }
        }

        // ---------------- parent-directory traversal + ".." path traversal ----------------

        [Fact]
        public void RelativePathTraversal_OutsideTheRoot_IsRefused()
        {
            var parent = TempRoot();
            try
            {
                var boot = Path.Combine(parent, "RiskPilot.Diagnostics.Nt8");
                WriteCleanBoot(boot);
                WriteForbidden(Path.Combine(parent, "Outside.cs"), parent);

                var source = new FileSystemSecurityScanSource(boot);

                Assert.Null(source.ReadFile("../Outside.cs"));
                Assert.Null(source.ReadFile("../../Outside.cs"));
                Assert.Null(source.ReadFile("Host/../../Outside.cs"));
                Assert.False(source.FileExists("../Outside.cs"));
                Assert.Null(source.ReadFile(Path.Combine(parent, "Outside.cs"))); // rooted
            }
            finally
            {
                TryDelete(parent);
            }
        }

        [Fact]
        public void RelativePathTraversal_InsideTheRoot_IsAllowed()
        {
            var root = TempRoot();
            try
            {
                WriteCleanBoot(root);
                var source = new FileSystemSecurityScanSource(root);

                // A ".." that stays inside the root is a legitimate in-tree path.
                var text = source.ReadFile("Host/../Core/Identity/AccountIdentityVerifier.cs");
                Assert.NotNull(text);
                Assert.True(source.FileExists("Core/Identity/../Identity/AccountIdentityVerifier.cs"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        // ---------------- symbolic links / reparse points ----------------

        [Fact]
        public void BootRootThatIsASymbolicLink_IsUnavailable()
        {
            var parent = TempRoot();
            try
            {
                var real = Path.Combine(parent, "RealBoot");
                WriteCleanBoot(real);
                var link = Path.Combine(parent, "LinkedBoot");
                try
                {
                    Directory.CreateSymbolicLink(link, real);
                }
                catch (Exception)
                {
                    return; // symbolic links unsupported in this environment
                }

                var source = new FileSystemSecurityScanSource(link);
                Assert.False(source.IsAvailable);
                Assert.False(SecurityScanner.Scan(source).Executed);
            }
            finally
            {
                TryDelete(parent);
            }
        }

        [Fact]
        public void SymbolicLinkedDirectoryInsideBoot_IsNotTraversed_AndMarksIncomplete()
        {
            var parent = TempRoot();
            try
            {
                var boot = Path.Combine(parent, "RiskPilot.Diagnostics.Nt8");
                var outside = Path.Combine(parent, "OutsideVendor");
                WriteCleanBoot(boot);
                WriteForbidden(Path.Combine(outside, "Evil.cs"), outside);

                try
                {
                    Directory.CreateSymbolicLink(Path.Combine(boot, "LinkedVendor"), outside);
                }
                catch (Exception)
                {
                    return; // symbolic links unsupported in this environment
                }

                var source = new FileSystemSecurityScanSource(boot);
                var scan = SecurityScanner.Scan(source);

                Assert.Equal(0, scan.HitCount);
                Assert.DoesNotContain(source.GetSourceFiles(), f => f.Contains("LinkedVendor"));
                Assert.False(source.IsComplete);
                Assert.Contains(SecurityScanReasonCodes.Incomplete, scan.ReasonCodes);
                Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(parent);
            }
        }

        [Fact]
        public void SymbolicLinkedFileInsideBoot_IsNotRead()
        {
            var parent = TempRoot();
            try
            {
                var boot = Path.Combine(parent, "RiskPilot.Diagnostics.Nt8");
                WriteCleanBoot(boot);
                var outside = Path.Combine(parent, "Outside.cs");
                WriteForbidden(outside, parent);

                try
                {
                    File.CreateSymbolicLink(Path.Combine(boot, "Linked.cs"), outside);
                }
                catch (Exception)
                {
                    return; // symbolic links unsupported in this environment
                }

                var source = new FileSystemSecurityScanSource(boot);

                // The symlinked file is refused both directly and as part of the enumerated set.
                Assert.Null(source.ReadFile("Linked.cs"));
                Assert.False(source.FileExists("Linked.cs"));
                Assert.DoesNotContain(source.GetSourceFiles(), f => f.Contains("Linked.cs"));

                var scan = SecurityScanner.Scan(source);
                Assert.Equal(0, scan.HitCount);
                Assert.False(scan.SourceComplete);
            }
            finally
            {
                TryDelete(parent);
            }
        }

        // ---------------- missing / empty / inaccessible ----------------

        [Fact]
        public void MissingBootDirectory_IsUnavailable_NeverPass()
        {
            var missing = Path.Combine(Path.GetTempPath(), "rp-missing-" + Guid.NewGuid().ToString("N"));
            var source = new FileSystemSecurityScanSource(missing);

            Assert.False(source.IsAvailable);
            Assert.False(source.IsComplete);
            var scan = SecurityScanner.Scan(source);
            Assert.False(scan.Executed);
            Assert.Equal(SecurityScanReasonCodes.SourceUnavailable, scan.UnavailableReason);
        }

        [Fact]
        public void EmptyBootDirectory_IsUnavailable_NotAFalselyCleanScan()
        {
            var root = TempRoot();
            try
            {
                var source = new FileSystemSecurityScanSource(root);
                Assert.False(source.IsAvailable);
                Assert.False(source.IsComplete);

                var scan = SecurityScanner.Scan(source);
                Assert.False(scan.Executed);
                Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void UnreadableDirectory_MarksTheScanIncomplete()
        {
            var root = TempRoot();
            try
            {
                WriteCleanBoot(root);
                var locked = Path.Combine(root, "Locked");
                Directory.CreateDirectory(locked);
                File.WriteAllText(Path.Combine(locked, "Hidden.cs"), "public class Hidden {}");
                DenyAccess(locked);

                // On a privileged (root) process the chmod is ineffective; skip rather than assert.
                if (CanList(locked))
                {
                    return;
                }

                var source = new FileSystemSecurityScanSource(root);
                Assert.True(source.IsAvailable);
                Assert.False(source.IsComplete);

                var scan = SecurityScanner.Scan(source);
                Assert.True(scan.Executed);
                Assert.False(scan.SourceComplete);
                Assert.Contains(SecurityScanReasonCodes.Incomplete, scan.ReasonCodes);
                Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void UnreadableFile_MarksTheScanIncomplete()
        {
            var root = TempRoot();
            try
            {
                WriteCleanBoot(root);
                var locked = Path.Combine(root, "Locked.cs");
                File.WriteAllText(locked, "public class Locked {}");
                DenyAccess(locked);

                if (CanRead(locked))
                {
                    return;
                }

                var source = new FileSystemSecurityScanSource(root);
                Assert.True(source.IsAvailable);

                var scan = SecurityScanner.Scan(source);
                Assert.True(scan.Executed);
                Assert.False(scan.SourceComplete);
                Assert.Contains(SecurityScanReasonCodes.Incomplete, scan.ReasonCodes);
                Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void UnreadablePathAttributes_AreTreatedAsUnsafe_FailClosed()
        {
            var root = TempRoot();
            try
            {
                WriteCleanBoot(root);

                // A directory that can be listed but not traversed: its entries are enumerated yet
                // their attributes cannot be read (File.GetAttributes throws). This is the real,
                // non-mocked trigger for the HasReparsePoint error path.
                var listed = Path.Combine(root, "Listed");
                Directory.CreateDirectory(listed);
                var inner = Path.Combine(listed, "Inner.cs");
                File.WriteAllText(inner, "public class Inner {}");

                if (!IsPosix)
                {
                    return; // Unix permission semantics are required to force the stat failure
                }

                File.SetUnixFileMode(listed, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

                // The OS still lists the entry; only its attributes are unreadable.
                Assert.Single(Directory.GetFiles(listed, "*.cs", SearchOption.TopDirectoryOnly));

                // A privileged (root) process bypasses the mode, so the trigger cannot be produced.
                if (CanStat(inner))
                {
                    return;
                }

                var source = new FileSystemSecurityScanSource(root);

                Assert.True(source.IsAvailable);
                // An entry whose attributes cannot be read is treated as unsafe, never silently
                // accepted into the scanned set.
                Assert.DoesNotContain(source.GetSourceFiles(), f => f.Contains("Inner.cs"));
                Assert.False(source.IsComplete);

                var scan = SecurityScanner.Scan(source);
                Assert.True(scan.Executed);
                Assert.False(scan.SourceComplete);
                Assert.Contains(SecurityScanReasonCodes.Incomplete, scan.ReasonCodes);
                Assert.DoesNotContain(SecurityScanReasonCodes.Passed, scan.ReasonCodes);
            }
            finally
            {
                TryDelete(root);
            }
        }

        // ---------------- M8 reporting of an incomplete scan ----------------

        [Fact]
        public void M8_SecurityScan_IsInconclusiveWhenTheSourceSetIsIncomplete()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Safety = new SafetyObservation() },
                SecurityScanSource = new FakeSecurityScanSource { IsComplete = false }
                    .Add("Host/DiagnosticAddOn.cs", "public sealed class DiagnosticAddOn { public const bool TradingDisabled = true; }")
                    .Add("Core/Identity/AccountIdentityVerifier.cs",
                        "public static class ExpectedAccountIdentity { public const string AccountName = \""
                        + ExpectedAccountIdentity.AccountName + "\"; }")
            };

            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            var scan = Find(results, "M8.SECURITY_SCAN");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, scan.Status);
            Assert.Contains(SecurityScanReasonCodes.Incomplete, scan.ReasonCodes);
        }

        // ---------------- helpers ----------------

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

        private static bool CanList(string directory)
        {
            try
            {
                Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CanRead(string file)
        {
            try
            {
                File.ReadAllText(file);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CanStat(string path)
        {
            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
