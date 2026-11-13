using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// The single, reviewed declaration of the NinjaTrader 8 host versions this tool targets.
    /// These are compile-time constants, not a runtime observation: no public NinjaTrader API
    /// exposes the installed host build (see <c>M1.HOST_VERSION</c>), so the tool can never
    /// confirm which of these versions is actually running.
    /// <para>
    /// The three versions are deliberately kept distinct:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Minimum</b> — the oldest host build the implementation claims to
    /// support. It is an implementation constraint, not a tested fact.</description></item>
    /// <item><description><b>Target</b> — the intended host build the project is aimed at. This
    /// is the value the documentation is aligned to.</description></item>
    /// <item><description><b>Tested</b> — the exact host build(s) on which the real-host build
    /// and a runtime session were actually exercised. This list is empty because the real
    /// NinjaTrader assemblies are not available in the offline build environment, so no host
    /// build has been verified at runtime.</description></item>
    /// </list>
    /// <para>
    /// Because <see cref="TestedVersions"/> is empty, <see cref="CompatibilityVerifiedAtRuntime"/>
    /// is <c>false</c> and every consumer must report the compatibility as unverified. Changing a
    /// documentation string never makes a version tested; only recording a real host build here
    /// after a successful real-host run would.
    /// </para>
    /// </summary>
    public static class NinjaTraderCompatibility
    {
        /// <summary>Oldest NinjaTrader 8 host build the implementation targets (implementation constraint, not tested).</summary>
        public const string MinimumSupportedVersion = "8.1.8.4";

        /// <summary>Intended NinjaTrader 8 host build the project is aimed at and documented against.</summary>
        public const string TargetVersion = "8.1.9.0";

        /// <summary>Major host family the tool compiles against.</summary>
        public const string HostFamily = "NinjaTrader 8";

        /// <summary>Process architecture the tool requires.</summary>
        public const string RequiredArchitecture = "64-bit";

        /// <summary>Compile-time target framework of the real-host build.</summary>
        public const string TargetFramework = ".NET Framework 4.8 (net48)";

        /// <summary>Language level of the production sources.</summary>
        public const string LanguageLevel = "C# 8.0";

        /// <summary>
        /// Host builds on which a real-host build and runtime session were actually verified.
        /// Empty: the NinjaTrader assemblies are not available in the offline build environment,
        /// so no host build has been exercised. Do not add a value here without a real-host run.
        /// </summary>
        public static readonly IReadOnlyList<string> TestedVersions = new string[0];

        /// <summary>
        /// True only when at least one host build has actually been verified at runtime. Always
        /// false while <see cref="TestedVersions"/> is empty; a consumer must never report the
        /// target as verified when this is false.
        /// </summary>
        public static bool CompatibilityVerifiedAtRuntime
        {
            get { return TestedVersions.Count > 0; }
        }

        /// <summary>The exact build against which the compiled product was validated, if any.</summary>
        public const string VerifiedBuild = "UNVERIFIED (NinjaTrader assemblies not available offline)";

        /// <summary>
        /// A single, human-readable statement of the compatibility posture that every document,
        /// report and probe uses verbatim, so no artifact can claim more than was verified.
        /// </summary>
        public static string CompatibilityStatement()
        {
            return "Targets " + HostFamily + " " + TargetVersion + " (" + RequiredArchitecture + "); "
                + "minimum supported " + MinimumSupportedVersion + "; "
                + "verified at runtime: " + (CompatibilityVerifiedAtRuntime ? Join(TestedVersions) : "NONE")
                + " — real-host compatibility is NOT verified without the installed NinjaTrader assemblies.";
        }

        /// <summary>
        /// The documented target plus its verification status, for a report or finding
        /// observation. Never presents the target as verified.
        /// </summary>
        public static string TargetWithVerificationStatus()
        {
            return TargetVersion + " (" + (CompatibilityVerifiedAtRuntime ? "verified at runtime" : "not verified at runtime") + ")";
        }

        private static string Join(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "NONE";
            }

            return string.Join(", ", values);
        }
    }
}
