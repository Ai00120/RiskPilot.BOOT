using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Security
{
    /// <summary>
    /// A single read-only security-scan rule. The rule is applied to a source file's
    /// comment/string-stripped text, so a rule keyword that only appears inside a comment or a
    /// string literal is never a false positive. This rule set is the in-process counterpart of
    /// the offline <c>verification/addon-smoke</c> scan and preserves the same checks.
    /// </summary>
    public sealed class SecurityScanRule
    {
        /// <summary>Stable rule id (also used as a reason code fragment).</summary>
        public string Id { get; set; }

        /// <summary>Group the rule belongs to (order execution, account mutation, ...).</summary>
        public string Category { get; set; }

        /// <summary>Human-readable description of what the rule forbids.</summary>
        public string Description { get; set; }

        /// <summary>Regular expression applied to the stripped source text.</summary>
        public string Pattern { get; set; }

        /// <summary>True when the pattern is matched case-insensitively.</summary>
        public bool CaseInsensitive { get; set; }

        public SecurityScanRule()
        {
            Id = string.Empty;
            Category = string.Empty;
            Description = string.Empty;
            Pattern = string.Empty;
        }
    }

    /// <summary>Rule categories. They are stable so an artifact or finding can group hits.</summary>
    public static class SecurityScanCategories
    {
        public const string OrderExecution = "ORDER_EXECUTION";
        public const string AccountMutation = "ACCOUNT_MUTATION";
        public const string LiveTradingEnable = "LIVE_TRADING_ENABLE";
        public const string Reflection = "REFLECTION";
        public const string UnsafeInterop = "UNSAFE_INTEROP";
        public const string IdentityLeak = "IDENTITY_LEAK";
        public const string Invariant = "INVARIANT";
    }

    /// <summary>Stable reason codes emitted by the scanner. The wire values never change.</summary>
    public static class SecurityScanReasonCodes
    {
        public const string Passed = "SECURITY_SCAN_PASSED";
        public const string SourceUnavailable = "SECURITY_SCAN_SOURCE_UNAVAILABLE";
        public const string NoSourceFiles = "SECURITY_SCAN_NO_SOURCE_FILES";
        public const string ForbiddenApi = "SECURITY_SCAN_FORBIDDEN_API";
        public const string IdentityLeak = "SECURITY_SCAN_IDENTITY_LEAK";
        public const string TradingDisabledMissing = "SECURITY_SCAN_TRADING_DISABLED_MISSING";
        public const string DefinitionMissing = "SECURITY_SCAN_IDENTITY_DEFINITION_MISSING";
        public const string DefinitionLiteralMissing = "SECURITY_SCAN_IDENTITY_DEFINITION_LITERAL_MISSING";
        public const string Incomplete = "SECURITY_SCAN_INCOMPLETE";
        public const string Exception = "SECURITY_SCAN_EXCEPTION";
    }

    /// <summary>
    /// The read-only security rule catalogue. It mirrors the offline safety scan
    /// (<c>verification/addon-smoke/test_diagnostic_safety.py</c>) exactly, so no check is lost
    /// when the scan runs inside M8 rather than out of band:
    /// <list type="bullet">
    /// <item>no order submission/modification/cancellation API;</item>
    /// <item>no account/connection/position mutation API;</item>
    /// <item>no live-trading enable path;</item>
    /// <item>no reflection, <c>unsafe</c>, <c>stackalloc</c> or native interop;</item>
    /// <item>the Owner identity literals appear only in the reviewed identity definition file;</item>
    /// <item>the AddOn declares <c>TradingDisabled</c>.</item>
    /// </list>
    /// The identity literals are never hardcoded here: they are read from the production
    /// <see cref="Core.Identity.ExpectedAccountIdentity"/> constants, so the scanner cannot itself
    /// become an identity-leak site.
    /// </summary>
    public static class SecurityScanCatalog
    {
        /// <summary>Production source root scanned by the catalogue (repo-relative, POSIX form).</summary>
        public const string ProductionSourceRoot = "src/RiskPilot.Diagnostics.Nt8";

        /// <summary>
        /// The only production file allowed to contain <see cref="System.Reflection"/> (assembly
        /// attributes only).
        /// </summary>
        public const string ReflectionAllowedFile =
            "src/RiskPilot.Diagnostics.Nt8/Properties/AssemblyInfo.cs";

        /// <summary>The reviewed file that defines the Owner identity literals.</summary>
        public const string IdentityDefinitionFile =
            "src/RiskPilot.Diagnostics.Nt8/Core/Identity/AccountIdentityVerifier.cs";

        /// <summary>The AddOn file that must declare the <c>TradingDisabled</c> invariant.</summary>
        public const string TradingDisabledDeclarationFile =
            "src/RiskPilot.Diagnostics.Nt8/Host/DiagnosticAddOn.cs";

        /// <summary>Token that must appear in <see cref="TradingDisabledDeclarationFile"/>.</summary>
        public const string TradingDisabledToken = "TradingDisabled";

        /// <summary>Path fragments excluded from the scan (offline stubs and build output).</summary>
        public static readonly IReadOnlyList<string> ExcludedPathFragments = new[]
        {
            "/Stubs/",
            "/obj/",
            "/bin/"
        };

        /// <summary>The ordered forbidden-pattern rules (mirrors the offline scan's rule list).</summary>
        public static readonly IReadOnlyList<SecurityScanRule> Rules = new[]
        {
            Rule("SUBMIT_ORDER", SecurityScanCategories.OrderExecution, "Order submission API", @"\bSubmitOrder\b"),
            Rule("SUBMIT_ORDER_UNMANAGED", SecurityScanCategories.OrderExecution, "Unmanaged order submission API", @"\bSubmitOrderUnmanaged\b"),
            Rule("SUBMIT_ORDER_ASYNC", SecurityScanCategories.OrderExecution, "Async order submission API", @"\bSubmitOrderAsync\b"),
            Rule("ENTER_LONG", SecurityScanCategories.OrderExecution, "Enter long helper", @"\bEnterLong\b"),
            Rule("ENTER_SHORT", SecurityScanCategories.OrderExecution, "Enter short helper", @"\bEnterShort\b"),
            Rule("EXIT_LONG", SecurityScanCategories.OrderExecution, "Exit long helper", @"\bExitLong\b"),
            Rule("EXIT_SHORT", SecurityScanCategories.OrderExecution, "Exit short helper", @"\bExitShort\b"),
            Rule("SET_STOP_LOSS", SecurityScanCategories.OrderExecution, "Set stop loss helper", @"\bSetStopLoss\b"),
            Rule("SET_PROFIT_TARGET", SecurityScanCategories.OrderExecution, "Set profit target helper", @"\bSetProfitTarget\b"),
            Rule("SET_TRAIL_STOP", SecurityScanCategories.OrderExecution, "Set trail stop helper", @"\bSetTrailStop\b"),
            Rule("PLACE_ORDER", SecurityScanCategories.OrderExecution, "Place order API", @"\bPlaceOrder\b"),
            Rule("CANCEL_ORDER", SecurityScanCategories.OrderExecution, "Cancel order API", @"\bCancelOrder\b"),
            Rule("CHANGE_ORDER", SecurityScanCategories.OrderExecution, "Change order API", @"\bChangeOrder\b"),
            Rule("AMEND_ORDER", SecurityScanCategories.OrderExecution, "Amend order API", @"\bAmendOrder\b"),
            Rule("ORDER_ACTION", SecurityScanCategories.OrderExecution, "Order action enum/type", @"\bOrderAction\b"),
            Rule("IORDER", SecurityScanCategories.OrderExecution, "IOrder interface", @"\bIOrder\b"),
            Rule("ORDER_CREATE_ORDER", SecurityScanCategories.OrderExecution, "Order.CreateOrder", @"\bOrder\s*\.\s*CreateOrder\b"),
            Rule("ACCOUNT_FLATTEN", SecurityScanCategories.AccountMutation, "Account.Flatten", @"\baccount\s*\.\s*Flatten\b", true),
            Rule("ACCOUNT_FLATTEN_EVERYTHING", SecurityScanCategories.AccountMutation, "Account.FlattenEverything", @"\baccount\s*\.\s*FlattenEverything\b", true),
            Rule("ACCOUNT_CREATE_ORDER", SecurityScanCategories.AccountMutation, "Account.CreateOrder", @"\baccount\s*\.\s*CreateOrder\b", true),
            Rule("ACCOUNT_CANCEL", SecurityScanCategories.AccountMutation, "Account.Cancel", @"\baccount\s*\.\s*Cancel\b", true),
            Rule("ACCOUNT_CHANGE", SecurityScanCategories.AccountMutation, "Account.Change", @"\baccount\s*\.\s*Change\b", true),
            Rule("ACCOUNT_EXECUTIONS", SecurityScanCategories.AccountMutation, "Account.Executions (out of scope)", @"\baccount\s*\.\s*Executions\b", true),
            Rule("CONNECTION_CONNECT", SecurityScanCategories.AccountMutation, "Connection.Connect(", @"\bConnection\s*\.\s*Connect\s*\("),
            Rule("CONNECTION_DISCONNECT", SecurityScanCategories.AccountMutation, "Connection.Disconnect(", @"\bConnection\s*\.\s*Disconnect\s*\("),
            Rule("IS_LIVE_TRADING_ENABLED", SecurityScanCategories.LiveTradingEnable, "IsLiveTradingEnabled", @"\bIsLiveTradingEnabled\b"),
            Rule("ENABLE_LIVE_TRADING", SecurityScanCategories.LiveTradingEnable, "EnableLiveTrading", @"\bEnableLiveTrading\b"),
            Rule("SYSTEM_REFLECTION", SecurityScanCategories.Reflection, "System.Reflection", @"\bSystem\.Reflection\b"),
            Rule("REFLECTION_GET_PROPERTY", SecurityScanCategories.Reflection, "reflection GetProperty", @"\bGetType\s*\(\s*\)\s*\.\s*GetProperty"),
            Rule("REFLECTION_GET_METHOD", SecurityScanCategories.Reflection, "reflection GetMethod", @"\bGetType\s*\(\s*\)\s*\.\s*GetMethod"),
            Rule("ACTIVATOR_CREATE_INSTANCE", SecurityScanCategories.Reflection, "Activator.CreateInstance", @"\bActivator\s*\.\s*CreateInstance"),
            Rule("UNSAFE_CODE", SecurityScanCategories.UnsafeInterop, "unsafe code", @"\bunsafe\b"),
            Rule("STACKALLOC", SecurityScanCategories.UnsafeInterop, "stackalloc", @"\bstackalloc\b"),
            Rule("DLL_IMPORT", SecurityScanCategories.UnsafeInterop, "DllImport / native interop", @"\bDllImport\b")
        };

        /// <summary>Number of forbidden-pattern rules. Reported as the catalogue breadth.</summary>
        public static int RuleCount { get { return Rules.Count; } }

        private static SecurityScanRule Rule(string id, string category, string description, string pattern)
        {
            return Rule(id, category, description, pattern, false);
        }

        private static SecurityScanRule Rule(string id, string category, string description, string pattern, bool caseInsensitive)
        {
            return new SecurityScanRule
            {
                Id = id,
                Category = category,
                Description = description,
                Pattern = pattern,
                CaseInsensitive = caseInsensitive
            };
        }
    }
}
