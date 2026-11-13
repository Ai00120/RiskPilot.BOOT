"""
RiskPilot NT8 Diagnostic Center — static safety smoke test.

Runs without NinjaTrader, Rithmic, or any trading account. It verifies the
safety invariants that must hold across ALL production source files:

  - No order submission / modification / cancellation API appears in production source.
  - No account / connection / position mutation API appears.
  - No reflection or unsafe / unmanaged code.
  - No live-trading enable path.
  - The AddOn declares Trading=Disabled.

DISCLAIMER: passing this check confirms offline static invariants only. It makes
NO claim about real NinjaTrader 8 host compatibility or runtime behaviour.

Exit 0 = PASS, 1 = FAIL.
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SRC_DIR = REPO / "src" / "RiskPilot.Diagnostics.Nt8"

# Only these files may reference the NinjaTrader account/connection surface at all.
HOST_LAYER = "src/RiskPilot.Diagnostics.Nt8/Host/"

FORBIDDEN_PATTERNS = [
    (r"\bSubmitOrder\b", "SubmitOrder"),
    (r"\bSubmitOrderUnmanaged\b", "SubmitOrderUnmanaged"),
    (r"\bSubmitOrderAsync\b", "SubmitOrderAsync"),
    (r"\bEnterLong\b", "EnterLong"),
    (r"\bEnterShort\b", "EnterShort"),
    (r"\bExitLong\b", "ExitShort/ExitLong"),
    (r"\bExitShort\b", "ExitShort"),
    (r"\bSetStopLoss\b", "SetStopLoss"),
    (r"\bSetProfitTarget\b", "SetProfitTarget"),
    (r"\bSetTrailStop\b", "SetTrailStop"),
    (r"\bPlaceOrder\b", "PlaceOrder"),
    (r"\bCancelOrder\b", "CancelOrder"),
    (r"\bChangeOrder\b", "ChangeOrder"),
    (r"\bAmendOrder\b", "AmendOrder"),
    (r"\bOrderAction\b", "OrderAction"),
    (r"\bIOrder\b", "IOrder"),
    (r"\bOrder\s*\.\s*CreateOrder\b", "Order.CreateOrder"),
    (r"(?i)\baccount\s*\.\s*Flatten\b", "Account.Flatten"),
    (r"(?i)\baccount\s*\.\s*FlattenEverything\b", "Account.FlattenEverything"),
    (r"(?i)\baccount\s*\.\s*CreateOrder\b", "Account.CreateOrder"),
    (r"(?i)\baccount\s*\.\s*Cancel\b", "Account.Cancel"),
    (r"(?i)\baccount\s*\.\s*Change\b", "Account.Change"),
    (r"(?i)\baccount\s*\.\s*Executions\b", "Account.Executions (out of scope)"),
    (r"\bConnection\s*\.\s*Connect\s*\(", "Connection.Connect("),
    (r"\bConnection\s*\.\s*Disconnect\s*\(", "Connection.Disconnect("),
    (r"\bIsLiveTradingEnabled\b", "IsLiveTradingEnabled"),
    (r"\bEnableLiveTrading\b", "EnableLiveTrading"),
    (r"\bSystem\.Reflection\b", "System.Reflection"),
    (r"\bGetType\s*\(\s*\)\s*\.\s*GetProperty", "reflection GetProperty"),
    (r"\bGetType\s*\(\s*\)\s*\.\s*GetMethod", "reflection GetMethod"),
    (r"\bActivator\s*\.\s*CreateInstance", "Activator.CreateInstance"),
    (r"\bunsafe\b", "unsafe code"),
    (r"\bstackalloc\b", "stackalloc"),
    (r"\bDllImport\b", "DllImport / native interop"),
]

# The stubs are offline-only and must never be part of the real host build; they are
# excluded from this scan because they legitimately declare host *type* names.
# obj/ and bin/ contain generated code and are excluded as well. AssemblyInfo.cs is
# allowed to reference System.Reflection for assembly-level attributes only.
ALLOWED_REFLECTION_FILES = {
    "src/RiskPilot.Diagnostics.Nt8/Properties/AssemblyInfo.cs",
}

# The exact Owner-specified account/connection identifiers (RiskPilot.BOT D-096) are
# Owner-provided configuration constants. They may be defined in exactly one reviewed core
# file (the identity definition) and must not be emitted to diagnostics, logs or reports.
# This check fails if a literal appears anywhere else in production source, which is how an
# accidental hardcoded identity leak would first appear. It catches a hardcoded literal only;
# it does not prove that a runtime value (for example a host-reported connection name) is never
# emitted -- that is enforced by the artifact/finding tests.
IDENTITY_LITERALS = ["SimRP.BOT", "My Rithmic"]
IDENTITY_DEFINITION_FILE = "src/RiskPilot.Diagnostics.Nt8/Core/Identity/AccountIdentityVerifier.cs"

# The declared NinjaTrader host versions (minimum supported / target) are published evidence and
# must live in exactly one reviewed file. A version literal (for example "8.1.9.0") appearing
# anywhere else in production source is version drift: a document, probe or report could then
# disagree with the single source of truth. This is a source-confined literal only; the declared
# constants remain readable at runtime.
VERSION_DEFINITION_FILE = "src/RiskPilot.Diagnostics.Nt8/Core/Identity/NinjaTraderCompatibility.cs"
VERSION_LITERAL = re.compile(r'"8\.1\.\d[^"]*"')

# The .csproj header restates the target/minimum for a reader who never opens the C# source. The
# guard below fails if it drifts from the single source of truth, so the two cannot disagree.
CSPROJ_FILE = "src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj"
TARGET_VERSION_DECL = re.compile(r'TargetVersion\s*=\s*"([0-9][0-9.]*)"')
MINIMUM_VERSION_DECL = re.compile(r'MinimumSupportedVersion\s*=\s*"([0-9][0-9.]*)"')


def iter_source_files():
    for path in SRC_DIR.rglob("*.cs"):
        rel = path.relative_to(REPO).as_posix()
        if "/Stubs/" in rel or "/obj/" in rel or "/bin/" in rel:
            continue
        yield path, rel


def strip_comments_and_strings(text):
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    text = re.sub(r"//[^\n]*", " ", text)
    text = re.sub(r'@"(?:[^"]|"")*"', '""', text, flags=re.DOTALL)
    text = re.sub(r'"(?:\\.|[^"\\])*"', '""', text)
    return text


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    text = re.sub(r"//[^\n]*", " ", text)
    return text


def main():
    failures = []
    files = list(iter_source_files())
    if not files:
        print("FAIL: no source files found under", SRC_DIR)
        return 1

    for path, rel in files:
        raw = path.read_text(encoding="utf-8")
        stripped = strip_comments_and_strings(raw)
        for pattern, label in FORBIDDEN_PATTERNS:
            if rel in ALLOWED_REFLECTION_FILES and label.startswith("System.Reflection"):
                continue
            if re.search(pattern, stripped):
                failures.append(f"{rel}: forbidden pattern '{label}' ({pattern})")

    # Positive invariants: the AddOn must declare Trading=Disabled.
    addon = (SRC_DIR / "Host" / "DiagnosticAddOn.cs").read_text(encoding="utf-8")
    if "TradingDisabled" not in addon:
        failures.append("DiagnosticAddOn.cs: TradingDisabled declaration missing")

    # The Owner-specified identity literals must exist (the verifier compares against them) and
    # must not appear anywhere else in production source, so they can never be emitted to a
    # diagnostic, log or report by accident.
    definition = REPO / IDENTITY_DEFINITION_FILE
    if not definition.exists():
        failures.append(f"{IDENTITY_DEFINITION_FILE}: identity definition file is missing")
    else:
        for path, rel in files:
            if rel == IDENTITY_DEFINITION_FILE:
                continue
            text = path.read_text(encoding="utf-8")
            for literal in IDENTITY_LITERALS:
                if literal in text:
                    failures.append(f"{rel}: owner identity literal '{literal}' must not appear outside {IDENTITY_DEFINITION_FILE}")

    # The declared NinjaTrader host versions must be confined to the single reviewed definition
    # file, so no probe/report can hardcode a drifting version. A version literal elsewhere in
    # production source (outside comments and other string literals) is version drift.
    version_definition = REPO / VERSION_DEFINITION_FILE
    if not version_definition.exists():
        failures.append(f"{VERSION_DEFINITION_FILE}: version definition file is missing")
    else:
        for path, rel in files:
            if rel == VERSION_DEFINITION_FILE:
                continue
            body = strip_comments(path.read_text(encoding="utf-8"))
            if VERSION_LITERAL.search(body):
                failures.append(
                    f"{rel}: NinjaTrader version literal must not appear outside {VERSION_DEFINITION_FILE}"
                )

    # The .csproj header must agree with the declared constants, so the two never disagree.
    if version_definition.exists():
        definition_text = version_definition.read_text(encoding="utf-8")
        target = TARGET_VERSION_DECL.search(definition_text)
        minimum = MINIMUM_VERSION_DECL.search(definition_text)
        csproj = REPO / CSPROJ_FILE
        if target and minimum and csproj.exists():
            csproj_text = csproj.read_text(encoding="utf-8")
            if f"NinjaTrader {target.group(1)}" not in csproj_text:
                failures.append(f"{CSPROJ_FILE}: target version {target.group(1)} missing/drifted in header")
            if f"minimum supported {minimum.group(1)}" not in csproj_text:
                failures.append(f"{CSPROJ_FILE}: minimum supported version {minimum.group(1)} missing/drifted in header")

    if failures:
        print("STATIC SAFETY CHECK: FAIL")
        for f in failures:
            print("  -", f)
        return 1

    print("STATIC SAFETY CHECK: PASS")
    print(f"  scanned {len(files)} production source files")
    print(f"  forbidden patterns checked: {len(FORBIDDEN_PATTERNS)}")
    print("  disclaimer: offline static invariants only; not a real NT8 host verification")
    return 0


if __name__ == "__main__":
    sys.exit(main())
