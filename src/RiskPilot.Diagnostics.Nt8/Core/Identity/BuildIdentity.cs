namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// Compile-time product identity. No reflection is used to discover these values;
    /// they are constants so that the assembly is self-describing without touching any
    /// undocumented API surface.
    /// </summary>
    public static class BuildIdentity
    {
        public const string ProductName = "RiskPilot NT8 Diagnostic Center";
        public const string Namespace = "RiskPilot.Diagnostics.Nt8";
        public const string ToolVersion = "1.0.0-phase2";
        public const string AssemblyVersionString = "1.0.0.0";
        public const string SchemaVersion = "rpdiag-1.0";

#if USE_STUBS
        /// <summary>
        /// Indicates the source was compiled against offline stubs rather than the real
        /// NinjaTrader assemblies. Real host compatibility is NOT established in this mode.
        /// </summary>
        public const string BuildMode = "StubOffline";
        public const bool IsStubBuild = true;
#else
        public const string BuildMode = "RealHost";
        public const bool IsStubBuild = false;
#endif
    }
}
