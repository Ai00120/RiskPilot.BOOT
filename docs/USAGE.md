# User Guide — RiskPilot NT8 Diagnostic Center

This guide explains how to run the read-only diagnostics and how to read the report. See
`docs/INSTALL.md` for installation.

## What the tool does

The Diagnostic Center inspects the running NinjaTrader 8 host through its **documented public API
only** and reports what it can observe. It never changes anything: no order is sent, modified or
cancelled, no position is opened or closed, no account, connection or RiskPilot setting is
modified, and trading is never enabled.

A check that cannot be positively observed is reported as `INCONCLUSIVE` or `NOT_APPLICABLE` — never
as `PASS`. Missing data is always reported with a reason.

## Opening the window

NinjaTrader **Control Center → main menu → RiskPilot NT8 Diagnostic Center**.

## The three modes

| Mode | What it runs | Modules |
| --- | --- | --- |
| **Quick Diagnostic** | Fast environment, connection, account, instrument, market-data and safety checks | M1, M2, M3, M4, M5, M8 |
| **Full Diagnostic** | Every module, including DOM, order-flow and strategies | M1–M8 |
| **Post-Trade Audit** | Read-only analysis of already-recorded order / execution / position events | M9 |

A run executes on a background thread, so the Control Center never freezes. Only one run can be
active at a time; starting a second run while one is active is refused. **Cancel** stops the
diagnostic processing only — it never touches a trade, and a cancelled run writes no evidence
package.

## Result states

| State | Meaning |
| --- | --- |
| `PASS` | The check was positively observed to be good. |
| `FAIL` | A real problem was observed (for example a data gap or a missing approval). |
| `INCONCLUSIVE` | The check could not be decided from what was observable. |
| `NOT_APPLICABLE` | The check does not apply to the current host state. |

`INCONCLUSIVE` is a normal, honest outcome. With no connection, for example, provider identity is
`INCONCLUSIVE`, because it genuinely cannot be proven.

## The modules

| Module | Focus |
| --- | --- |
| **M1** | Environment, API & lifecycle compatibility (host version is unobservable — no public API). Sole owner of the AddOn lifecycle surface; the connection phase is deferred to M2. |
| **M2** | Connection & provider identity (price feed and order channel are tracked separately). |
| **M3** | Account & order lifecycle (F-05): order-id classification, correlation, fill continuity. |
| **M4** | Instrument attestation / D-078 field approval. |
| **M5** | DOM & market data: snapshot sync, out-of-bounds updates, data integrity. |
| **M6** | TradeDelta / OrderFlow: trade-stream observability, direction provenance, completeness, readiness. |
| **M7** | Strategies / risk gate: strategy surface and candidate rejection. |
| **M8** | Safety / acceptance: trading-disabled evidence, read-only posture, static safety scan. |
| **M9** | Post-trade audit: order/execution/position surface and correlation timing. |

## Exporting the evidence package

**Export** writes the evidence package for the last completed run. Each run gets its own directory:

```
%USERPROFILE%\Documents\RiskPilot.Diagnostics.Evidence\<session-id>\
```

The package contains 17 artifacts:

```
summary.html  full-report.html  acceptance-matrix.csv
environment.json  connections.json  accounts.json  instruments.json
orders.csv  executions.csv  positions.csv
market-data-summary.json  dom-summary.json  orderflow-summary.json
strategies.json  safety-audit.json  findings.json  manifest.json
```

- `summary.html` — the human-readable one-page summary.
- `full-report.html` — the complete report with every check and its evidence.
- `manifest.json` — session, start/end UTC, versions, provider, instrument, **masked** accounts,
  record and dropped counts, truncation status, `evidenceProvenance`, every file with its SHA-256,
  and the overall run status.

Only data that was actually observable is exported. A section whose source could not be read is
written with `status=UNAVAILABLE` and a `reason`, never fabricated and never omitted silently. Every
artifact is scanned for residual secrets before the package is declared valid, and account
references are replaced with synthetic `account#<hash>` tokens.

### Evidence provenance: runtime vs. sample

Every package carries an `evidenceProvenance` field:

- `RUNTIME` — a real in-host diagnostic run.
- `SAMPLE` — synthetic documentation data. A `SAMPLE` package also shows a visible banner in
  `summary.html`. It must never be read as a runtime result.

The repository ships one sample package at
`docs/samples/sample-evidence-package/` for documentation. It is labelled `SAMPLE`.

## Reading a package

1. Open `summary.html` first: it shows the aggregate status, the module table, the three acceptance
   subjects and the top non-PASS findings.
2. For a failing or inconclusive check, open `full-report.html` or `findings.json`. Each finding
   carries its observation, source, collection method, limitation, recommended next action and an
   evidence reference.
3. Verify integrity: `manifest.json` lists a SHA-256 for every artifact. Re-hash the files and
   compare. The tool already verifies all 17 artifacts and their hashes before it writes anything.

## The three acceptance subjects

Acceptance is reported as three independent subjects and is never collapsed into a single verdict:

1. **AddOn implementation / build** — does the tool build and load. A stub build stays
   `INCONCLUSIVE`, because compiling against stubs is not proof of host compatibility.
2. **Runtime diagnostic coverage / evidence quality** — what the run actually observed.
3. **RiskPilot.BOT acceptance** — always `NOT_APPLICABLE` from inside this tool. The tool is
   independent of RiskPilot.BOT and cannot certify it.

A green AddOn build or a green diagnostic run is **not** a RiskPilot.BOT acceptance.

## Keyboard and workflow tips

- Run **Quick Diagnostic** first; it is fast and covers the common causes.
- Run **Full Diagnostic** when you need DOM, order-flow and strategy coverage.
- Run **Post-Trade Audit** after a session to inspect the order/execution/position surface.
- Export after the run you want to keep. Each export is self-contained and hash-verified.
