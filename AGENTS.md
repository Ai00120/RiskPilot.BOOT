# AGENTS.md — RiskPilot.BOOT

Repository-specific knowledge for agents working in this repo. Keep it current.

## What this repo is

Independent development repository for the **RiskPilot NT8 Diagnostic Center** — a
standalone, **read-only** NinjaTrader 8 AddOn (`RiskPilot.Diagnostics.Nt8`). It has **no
execution dependency** on the reference repo `Ai00120/RiskPilot.BOT`.

## Hard safety rules (never violate)

- Read-only. Never call an order-submission/modification/cancellation API.
- Never mutate an account, connection, position or RiskPilot setting.
- No reflection, no `unsafe`, no native interop, no undocumented APIs.
- Trading stays disabled. There is no code path that enables it.
- Missing/unobservable data must never be reported as `PASS` (fail-closed).
- No account identifiers or secrets in any report or log; use `SecretRedactor` / `SyntheticRef`.
- Report artifacts are written **outside** the Git working tree.

These are enforced by `verification/addon-smoke/test_diagnostic_safety.py` — run it after
any source change.

## Layout

- `src/RiskPilot.Diagnostics.Nt8/Core/` — pure domain, **no NinjaTrader types**.
- `src/RiskPilot.Diagnostics.Nt8/Host/` — the **only** layer that touches NinjaTrader types.
- `src/RiskPilot.Diagnostics.Nt8/Stubs/` — offline compile-time stubs (`#if USE_STUBS` only).
- `tests/RiskPilot.Diagnostics.Nt8.Tests/` — xUnit tests.
- `verification/addon-smoke/` — static safety scan (no NT8 needed).
- `docs/` — Phase 1 audit + Phase 2 implementation record.

## Build & test (offline CI)

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # only if the runner lacks ICU

dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
dotnet test  tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
python3 verification/addon-smoke/test_diagnostic_safety.py
```

- `UseStubs=true` (default) → `net8.0` + stubs, for offline validation only.
- `UseStubs=false` → `net48`, references real NT8 assemblies (Windows only).
- **A green stub build is NOT proof of real host compatibility.** Never claim it is.

## Conventions

- Production targets C# 8.0 / .NET Framework 4.8; keep `LangVersion=8.0` compatible.
- `EnableDefaultCompileItems=false`: every source file must be listed in the `.csproj`
  `<Compile Include=...>` items, otherwise it is silently not compiled.
- Tests use real production code; the only doubles are a deterministic clock and an
  in-memory `IDiagnosticDataProvider` that replaces the NinjaTrader host boundary.
- New forbidden-API checks go in the static safety scan.

## Known facts about the real NT8 surface (learned here)

- No `Provider.Rithmic` enum member is guaranteed — resolve provider identity by
  `ConnectOptions.Provider.ToString()` string comparison.
- `ConnectionStatus` has no `Unknown`-style member mismatch; map by closed switch → fail closed.
- Connection exposes separate `PriceStatus` and `Status` (order channel).
- `Instrument.MasterInstrument.*` must be read only after `DataLoaded`/`Realtime`.
- No public API exposes the exact NT8 host version or the provider build — mark unobservable.

## Reference repo

`Ai00120/RiskPilot.BOT` is **read-only reference** for study. Never modify it. Its stubs live
at `src/RiskPilot.BOT/Stubs/NinjaTraderStubs.cs`; its two-build-mode csproj is the pattern
mirrored here.
