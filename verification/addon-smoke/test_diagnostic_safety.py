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
