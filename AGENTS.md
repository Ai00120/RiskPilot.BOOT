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
- `src/RiskPilot.Diagnostics.Nt8/Core/Run/` — modes, run coordinator, view state (host-free).
- `src/RiskPilot.Diagnostics.Nt8/Core/Evidence/` — 17-artifact evidence package builder.
- `src/RiskPilot.Diagnostics.Nt8/Host/` — the **only** layer that touches NinjaTrader types.
- `src/RiskPilot.Diagnostics.Nt8/Stubs/` — offline compile-time stubs (`#if USE_STUBS` only).
- `tests/RiskPilot.Diagnostics.Nt8.Tests/` — xUnit tests.
- `verification/addon-smoke/` — static safety scan (no NT8 needed).
- `docs/` — Phase 1–4 records.

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
- `Dispatcher` exposes both `HasShutdownStarted` and `HasShutdownFinished`; check both (the
  stub file declares the latter too).

## Phase 4 notes (UI + evidence)

- The WPF UI files (`DiagnosticCenterWindow`, `MenuIntegration`, `DiagnosticCenterWindowManager`,
  `DiagnosticAddOn.Ui.cs`) are in the `UseStubs != true` ItemGroup — they are **not compiled** in
  the offline stub build. All UI-adjacent logic that must be testable lives in `Core/Run`.
- `DiagnosticRunCoordinator` owns the single-run guarantee; `Start` returns false when a run is
  active. It marshals notifications through `IUiDispatcher` (async post only).
- The AddOn wires `ProgressChanged`/`RunCompleted` to `DiagnosticViewState` in
  `SafeInitialize` (`WireViewState`); the window renders from that state. `SwitchableUiDispatcher`
  runs notifications inline until a window attaches (then `InvokeAsync`), so a run before the
  window opens is still reflected.
- Cancel is polled by the engine between modules via `Func<bool> shouldStop`; a cancelled run
  never writes an evidence package.
- `EvidencePackageBuilder.Build` is pure (no host/FS/clock). `VerifyIntegrity` checks all 17
  artifacts + SHA-256 before `WriteEvidencePackage` writes anything.
- In the manifest, `MaskedRef` is **always** a synthetic `account#<hash>` token, never a raw ref.
- `EvidencePackage.IsSecretFree` skips `manifest.json` (dates/hashes trip the digit-run rule);
  use `ManifestIsIdentitySafe` for the manifest.
- M9's `PostTradeAudit` observation is built from BOTH the ingest collector AND the host's
  already-read `Account.Orders` snapshot (`Snapshot.OrderLifecycle`), deduped by order ref, so
  the audit has a real surface even though the tool subscribes to no host event feed.
- The audit observation must reach BOTH paths: the live probe (via the `EnrichWithPostTradeAudit`
  enricher on `EnrichedDataProvider`) and the exported package (`BuildEvidenceDataset`). Setting
  it only in `BuildEvidenceDataset` leaves the live M9 probe UNOBSERVABLE.
- `PostTradeAuditObservation.ExecutionsInScope` must be derived, never hardcoded true: the host
  reads only `Account.Orders` (`AccountRecord.ExecutionsInScope=false`), so claiming execution
  coverage would let `M9.CORRELATION_TIMING` PASS without an observed execution (fail-closed).
- Never hardcode an observation in an artifact: `market-data-summary.sourceTimeBasis` is derived
  from the observed records (weakest basis) and is `UNOBSERVABLE` with no records.

## Reference repo

`Ai00120/RiskPilot.BOT` is **read-only reference** for study. Never modify it. Its stubs live
at `src/RiskPilot.BOT/Stubs/NinjaTraderStubs.cs`; its two-build-mode csproj is the pattern
mirrored here.
