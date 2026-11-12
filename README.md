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
- Product code is intentionally **not** started in Phase 1.

## Target environment

NinjaTrader 8.1.8.4 (64-bit) / .NET Framework 4.8 / C# 8.0, CME ES futures via an authorized
Rithmic connection.
