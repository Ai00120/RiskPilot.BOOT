# NT8 compilation fixes — evidence and verification limits

## Assembly metadata ownership

The standalone project disables SDK assembly metadata generation and explicitly compiles
`Properties/AssemblyInfo.cs` for both net8.0/stubs and net48/real references. That is correct.
NinjaScript Editor does not use BOOT's project settings: BOOT sources become part of the
shared NinjaTrader.Custom assembly, whose own AssemblyInfo already defines the reported
attributes. Copying BOOT's metadata (or generated obj sources) into Custom causes CS0579.
The README previously allowed copying unspecified '.cs sources', despite the narrower
INSTALL guidance. There was no packaging enforcement. The user's installed tree was not
available for inspection, so the exact extra file location still needs checking locally.

Metadata is retained unchanged. The new allowlisted source packager excludes Properties,
Stubs and build outputs, rejects assembly attributes and stale sources, and refuses overlapping
source/destination trees. Installation and cleanup instructions are in INSTALL.md. Preserve
NinjaTrader's own Custom/AssemblyInfo.cs and back up files outside Custom before cleanup.

Reference: NinjaTrader support describes duplicate AssemblyInfo and generated bin/obj files:
https://forum.ninjatrader.com/forum/ninjatrader-8/indicator-development/1235613-errors-related-to-assemblyinfo-cs

## Currency API evidence

The old stub invented `Currency.Yen`, masking the host compiler error. The account adapter
referenced it directly; the instrument path duplicated the same generic ISO mapping as strings.
Only USD is relevant to the approved identity. Both paths now recognize only the documented
`Currency.UsDollar`; other observed member names remain mismatches, never converted to USD.
Read failures remain unobservable. Core verifiers and their fail-closed precedence are unchanged.

Sources inspected:
- Official account denomination documentation:
  https://docs.ninjatrader.com/ninjascript/denomination
- Official MasterInstrument.Currency example explicitly uses `Currency.UsDollar`:
  https://docs.ninjatrader.com/ninjascript/masterinstrument_currency
- Third-party reflection-generated NT8 API snapshot (Currency file history includes May 2025):
  https://github.com/matdev83/ninjatrader-autodocs/blob/222dad08/generated-code/Core/NinjaTrader.Cbi.Currency.cs
  This lists JapaneseYen=5, BritishPound=1, Euro=3 and UsDollar=7, not Yen.

The stub is aligned to that snapshot, including a non-USD zero value rather than falsely
making default(Currency) USD. This snapshot is NOT proof of the exact 8.1.9.0 ABI. No genuine
8.1.9.0 assemblies are available here; exact-version enum inspection and real compilation
remain outstanding. No replacement JPY member is referenced by production identity code.
The offline-only denomination fault hook supports a getter-failure regression; it is not
represented as a real NinjaTrader API.

## Validation

- Python source-packaging/regression checks and static read-only safety scan run locally.
- xUnit tests cover USD, observed non-USD, missing denomination, throwing denomination getter,
  instrument currency mismatch, ES economics mismatch and unavailable instrument metadata.
- Both existing GitHub Actions build/regression workflows run the new packaging checks.
- Local dotnet build/test cannot execute: SDK absent and SDK download endpoints blocked.
  Consult this PR's CI results for the full stub build and automated test execution.
- No real NinjaTrader build or runtime validation has been performed. The declared tested-host
  list remains unchanged (empty). No BOT files or trading functionality were modified.

## Windows acceptance still required

1. Confirm installed NinjaTrader is 8.1.9.0 and inspect Currency in its actual referenced assembly
   using Visual Studio Object Browser; confirm UsDollar and record enum members/values.
2. Outside Custom, build the standalone project with UseStubs=false and retain the complete log.
3. Close NT8, back up the BOOT installation outside Custom, remove conflicting BOOT metadata and
   generated source copies, then stage only the source bundle following INSTALL.md. Do not mix
   DLL and source installations. Never delete the host's own AssemblyInfo.cs.
4. Compile in NinjaScript Editor and retain the complete error list or successful compile log.
5. Run read-only diagnostics with the approved SimRP.BOT / My Rithmic / Rithmic / Simulation /
   ES / CME / USD identity. Missing denomination must not PASS; non-USD must not PASS. Do not
   place trades or change account currency merely to test diagnostics.
