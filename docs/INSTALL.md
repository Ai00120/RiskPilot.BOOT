# Installation Guide — RiskPilot NT8 Diagnostic Center

This guide installs the **standalone, read-only** NinjaTrader 8 AddOn
`RiskPilot.Diagnostics.Nt8` (product name: *RiskPilot NT8 Diagnostic Center*). It has no execution
dependency on `Ai00120/RiskPilot.BOT`.

> **Read this first.** The repository ships a build that has been verified only against offline
> **stubs** on Linux CI. A real NinjaTrader-host build has **not** been verified in this environment
> because the NinjaTrader 8 assemblies are not available here. Treat a stub build as a
> syntax/contract check only. Verify the real-host build on a Windows machine with NinjaTrader 8
> installed before you install the AddOn.

## 1. Requirements

| Item | Value |
| --- | --- |
| NinjaTrader | 8.1.8.4 (64-bit) or a compatible 8.x |
| Target framework (real build) | .NET Framework 4.8 (`net48`) |
| Language level | C# 8.0 |
| NinjaTrader install path (default) | `C:\Program Files\NinjaTrader 8\bin\` |
| NinjaScript custom folder | `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\` |

The AddOn is read-only. It never sends, modifies or cancels an order, never mutates an account,
connection or position, and never changes a RiskPilot setting. Trading stays disabled.

## 2. Build against the real host (Windows)

The real build references these NinjaTrader assemblies from
`C:\Program Files\NinjaTrader 8\bin\`:

- `NinjaTrader.Core.dll`
- `NinjaTrader.Cbi.dll`
- `NinjaTrader.Gui.dll`
- `NinjaTrader.Data.dll`
- `NinjaTrader.NinjaScript.dll`

(plus the WPF assemblies the GUI project already references). If NinjaTrader is installed
elsewhere, adjust the `HintPath` entries in
`src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj` for the `UseStubs != true`
configuration.

```bat
dotnet build src\RiskPilot.Diagnostics.Nt8\RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=false -c Release
```

The output is `src\RiskPilot.Diagnostics.Nt8\bin\Release\net48\RiskPilot.Diagnostics.Nt8.dll`.

### Alternative: compile inside NinjaTrader

The canonical NT8 workflow is to copy the sources into the NinjaScript custom folder and let the
NinjaScript Editor compile them:

1. Copy the `src\RiskPilot.Diagnostics.Nt8\Core\`, `Host\` and `Properties\` folders (the `Stubs\`
   folder is compiled only under `USE_STUBS` and is not needed) into
   `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\RiskPilot.Diagnostics.Nt8\`.
2. Open NinjaTrader → **New → NinjaScript Editor** → right-click **AddOns** → **Compile**.
3. Fix nothing unless the editor reports a genuine API mismatch; a clean compile registers the
   AddOn.

## 3. Install the compiled AddOn

1. Close NinjaTrader.
2. Copy `RiskPilot.Diagnostics.Nt8.dll` into
   `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\`.
3. If you used the NinjaScript Editor compile in step 2 of the previous section, the AddOn is
   already registered and you can skip the copy.
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

Each run gets its own directory containing the 17-artifact package (see `docs/USAGE.md`). The
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
```

This mode targets `net8.0` and compiles `Stubs/NinjaTraderStubs.cs` under `#if USE_STUBS`. A green
offline build is **not** proof of real host compatibility.
