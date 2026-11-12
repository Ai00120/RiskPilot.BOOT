# RiskPilot.BOOT

Independent development repository for the **RiskPilot NT8 Diagnostic & Acceptance AddOn**
("RiskPilot NT8 Diagnostic Center", namespace `RiskPilot.Diagnostics.Nt8`).

This repository hosts a **standalone, read-only NinjaTrader 8 AddOn** that performs diagnostics and
host-acceptance checks. It has **no execution dependency** on the trading logic of the reference
repository [`Ai00120/RiskPilot.BOT`](https://github.com/Ai00120/RiskPilot.BOT).

## Safety boundaries

- Read-only: no order, execution, position, or account mutation.
- Only documented public NinjaTrader 8 APIs; no reflection, private access, unsafe memory, or guessed APIs.
- `Trading=Disabled` is preserved; the tool never enables trading or changes RiskPilot settings.
- Missing data is never reported as `PASS`. Result states are only
  `PASS / FAIL / INCONCLUSIVE / NOT_APPLICABLE`.

## Current status

- **Phase 1 complete** — repository audit and architecture.
  See [`docs/PHASE1_ARCHITECTURE_AND_AUDIT.md`](docs/PHASE1_ARCHITECTURE_AND_AUDIT.md).
- **Phase 2 complete** — Foundation & Diagnostic Core: standalone solution/project, normalized
  data model, bounded event queue, session/logging/manifest, M1/M2 probes, and unit tests.
  See [`docs/PHASE2_IMPLEMENTATION.md`](docs/PHASE2_IMPLEMENTATION.md).

> **Real NinjaTrader host compatibility is NOT yet verified.** The offline build compiles against
> compile-time stubs for CI validation. A green offline build is a syntax/contract check only, not
> proof that the AddOn builds or runs against a real NinjaTrader 8.1.8.4 host.

## Repository layout

```
src/RiskPilot.Diagnostics.Nt8/          The AddOn (shippable product)
├── Core/                                Pure domain — no NinjaTrader types
│   ├── Clock/ Identity/ Status/ Security/
│   ├── Models/                          Normalized record types
│   ├── Result/ Session/ Queue/ Logging/ Errors/
│   ├── Report/                          JSON writer, SHA-256 manifest, serializer
│   ├── Engine/                          DiagnosticEngine + probes M1/M2
│   └── Contract/                        IReadOnlyDiagnosticSource (optional)
├── Host/                                The ONLY layer that touches NinjaTrader types
├── Stubs/                               Offline compile-time stubs (USE_STUBS only)
└── Properties/AssemblyInfo.cs
tests/RiskPilot.Diagnostics.Nt8.Tests/   xUnit unit tests
verification/addon-smoke/                 Static safety scan (no NT8 required)
docs/                                     Phase 1 audit + Phase 2 implementation
```

## Build

### Offline / CI build (no NinjaTrader required)

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # only if the runner lacks ICU

dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
dotnet test  tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
python3 verification/addon-smoke/test_diagnostic_safety.py
```

With `UseStubs=true` (the default) the project targets `net8.0` and compiles
`Stubs/NinjaTraderStubs.cs` under `#if USE_STUBS`. This mode is for offline validation only.

### Real host build (NinjaTrader installed)

Requires Windows with NinjaTrader 8.1.8.4 (64-bit) and the .NET Framework 4.8 targeting pack.

```bat
dotnet build src\RiskPilot.Diagnostics.Nt8\RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=false
```

With `UseStubs=false` the project targets `net48` and references the real assemblies from
`C:\Program Files\NinjaTrader 8\bin\`. Adjust the `HintPath` entries if NinjaTrader is installed
elsewhere. The canonical NT8 workflow is to place the `.cs` files under
`Documents\NinjaTrader 8\bin\Custom\AddOns\` and compile with the NinjaScript Editor.

## Installation (NinjaTrader)

1. Compile the AddOn against the real host (see build mode 2).
2. Copy the produced `RiskPilot.Diagnostics.Nt8.dll` (or the `.cs` sources) into
   `Documents\NinjaTrader 8\bin\Custom\AddOns\`.
3. Compile once inside NinjaTrader (NinjaScript Editor → Compile) to register the AddOn.
4. Restart NinjaTrader. The AddOn activates on the next start (`SetDefaults` → `Active`).

Report artifacts are written to a directory **outside** the Git working tree, per repository
governance for output locations.

## Target environment

NinjaTrader 8.1.8.4 (64-bit) / .NET Framework 4.8 / C# 8.0, CME ES futures via an authorized
Rithmic connection.

