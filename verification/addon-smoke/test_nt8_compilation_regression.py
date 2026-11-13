#!/usr/bin/env python3
"""
Regression checks for the two NinjaTrader 8.1.9.0 compilation issues.

ISSUE 1 (CS0579 duplicate assembly attributes)
  The NinjaScript Editor compiles every .cs file under bin\\Custom into one
  NinjaTrader.Custom.dll, which already has bin\\Custom\\AssemblyInfo.cs. The standalone
  project's Properties/AssemblyInfo.cs must stay in the repository and must NEVER be
  staged into that tree. These checks exercise the deterministic source-bundle packager,
  including its fail-closed behaviour on a pre-existing conflicting AssemblyInfo.cs.

ISSUE 2 (CS0117 Currency.Yen)
  The real NinjaTrader.Cbi.Currency enum has no `Yen` member (JPY is `JapaneseYen`).
  These checks verify the offline stub mirrors the real enum and that the identity
  adapter references only `Currency.UsDollar` -- no generic USD/EUR/JPY/GBP table.

DISCLAIMER: this is an offline static/regression check. It does NOT compile against the
real NinjaTrader 8.1.9.0 assemblies and makes no real-host compatibility claim.

Exit 0 = PASS, 1 = FAIL.
"""
from __future__ import annotations

import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SRC = REPO / "src" / "RiskPilot.Diagnostics.Nt8"
PACKAGER = REPO / "tools" / "nt8-source-package" / "package_addon_sources.py"
HOST_PROVIDER = SRC / "Host" / "Nt8HostDataProvider.cs"
STUBS = SRC / "Stubs" / "NinjaTraderStubs.cs"
ASSEMBLY_INFO = SRC / "Properties" / "AssemblyInfo.cs"
CSPROJ = SRC / "RiskPilot.Diagnostics.Nt8.csproj"
ASSEMBLY_ATTRIBUTE = re.compile(r"\[\s*assembly\s*:", re.IGNORECASE)


class CheckError(AssertionError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise CheckError(message)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    return re.sub(r"//[^\n]*", " ", text)


def run_packager(source: Path, output: Path) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, str(PACKAGER), "--source", str(source), "--output", str(output)],
        capture_output=True, text=True, cwd=str(REPO),
    )


def check_source_bundle() -> list[str]:
    with tempfile.TemporaryDirectory(prefix="boot-nt8-bundle-") as tmp:
        output = Path(tmp) / "RiskPilot.Diagnostics.Nt8"
        result = run_packager(SRC, output)
        require(result.returncode == 0, f"packager failed on the clean tree: {result.stdout}{result.stderr}")

        cs_files = sorted(output.rglob("*.cs"))
        require(len(cs_files) >= 100, f"expected the Core/Host source bundle, got {len(cs_files)} .cs files")
        for path in cs_files:
            rel = path.relative_to(output).as_posix()
            require(rel.startswith("Core/") or rel.startswith("Host/"), f"unexpected staged path: {rel}")
            require("assemblyinfo" not in path.name.lower(), f"AssemblyInfo.cs staged: {rel}")
            require(not ASSEMBLY_ATTRIBUTE.search(read(path)), f"assembly attribute staged: {rel}")

        all_entries = list(output.rglob("*"))
        require(not any(p.is_dir() and p.name.lower() in {"bin", "obj"} for p in all_entries),
                "bin/ or obj/ directory staged")
        require(not any(p.is_file() and p.suffix.lower() in {".csproj", ".sln", ".dll", ".pdb", ".cache"}
                        for p in all_entries), "build artifact staged")

        # Re-staging over the same bundle must stay clean (upgrades are deterministic).
        again = run_packager(SRC, output)
        require(again.returncode == 0, f"re-staging failed: {again.stdout}{again.stderr}")

    return [f"clean source bundle staged ({len(cs_files)} Core/Host .cs files, idempotent re-stage)"]


def check_packager_rejects_source_assembly_info() -> list[str]:
    with tempfile.TemporaryDirectory(prefix="boot-nt8-src-reject-") as tmp:
        root = Path(tmp)
        source, output = root / "src", root / "out"
        (source / "Core").mkdir(parents=True)
        (source / "Core" / "Good.cs").write_text("namespace Stub { internal sealed class Good { } }\n",
                                                encoding="utf-8")
        (source / "Core" / "AssemblyInfo.cs").write_text(
            '[assembly: System.Reflection.AssemblyTitle("conflict")]\n', encoding="utf-8")
        (source / "RiskPilot.Diagnostics.Nt8.csproj").write_text(
            '<Project><ItemGroup><Compile Include="Core\\Good.cs" />'
            '<Compile Include="Core\\AssemblyInfo.cs" /></ItemGroup></Project>\n', encoding="utf-8")

        result = run_packager(source, output)
        require(result.returncode == 1, "packager must fail closed on an AssemblyInfo.cs in Core/")
        text = (result.stdout + result.stderr).lower()
        require("assemblyinfo" in text or "assembly-info" in text or "cs0579" in text,
                "failure message must explain the CS0579 assembly-info conflict")
        staged = list(output.rglob("*.cs")) if output.exists() else []
        require(not staged, "packager must not stage any source when it fails closed")

    return ["packager rejects an AssemblyInfo.cs inside the source tree (fail closed)"]


def check_packager_rejects_destination_assembly_info() -> list[str]:
    with tempfile.TemporaryDirectory(prefix="boot-nt8-dst-reject-") as tmp:
        output = Path(tmp) / "RiskPilot.Diagnostics.Nt8"
        first = run_packager(SRC, output)
        require(first.returncode == 0, f"initial staging failed: {first.stdout}{first.stderr}")

        conflict_dir = output / "Properties"
        conflict_dir.mkdir(parents=True, exist_ok=True)
        (conflict_dir / "AssemblyInfo.cs").write_text(
            '[assembly: System.Reflection.AssemblyTitle("stale copy")]\n', encoding="utf-8")

        second = run_packager(SRC, output)
        require(second.returncode == 1,
                "packager must fail closed when the destination already contains AssemblyInfo.cs")
        text = (second.stdout + second.stderr).lower()
        require("assemblyinfo" in text or "assembly-info" in text or "cs0579" in text,
                "failure message must explain the CS0579 assembly-info conflict")

    return ["packager rejects a pre-existing AssemblyInfo.cs in the NinjaScript destination"]


def check_standalone_metadata_preserved() -> list[str]:
    require(ASSEMBLY_INFO.is_file(), "Properties/AssemblyInfo.cs was removed from the repository")
    assembly = read(ASSEMBLY_INFO)
    for attribute in ("AssemblyTitle", "AssemblyDescription", "AssemblyCompany", "AssemblyProduct",
                      "AssemblyCopyright", "ComVisible", "AssemblyVersion", "AssemblyFileVersion", "Guid"):
        require(f"[assembly: {attribute}" in assembly,
                f"standalone assembly metadata lost: {attribute}")

    project = read(CSPROJ)
    require("<GenerateAssemblyInfo>false</GenerateAssemblyInfo>" in project,
            "GenerateAssemblyInfo must stay false for the standalone build")
    require('<Compile Include="Properties\\AssemblyInfo.cs" />' in project,
            "Properties/AssemblyInfo.cs must stay compiled by the standalone project")

    return ["standalone AssemblyInfo.cs + GenerateAssemblyInfo=false preserved"]


def check_real_host_project_references() -> list[str]:
    import xml.etree.ElementTree as ET

    root = ET.fromstring(read(CSPROJ))
    real_refs = set()
    for group in root.findall("ItemGroup"):
        if group.attrib.get("Condition") == "'$(UseStubs)' != 'true'":
            for ref in group.findall("Reference"):
                inc = ref.attrib.get("Include", "").strip()
                if inc:
                    real_refs.add(inc)

    for required in (
        "PresentationCore",
        "PresentationFramework",
        "System.Xaml",
        "WindowsBase",
        "NinjaTrader.Core",
        "NinjaTrader.Gui",
    ):
        require(required in real_refs, f"UseStubs != true must reference {required}")

    for nonexistent in ("NinjaTrader.Cbi", "NinjaTrader.Data", "NinjaTrader.NinjaScript"):
        require(nonexistent not in real_refs,
                f"UseStubs != true must not reference nonexistent NT8 assembly {nonexistent}.dll "
                "(those namespaces live in NinjaTrader.Core.dll)")

    return ["real-host .csproj references include System.Xaml + NinjaTrader.{Core,Gui} only"]


def check_install_documentation() -> list[str]:
    install = read(REPO / "docs" / "INSTALL.md")
    readme = read(REPO / "README.md")
    for label, text in (("docs/INSTALL.md", install), ("README.md", readme)):
        require("package_addon_sources.py" in text, f"{label} must prescribe the source-bundle packager")
        require("AssemblyInfo.cs" in text, f"{label} must state the AssemblyInfo.cs exclusion")
    require("CS0579" in install, "docs/INSTALL.md must document the CS0579 root cause and recovery")
    require("GenerateAssemblyInfo" in install,
            "docs/INSTALL.md must explain why the standalone project keeps its metadata")

    return ["installation docs prescribe the packager and explain the CS0579 exclusion"]


def check_currency_api_and_usd_only_path() -> list[str]:
    stub = strip_comments(read(STUBS))
    enum_match = re.search(r"public\s+enum\s+Currency\s*\{(.*?)\}", stub, re.DOTALL)
    require(enum_match is not None, "stub Currency enum is missing")
    enum_body = enum_match.group(1)
    for member, value in (("BritishPound", 1), ("Euro", 3), ("JapaneseYen", 5), ("UsDollar", 7)):
        require(re.search(rf"\b{member}\s*=\s*{value}\b", enum_body) is not None,
                f"stub Currency.{member} must exist with the real NT8 value {value}")
    require(re.search(r"(?m)^\s*Yen\s*[,=]", enum_body) is None,
            "stub must not declare a non-existent Currency.Yen member (real NT8 uses JapaneseYen)")
    require(re.search(r"(?m)^\s*BritishPounds\s*[,=]", enum_body) is None,
            "stub must not declare a non-existent Currency.BritishPounds member")
    require("Currency.Yen" not in stub, "stub must not reference Currency.Yen")

    host = strip_comments(read(HOST_PROVIDER))
    forbidden = (
        "Currency.Yen", "Currency.Euro", "Currency.BritishPound",
        '"Yen"', '"Euro"', '"BritishPound"', '"JPY"', '"EUR"', '"GBP"',
        "MapCurrencyToIsoCode", "MapDenominationToIsoCode",
    )
    for token in forbidden:
        require(token not in host, f"identity path still contains a generic currency token: {token}")
    require("Currency.UsDollar" in host,
            "identity path must compare against the real NT8 Currency.UsDollar member")
    require("CurrencyForUsdIdentity" in host,
            "identity path must use the single USD-only reduction helper")

    return ["stub mirrors the real NT8 Currency enum; identity path references only UsDollar"]


def main() -> int:
    checks = (
        check_source_bundle,
        check_packager_rejects_source_assembly_info,
        check_packager_rejects_destination_assembly_info,
        check_standalone_metadata_preserved,
        check_real_host_project_references,
        check_install_documentation,
        check_currency_api_and_usd_only_path,
    )
    passed, failures = [], []
    for check in checks:
        try:
            passed.extend(check())
        except Exception as exc:  # noqa: BLE001 - report every failed invariant
            failures.append(f"{check.__name__}: {exc}")

    if failures:
        print("NT8 COMPILATION REGRESSION CHECK: FAIL")
        for failure in failures:
            print("  -", failure)
        print("  disclaimer: offline static checks only; not a real NT8 host verification")
        return 1

    print("NT8 COMPILATION REGRESSION CHECK: PASS")
    for item in passed:
        print("  -", item)
    print("  disclaimer: offline static checks only; not a real NT8 host verification")
    return 0


if __name__ == "__main__":
    sys.exit(main())
