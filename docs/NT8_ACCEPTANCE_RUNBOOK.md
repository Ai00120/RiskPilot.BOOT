# RiskPilot NT8 Diagnostic Center — Real-Host Acceptance Runbook

This runbook is the **BOOT-only** procedure for verifying the AddOn against a real NinjaTrader 8
host. It exercises the read-only diagnostic surface; it **never** asks the operator to submit,
modify or cancel an order, and it never asks to enable trading. `Trading=Disabled` throughout.

> **Status of this document.** Nothing in this runbook has been executed against a real host from
> this repository's build environment (no Windows machine and no NinjaTrader 8 assemblies were
> available). It is the *procedure* to be executed; it is not evidence that it has passed. A real
> run must be recorded and its results attached before any compatibility claim is made.

## 0. Prerequisites

| Item | Value |
| --- | --- |
| NinjaTrader | 8.1.9.0 (target); minimum supported 8.1.8.4 |
| OS | 64-bit Windows with NinjaTrader 8 installed |
| Real build | `dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=false` on Windows, or the NinjaScript Editor compile |
| Target account (the verified identity) | account `SimRP.BOT`, connection `My Rithmic`, provider `Rithmic`, mode `Simulation`, currency `USD` |
| Provider slot | The adapter occupies a generic placeholder slot (for example `Provider14`); it is unresolved until the owner-authorized slot mapping is supplied (see §3) |
| Trading | **Disabled**. Do not enable it at any point. |

Before starting, confirm the AddOn's source was staged with
`tools/nt8-source-package/package_addon_sources.py` (never stage `Properties/AssemblyInfo.cs`).

## 1. AddOn load and initialization

- **Preconditions:** NinjaTrader 8 running; the AddOn compiled and installed; no diagnostic run active.
- **Operator action:** Open **Control Center → main menu → RiskPilot NT8 Diagnostic Center**.
- **Expected diagnostic status:** The window opens; M1 `M1.LIFECYCLE` reports the observed
  `SetDefaults → Configure → Active` sequence (PASS if ordered and reaching startup).
- **Expected evidence fields:** `environment.json` → `lifecycle` block with the observed states;
  `hostReadiness.addOnState` = `Active`.
- **PASS:** the window opens and the lifecycle check is PASS with an ordered sequence.
- **FAIL:** a backwards transition is reported (`M1.LIFECYCLE` = FAIL) or the window does not open.
- **INCONCLUSIVE:** no lifecycle surface is observable.

## 2. Host lifecycle transitions

- **Preconditions:** as §1.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M1.LIFECYCLE_PHASES` confirms the INITIALIZATION and STARTUP
  phases; `connectionPhase=DEFERRED_TO_M2` (M1 never infers the connection phase).
- **Expected evidence fields:** `environment.json` → `lifecycle.phaseFlags`, `lifecycle.connectionPhase`.
- **PASS:** phases INITIALIZATION and STARTUP confirmed.
- **FAIL:** a backwards transition or a state observed after `Terminated`.
- **INCONCLUSIVE:** an unrecognised `State` member or a missing phase.

## 3. Explicit instrument selection

- **Preconditions:** the target ES contract (for example `ES 12-26`) is loaded in NinjaTrader.
- **Operator action:** In the Diagnostic Center picker, select the exact contract FullName and click
  **Refresh** if the list is stale. Never rely on auto-discovery: with no selection the tool never
  becomes ready.
- **Expected diagnostic status:** `M1.HOST_READINESS.instrumentResolved = SATISFIED` only for the
  **exact** selected contract; with no selection `M1.HOST_READINESS` stays INCONCLUSIVE
  (`INSTRUMENT_NOT_RESOLVED`) and `M4.INSTRUMENT_IDENTITY` reports `TARGET_INSTRUMENT_NOT_CONFIGURED`.
- **Expected evidence fields:** `instruments.json` → `instrumentIdentity.selectionStatus=RESOLVED`,
  `selectionReasonCode`, `maskedRef` (`instrument#<hash>`), `candidateCount=1`.
- **PASS:** the selected contract resolves uniquely (`selectionStatus=RESOLVED`, one candidate).
- **FAIL:** a resolved contract whose readable root disagrees (`INSTRUMENT_ROOT_MISMATCH`).
- **INCONCLUSIVE:** no selection (`NOT_CONFIGURED`), not loaded (`NOT_LOADED`), ambiguous
  (`MULTIPLE_CANDIDATES`), unreadable surface (`SURFACE_UNAVAILABLE`) or a stub/mock value.

## 4. Instrument identity and venue validation

- **Preconditions:** the target contract selected and loaded (§3); its metadata readable.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M4.INSTRUMENT_IDENTITY` PASS for the canonical root/venue form
  (`ES_FUTURES_CME_*`); `M4.ES_PRODUCT_SPEC` PASS for point value 50 / tick size 0.25 / tick value
  12.50 / USD; `M4.FIELD_METADATA` PASS only when every required field was read.
- **Expected evidence fields:** `instruments.json` → `instrumentIdentity` block, `esProductSpec`,
  and the attestation `metadata` object with per-field `*Observable` flags and `fieldReadFailures`.
- **PASS:** root/venue match and all required metadata fields read.
- **FAIL:** a resolved root mismatch (`INSTRUMENT_ROOT_MISMATCH`), a wrong economics value
  (`ES_PRODUCT_SPEC`), or any recorded metadata read failure (`HOST_READ_FAILED`).
- **INCONCLUSIVE:** `EXCHANGE_METADATA_UNOBSERVABLE`, `INSTRUMENT_ROOT_UNOBSERVABLE`, a
  not-ready selection, or any unread required field (`HOST_METADATA_INCOMPLETE`).

## 5. Multiple loaded ES contracts

- **Preconditions:** two or more ES contracts loaded (for example `ES 12-26` and `ES 03-27`).
- **Operator action:** Select exactly one of them and run.
- **Expected diagnostic status:** only the selected contract resolves; the others are never chosen.
- **Expected evidence fields:** `instrumentIdentity.candidateCount` reflects the loaded set;
  `selectionStatus=RESOLVED` with one match.
- **PASS:** the explicitly selected contract resolves and the identity check is PASS.
- **FAIL:** the tool reports an identity for a contract the operator did not select.
- **INCONCLUSIVE:** an insufficiently specific selection that matches more than one contract
  (`MULTIPLE_CANDIDATES`, `TARGET_INSTRUMENT_AMBIGUOUS`).

## 6. Account discovery

- **Preconditions:** the account `SimRP.BOT` exists in NinjaTrader.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M2.ACCOUNT_IDENTITY` reports the exact-identity verdict for the
  configured target (PASS only for a fully observed, fully matching identity).
- **Expected evidence fields:** `accounts.json` → `identityVerification` block (status, reason codes,
  masked ref `account#<hash>`); `M1.HOST_READINESS.accountVerified`.
- **PASS:** exactly one exact-name match and every required field observed and matching.
- **FAIL:** a similar/differently-cased name, or a wrong provider/connection/mode/currency.
- **INCONCLUSIVE:** no account surface, zero accounts, ambiguous multi-match, or any unobserved
  required field.

## 7. Account-to-connection association

- **Preconditions:** the target account's own connection is enumerated and connected.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M2.ACCOUNT_CONNECTION_ASSOCIATION` PASS for the target account's
  **own** connection (never the first of several).
- **Expected evidence fields:** `accounts.json` → `connectionAssociation` block (status, reason codes,
  counts, `connectionRef` `connection#<hash>`); `M1.HOST_READINESS.connectionVerified`.
- **PASS:** the target's connection is enumerated, matches the required identity, and is connected.
- **FAIL:** an observed connection field that contradicts the requirement.
- **INCONCLUSIVE:** `ConnectionObserved=false`, `MatchingConnectionCount > 1`
  (`TARGET_ACCOUNT_CONNECTION_AMBIGUOUS`), `MatchingAccountCount > 1`, or the connection not
  enumerated (`TARGET_ACCOUNT_CONNECTION_NOT_ENUMERATED`).

## 8. Provider identity

- **Preconditions:** the connection reports its provider.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M2.PROVIDER_IDENTITY` PASS only when the target account's own
  connection provider is a **named** member that matches the requirement. A generic placeholder slot
  (for example `Provider14`) stays INCONCLUSIVE (`PROVIDER_SLOT_UNRESOLVED` / `PROVIDER_ROUTE_UNRESOLVED`)
  unless an owner-authorized slot mapping is supplied.
- **Expected evidence fields:** `connections.json` / `accounts.json` provider rows with
  `ResolutionBasis`, and the masked `connectionRef`.
- **PASS:** a named provider member matches (or a mapping that the owner has explicitly authorized
  and recorded resolves the placeholder to the required vendor).
- **FAIL:** a definite named member that differs.
- **INCONCLUSIVE:** an unreadable provider value, or an unresolved placeholder slot (the default).

## 9. LIVE/SIM classification

- **Preconditions:** the full verified chain account → connection → provider is available.
- **Operator action:** Start a **Quick Diagnostic** run.
- **Expected diagnostic status:** `M1.ENVIRONMENT_CLASS` PASS **only** with a fully verified chain;
  otherwise INCONCLUSIVE with the chain reason codes.
- **Expected evidence fields:** `environment.json` → `environmentVerificationChain` block
  (`verified`, `verifiedEnvironmentClass`, the four `links` booleans, `reasonCodes`).
- **PASS:** every link holds (`ENVIRONMENT_CHAIN_VERIFIED`).
- **FAIL:** not applicable to this check (an unresolved chain is INCONCLUSIVE, never FAIL).
- **INCONCLUSIVE:** any unresolved link (`ENVIRONMENT_CHAIN_ACCOUNT_UNVERIFIED` /
  `_CONNECTION_UNVERIFIED` / `_PROVIDER_UNVERIFIED` / `_CLASS_UNKNOWN`).

## 10. Level I market-data subscription and event observation

- **Preconditions:** the target contract selected and loaded; the market is open (so events arrive).
- **Operator action:** Start a **Quick Diagnostic** run and let it run a few seconds.
- **Expected diagnostic status:** `M5.DOM_SURFACE`, `M5.LEVEL1` become observable only once events
  arrive; `M5.DATA_INTEGRITY` reports a gap/drop count only for an observed stream.
- **Expected evidence fields:** `market-data-summary.json` → `dataSurfaceObservable`,
  `lastEventUtc`; `M1.HOST_READINESS.marketDataSubscribed` / `marketDataObserved`.
- **PASS:** real Level I records were ingested with no gap/drop.
- **FAIL:** a positive gap/drop/sync-failure/out-of-bounds/invalid counter.
- **INCONCLUSIVE:** no observed stream (a zero count with no stream is not "clean");
  `M5.FRESHNESS` is always INCONCLUSIVE and only labels FRESH/STALE.

## 11. Level II / DOM subscription and event observation

- **Preconditions:** the instrument exposes a Level II / DOM feed (not all do).
- **Operator action:** Start a **Full Diagnostic** run.
- **Expected diagnostic status:** `M5.DEPTH` becomes observable only once depth events arrive; depth
  is **never** inferred from a Level I quote.
- **Expected evidence fields:** `dom-summary.json` → `depthObserved`, `dataSurfaceObservable`,
  `lastEventUtc`.
- **PASS:** real Level II records were ingested and validated.
- **FAIL:** a positive invalid/out-of-bounds depth counter.
- **INCONCLUSIVE:** no Level II surface on this instrument/connection (a supported, expected outcome).

## 12. Connection loss and reconnect

- **Preconditions:** a completed run that reached `M1.HOST_READINESS.state=READY`.
- **Operator action:** Disconnect the provider connection, wait, then reconnect. Start another run.
- **Expected diagnostic status:** readiness does **not** remain satisfied across the loss: after the
  loss the connection/stream-dependent facts are invalidated (`connectionVerified`,
  `marketDataSubscribed`, `marketDataObserved` return to UNKNOWN/UNSATISFIED), and the next run
  re-establishes them.
- **Expected evidence fields:** `hostReadiness.connectionVerified` / `marketDataObserved` show the
  invalidated state on the run taken during/after the loss; `M2.RECONNECT` reports the reconnect.
- **PASS:** readiness re-reaches READY only after the subscription and events are re-observed.
- **FAIL:** readiness remains READY while the connection is down or no events are arriving.
- **INCONCLUSIVE:** the connection state is not observable.

## 13. Lifecycle shutdown and restart

- **Preconditions:** a live AddOn.
- **Operator action:** Terminate the AddOn (or restart NinjaTrader), then re-open the window.
- **Expected diagnostic status:** on termination `M1.HOST_READINESS.state=ADDON_SHUTTING_DOWN`; after a
  restart the new lifecycle epoch discards every fact observed before termination, so readiness
  starts again from `ADDON_NOT_OPERATIONAL`/`AWAITING_*` — never READY on stale facts.
- **Expected evidence fields:** `hostReadiness.state` and `hostReadiness.epoch` (the epoch increases
  across a restart).
- **PASS:** the restarted AddOn is not READY until it re-observes every fact.
- **FAIL:** the restarted AddOn reports READY before re-observing the facts.
- **INCONCLUSIVE:** the lifecycle is not observable.

## 14. Evidence export

- **Preconditions:** a completed run.
- **Operator action:** Export the evidence package (written outside the Git working tree).
- **Expected diagnostic status:** all 18 artifacts are present; the four write invariants
  (`VerifyIntegrity`, `HasCompleteArtifactSet`, `IsSecretFree`, `ManifestIsIdentitySafe`) hold.
- **Expected evidence fields:** `manifest.json` lists 17 files (all but itself) with SHA-256;
  `evidenceProvenance=RUNTIME`; `hostReadiness` block with the separated facts.
- **PASS:** the package writes and every invariant holds.
- **FAIL:** any invariant fails (the package is then never written).
- **INCONCLUSIVE:** n/a — a package that cannot be verified is simply not written.

## 15. Fail-closed behavior when required identity or metadata is unavailable

- **Preconditions:** none of the required surfaces is available (no account, no selection, no events).
- **Operator action:** Start a run with no instrument selected and no target account present.
- **Expected diagnostic status:** every dependent check is INCONCLUSIVE (never PASS):
  `M1.HOST_READINESS` (`INSTRUMENT_NOT_RESOLVED`), `M2.ACCOUNT_IDENTITY`
  (`ACCOUNT_SURFACE_UNOBSERVABLE`), `M3.ACTIVE_ORDERS` (`TARGET_ACCOUNT_NOT_VERIFIED`),
  `M4.INSTRUMENT_IDENTITY` (`TARGET_INSTRUMENT_NOT_CONFIGURED`), `M5.*` (no observed surface).
- **Expected evidence fields:** the corresponding `UNOBSERVABLE` markers in each artifact.
- **PASS:** n/a for this test.
- **FAIL:** any dependent check reports PASS or a misleading zero without an observed surface.
- **INCONCLUSIVE:** the expected outcome for every check in this scenario.

## Recording the result

For each test record: host build, provider build (if observable), the check ids and statuses, the
evidence fields, and PASS/FAIL/INCONCLUSIVE. Do **not** record any raw account/connection name or
secret — use the masked `account#<hash>` / `connection#<hash>` / `instrument#<hash>` tokens.

When a real host build has actually been run and recorded, add its version to
`NinjaTraderCompatibility.TestedVersions`; only then does `M1.HOST_COMPATIBILITY` become eligible to
report anything other than INCONCLUSIVE.
