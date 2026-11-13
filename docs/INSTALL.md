# Installation Guide — RiskPilot NT8 Diagnostic Center

This guide installs the **standalone, read-only** NinjaTrader 8 AddOn
`RiskPilot.Diagnostics.Nt8` (product name: *RiskPilot NT8 Diagnostic Center*). It has no execution
dependency on `Ai00120/RiskPilot.BOT`.

> **Read this first.** The repository ships a build that has been verified only against offline
> **stubs** on Linux CI. A real NinjaTrader-host build has **not** been verified in this environment
> because the NinjaTrader 8 assemblies are not available here. Treat a stub build as a
> syntax/contract check only. Verify the real-host build on a Windows machine with NinjaTrader 8
> installed before you install the AddOn.
>
> The declared **target** host is NinjaTrader **8.1.9.0** with a **minimum supported** version of
> **8.1.8.4**. The **tested** version list is empty — no host build has been verified at runtime.
> Changing this documentation does not change that: the tool cannot observe the installed host
> build through any public API, so it never reports the target as verified.

## 1. Requirements

| Item | Value |
| --- | --- |
| NinjaTrader (target) | 8.1.9.0 (64-bit); minimum supported 8.1.8.4 |
| Target framework (real build) | .NET Framework 4.8 (`net48`) |
| Language level | C# 8.0 |
| NinjaTrader install path (default) | `C:\Program Files\NinjaTrader 8\bin\` |
| NinjaScript custom folder | `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\` |

The AddOn is read-only. It never sends, modifies or cancels an order, never mutates an account,
connection or position, and never changes a RiskPilot setting. Trading stays disabled.

## 2. Build against the real host (Windows)

The real build references the two NinjaTrader assemblies from
`C:\Program Files\NinjaTrader 8\bin\`:

- `NinjaTrader.Core.dll` (contains `NinjaTrader.Cbi`, `NinjaTrader.Data`, and `NinjaTrader.NinjaScript`)
- `NinjaTrader.Gui.dll`

(plus the .NET Framework 4.8 WPF assemblies `PresentationCore`, `PresentationFramework`, `System.Xaml`,
and `WindowsBase` that the project already references). If NinjaTrader is installed elsewhere,
adjust the `HintPath` entries in `src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj`
for the `UseStubs != true` configuration.

```bat
dotnet build src\RiskPilot.Diagnostics.Nt8\RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=false -c Release
```

The output is `src\RiskPilot.Diagnostics.Nt8\bin\Release\net48\RiskPilot.Diagnostics.Nt8.dll`.

### Alternative: compile inside NinjaTrader

The NinjaScript Editor compiles **every** `.cs` file under
`Documents\NinjaTrader 8\bin\Custom` (including `AddOns\` recursively) into a **single**
`NinjaTrader.Custom.dll`. NinjaTrader already supplies its own
`bin\Custom\AssemblyInfo.cs` with `AssemblyTitle`, `AssemblyDescription`, `AssemblyCompany`,
`AssemblyProduct`, `AssemblyCopyright`, `ComVisible` and `AssemblyVersion`.

The standalone BOOT project legitimately keeps `src\RiskPilot.Diagnostics.Nt8\Properties\AssemblyInfo.cs`
for its **own** DLL build (`GenerateAssemblyInfo=false` in the `.csproj` prevents SDK-generated
duplicates there). That file is correct for `dotnet build`, but it must **never** be copied into
the NinjaScript Custom tree: NT8 would compile two sets of assembly-level attributes and fail with
**CS0579**. The same happens if a Visual Studio/`dotnet` build is run inside `bin\Custom` and
leaves `bin\`, `obj\` and generated `*.AssemblyInfo.cs` / `*.AssemblyAttributes.cs` files behind.
Do **not** fix this by deleting the repository's assembly metadata — it is required by every
standalone build target.

Use the deterministic source-bundle packager; it stages only the `Core\` and `Host\` `.cs` files
that the project explicitly compiles, and fails closed on any conflicting file:

```bat
python3 tools\nt8-source-package\package_addon_sources.py --output "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\RiskPilot.Diagnostics.Nt8"
```

1. Run the command above (re-running it is safe; it replaces only its own `Core\` and `Host\` trees).
   The packager **never** copies `Properties\AssemblyInfo.cs`, `Stubs\`, `*.csproj`, `bin\`, `obj\`
   or any file containing an `[assembly: ...]` attribute. It also refuses to stage into a
   destination that already contains such a file.
2. Open NinjaTrader → **New → NinjaScript Editor** → right-click **AddOns** → **Compile**.
3. A clean compile registers the AddOn. If the editor reports errors, capture the complete error
   list (file, code, line and column) before changing source files.

#### CS0579 troubleshooting (duplicate assembly attributes)

If the editor reports `Duplicate 'AssemblyTitle' / 'AssemblyDescription' / 'AssemblyCompany' /
'AssemblyProduct' / 'AssemblyCopyright' / 'ComVisible' / 'AssemblyVersion'`:

1. Close NinjaTrader.
2. Search `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\` for **extra** `AssemblyInfo.cs`,
   `*.AssemblyAttributes.cs`, `Stubs\`, `*.csproj`, `bin\` and `obj\` folders (because the
   NinjaScript Editor compiles **every** `.cs` file recursively under `bin\Custom\`, even inside
   backup subfolders). Remove only the extra BOOT copies (moving any backups **outside**
   `bin\Custom\`), including any stale `AddOns\RiskPilot.Diagnostics.Nt8\Properties\` or
   VS-generated `bin\`/`obj\` folders.
3. **Keep** NinjaTrader's own `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AssemblyInfo.cs`.
4. Re-run the packager into a clean destination, then compile again in the NinjaScript Editor.
   Do not build the BOOT `.csproj` from inside `bin\Custom`.

## 3. Install the compiled AddOn

Use **one** installation path, never both (a DLL plus duplicate source classes in `AddOns\`
breaks compilation):

1. Close NinjaTrader.
2. Copy `RiskPilot.Diagnostics.Nt8.dll` into
   `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\` **or** use the staged source bundle
   from section 2 — not both. Never copy `Properties\AssemblyInfo.cs`, `Stubs\`, `*.csproj`,
   `bin\` or `obj\` there.
3. If you used the NinjaScript Editor compile, the AddOn is already registered and you can skip
   the DLL copy.
4. Start NinjaTrader. The AddOn activates on start.

## 4. Verify the installation

1. Open the NinjaTrader **Control Center**.
2. In the main menu bar, find **RiskPilot NT8 Diagnostic Center** and click it. The diagnostic
   window opens.
3. Run **Quick Diagnostic**. You should see per-module rows for M1, M2, M3, M4, M5 and M8, each with
   a state of `PASS / FAIL / INCONCLUSIVE / NOT_APPLICABLE`.
4. If NinjaTrader is connected to a provider, M2 should show the resolved provider and the
   price/order feed states. With no connection, M2 stays `INCONCLUSIVE` (never `PASS`).

## 5. Where reports are written

Report artifacts are written **outside** the Git working tree:

```
%USERPROFILE%\Documents\RiskPilot.Diagnostics.Evidence\<session-id>\
```

Each run gets its own directory containing the 18-artifact package (see `docs/USAGE.md`). The
directory holds no account identifiers or secrets: account references are replaced with synthetic
`account#<hash>` tokens.

## 6. Uninstall

1. Close NinjaTrader.
2. Delete `RiskPilot.Diagnostics.Nt8.dll` from
   `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\` (and the `RiskPilot.Diagnostics.Nt8`
   source folder if you used the editor compile).
3. Optionally delete `%USERPROFILE%\Documents\RiskPilot.Diagnostics.Evidence\`.
4. Start NinjaTrader.

## 7. Offline verification build (no NinjaTrader required)

For a quick syntax/contract check without NinjaTrader (Linux or Windows):

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # only if the runner lacks ICU

dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true -c Release
dotnet test  tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj -c Release
python3 verification/addon-smoke/test_diagnostic_safety.py
python3 verification/addon-smoke/test_nt8_compilation_regression.py
```

This mode targets `net8.0` and compiles `Stubs/NinjaTraderStubs.cs` under `#if USE_STUBS`. A green
offline build is **not** proof of real host compatibility.
