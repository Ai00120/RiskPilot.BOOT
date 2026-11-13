#!/usr/bin/env python3
"""
Stage the NinjaTrader 8 NinjaScript Editor source bundle for RiskPilot.Diagnostics.Nt8.

WHY THIS EXISTS
---------------
NinjaTrader 8 compiles EVERY .cs file under
`Documents\\NinjaTrader 8\\bin\\Custom` (including `AddOns\\` recursively) into ONE
`NinjaTrader.Custom.dll`. NinjaTrader already supplies its own
`bin\\Custom\\AssemblyInfo.cs` carrying AssemblyTitle, AssemblyDescription,
AssemblyCompany, AssemblyProduct, AssemblyCopyright, ComVisible and AssemblyVersion.

The standalone BOOT project legitimately keeps `Properties/AssemblyInfo.cs` for its
OWN DLL build (`GenerateAssemblyInfo=false` in the .csproj). That file is correct for
`dotnet build`, but it must NEVER be copied into the NinjaScript Custom tree: NT8 would
compile two sets of assembly-level attributes and fail with CS0579. SDK/Visual Studio
builds performed inside `bin\\Custom` also leave `obj/**/*.AssemblyInfo.cs` and
`bin/` folders behind, which NT8 would compile as well.

This tool stages ONLY `Core/**/*.cs` and `Host/**/*.cs` that are explicitly listed as
`<Compile Include=...>` in the project file. It fails closed on any `AssemblyInfo.cs`,
any `*.AssemblyAttributes.cs`, any `[assembly: ...]` attribute, any `bin/`/`obj/`
directory and any stale/non-source build artifact in the destination. It never touches
the trading logic and never modifies the repository.

Exit 0 = PASS (source bundle staged), 1 = FAIL CLOSED (nothing staged / conflicting tree).
"""
from __future__ import annotations

import argparse
import re
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = REPO / "src" / "RiskPilot.Diagnostics.Nt8"
PROJECT_FILE = "RiskPilot.Diagnostics.Nt8.csproj"
STAGED_FOLDERS = ("Core", "Host")
EXCLUDED_DIR_NAMES = {"bin", "obj", "properties", "stubs"}
FORBIDDEN_ARTIFACT_SUFFIXES = {".csproj", ".sln", ".dll", ".pdb", ".cache"}
ASSEMBLY_ATTRIBUTE = re.compile(r"\[\s*assembly\s*:", re.IGNORECASE)
COMPILE_INCLUDE = re.compile(r'<Compile\s+Include="([^"]+\.cs)"\s*/>', re.IGNORECASE)


class PackagingError(Exception):
    """A fail-closed condition: the NT8 Custom source tree must not be staged."""


def _rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def _is_assembly_info(path: Path) -> bool:
    name = path.name.lower()
    return "assemblyinfo" in name or "assemblyattributes" in name


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def _compile_includes(source: Path) -> set[str]:
    project = source / PROJECT_FILE
    if not project.is_file():
        raise PackagingError(f"missing project file: {project}")
    includes = set()
    for match in COMPILE_INCLUDE.finditer(_read(project)):
        includes.add(match.group(1).replace("\\", "/"))
    if not includes:
        raise PackagingError(f"no <Compile Include=...> entries found in {project}")
    return includes


def _collect_source_files(source: Path) -> list[tuple[str, Path]]:
    if not source.is_dir():
        raise PackagingError(f"source directory does not exist: {source}")
    includes = _compile_includes(source)
    staged: list[tuple[str, Path]] = []
    for folder in STAGED_FOLDERS:
        root = source / folder
        if not root.is_dir():
            raise PackagingError(f"required source folder is missing: {root}")
        for path in sorted(root.rglob("*.cs")):
            parts = {part.lower() for part in path.relative_to(source).parts}
            if parts & EXCLUDED_DIR_NAMES:
                raise PackagingError(f"refusing excluded source path: {_rel(path, source)}")
            rel = _rel(path, source)
            if rel not in includes:
                raise PackagingError(
                    f"{rel} is under Core/Host but is not a <Compile Include=...> entry in "
                    f"{PROJECT_FILE}; refusing to stage an untracked source file"
                )
            if _is_assembly_info(path):
                raise PackagingError(
                    f"{rel} is an assembly-info file; NT8 already supplies its own and would "
                    "report CS0579 duplicate attributes"
                )
            if ASSEMBLY_ATTRIBUTE.search(_read(path)):
                raise PackagingError(
                    f"{rel} declares an [assembly: ...] attribute; it must not be compiled "
                    "into NinjaTrader.Custom.dll"
                )
            staged.append((rel, path))
    if not staged:
        raise PackagingError("no Core/Host .cs files were selected for staging")
    return staged


def _check_destination(output: Path, expected_rels: set[str]) -> None:
    if output.name.lower() in {"custom", "addons"}:
        raise PackagingError(
            "refusing to stage directly into bin\\Custom or bin\\Custom\\AddOns; use a dedicated "
            "subfolder such as bin\\Custom\\AddOns\\RiskPilot.Diagnostics.Nt8"
        )

    if not output.exists():
        return
    if not output.is_dir():
        raise PackagingError(f"destination exists but is not a directory: {output}")

    for path in sorted(output.rglob("*")):
        rel = _rel(path, output)
        if path.is_dir() and path.name.lower() in {"bin", "obj"}:
            raise PackagingError(
                f"generated build directory '{rel}' found in the destination; delete it before "
                "staging (Visual Studio/dotnet builds inside bin\\Custom create duplicate "
                "AssemblyInfo.cs files under obj/)"
            )
        if not path.is_file():
            continue
        if path.suffix.lower() in FORBIDDEN_ARTIFACT_SUFFIXES:
            raise PackagingError(
                f"build artifact '{rel}' found in the destination; only Core/ and Host/ .cs "
                "sources may live in the staged AddOns source folder"
            )
        if path.suffix.lower() != ".cs":
            continue  # e.g. a local README/NOTES.txt is harmless (NT8 compiles .cs only)
        if _is_assembly_info(path):
            raise PackagingError(
                f"'{rel}' is an assembly-info file in the destination; remove it. NT8 supplies "
                "bin\\Custom\\AssemblyInfo.cs itself and compiling a second copy raises CS0579"
            )
        if ASSEMBLY_ATTRIBUTE.search(_read(path)):
            raise PackagingError(
                f"'{rel}' declares an [assembly: ...] attribute in the destination; remove it"
            )
        if rel not in expected_rels:
            raise PackagingError(
                f"stale .cs file '{rel}' is not part of the current source bundle; delete the "
                "staging folder (or its old Core/Host trees) before re-running the packager"
            )


def _stage(source: Path, output: Path) -> list[str]:
    source, output = source.resolve(), output.resolve()
    if output == source or source in output.parents or output in source.parents:
        raise PackagingError("source and destination must not overlap")
    if output == REPO or REPO in output.parents:
        raise PackagingError("stage outside the repository, never inside the checkout")
    if output.exists() and any(p.is_symlink() for p in output.rglob("*")):
        raise PackagingError("destination must not contain symbolic links")
    staged = _collect_source_files(source)
    expected_rels = {rel for rel, _ in staged}

    # Fail closed BEFORE writing anything, including when re-staging over an older bundle.
    _check_destination(output, expected_rels)

    if output.exists():
        for folder in STAGED_FOLDERS:
            managed = output / folder
            if managed.exists():
                if not managed.is_dir():
                    raise PackagingError(f"expected a directory at {managed}")
                shutil.rmtree(managed)
    output.mkdir(parents=True, exist_ok=True)

    for rel, src in staged:
        dst = output / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)

    # Post-condition: the staged tree is exactly the Core/Host .cs bundle, nothing else compiled.
    for path in output.rglob("*"):
        rel = _rel(path, output)
        if path.is_dir() and path.name.lower() in {"bin", "obj"}:
            raise PackagingError(f"generated build directory appeared after staging: {rel}")
        if not path.is_file():
            continue
        if path.suffix.lower() in FORBIDDEN_ARTIFACT_SUFFIXES:
            raise PackagingError(f"build artifact appeared after staging: {rel}")
        if path.suffix.lower() == ".cs":
            if rel not in expected_rels or _is_assembly_info(path):
                raise PackagingError(f"unexpected .cs file after staging: {rel}")
            if ASSEMBLY_ATTRIBUTE.search(_read(path)):
                raise PackagingError(f"assembly attribute appeared after staging: {rel}")

    return sorted(expected_rels)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Stage only the Core/Host .cs sources for the NinjaScript Editor. "
                    "Never stages Properties/AssemblyInfo.cs, Stubs/, bin/, obj/ or .csproj."
    )
    parser.add_argument(
        "--source", type=Path, default=DEFAULT_SOURCE,
        help=f"AddOn source root (default: {DEFAULT_SOURCE})",
    )
    parser.add_argument(
        "--output", type=Path, required=True,
        help="Dedicated destination folder, e.g. "
             r"%USERPROFILE%\\Documents\\NinjaTrader 8\\bin\\Custom\\AddOns\\RiskPilot.Diagnostics.Nt8",
    )
    args = parser.parse_args()

    try:
        rels = _stage(args.source.resolve(), args.output.resolve())
    except PackagingError as exc:
        print("NT8 SOURCE PACKAGE: FAIL CLOSED")
        print(f"  - {exc}")
        print("  No conflicting source was staged. See docs/INSTALL.md (CS0579 troubleshooting).")
        return 1

    print("NT8 SOURCE PACKAGE: PASS")
    print(f"  staged {len(rels)} .cs files into {args.output.resolve()}")
    print("  Core/ only: pure domain; Host/ only: the NT8 adapter layer.")
    print("  Excluded: Properties/AssemblyInfo.cs, Stubs/, *.csproj, bin/, obj/ and generated "
          "assembly attributes.")
    print("  Next: compile inside NinjaTrader (New > NinjaScript Editor > AddOns > Compile).")
    print("  Disclaimer: this is a source-staging check, not a real NT8 host compile.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
