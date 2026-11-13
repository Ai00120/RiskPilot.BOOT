# RP-DIAG-01 — PHASE 3: DIAGNOSTIC MODULES

**Product:** RiskPilot NT8 Diagnostic Center
**Namespace:** `RiskPilot.Diagnostics.Nt8`
**Document type:** Phase 3 implementation record
**Prepared:** 2026-11-12
**Safety invariant:** `Trading=Disabled` — read-only, no order/execution/account mutation.
**Depends on:** `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md`, `docs/PHASE2_IMPLEMENTATION.md` (approved).

> این سند ثبت می‌کند که در فاز ۳ **واقعاً چه ساخته شد**، چگونه راستی‌آزمایی شد و — صریحاً —
> چه چیزی **راستی‌آزمایی نشد**. بیلد آفلاین از stubهای زمان‌کامپایل استفاده می‌کند، بنابراین
> **سازگاری واقعی با میزبان NinjaTrader 8.1.8.4 با این فاز اثبات نمی‌شود** (بخش ۱۱).
>
> هیچ تغییری در `Ai00120/RiskPilot.BOT` انجام نشد. سورس BOT فقط برای شناخت رابط‌ها و منطق
> موجود **مطالعه** شد (read-only).

---

## 1. خلاصهٔ اجرایی

فاز ۳ هستهٔ فاز ۲ را به **هشت ماژول تشخیصی (M1–M8)** گسترش می‌دهد. هر ماژول:

- فقط از **API عمومی و مستند** NinjaTrader استفاده می‌کند؛
- **بدون** Reflection، بدون `unsafe`، بدون API خصوصی؛
- **fail-closed**: دادهٔ گمشده = `INCONCLUSIVE`، هرگز `PASS`؛
- هر Finding با `Id / Severity / Observation / Timestamp / Source / Method / Limitation /
  NextAction / ReasonCodes` ساخته می‌شود؛
- اطلاعات محرمانه با `SecretRedactor` / `SyntheticRef` ماسک می‌شود.

| # | ماژول | لایهٔ جدید اصلی |
|---|---|---|
| M1 | Environment / API Compatibility | `Probes/M1EnvironmentProbe.cs` |
| M2 | Connection / Provider Identity | `Probes/M2ConnectionProbe.cs` |
| M3 | Account / Order Lifecycle (F-05) | `Probes/M3AccountOrderProbe.cs` + `Consumers/M3OrderLifecycleConsumer.cs` |
| M4 | Instrument Attestation / D-078 | `Probes/M4InstrumentAttestationProbe.cs` + `Instruments/CanonicalInstrumentId.cs` + `Core/Identity/InstrumentIdentityVerifier.cs` |
| M5 | DOM / Market Data | `Probes/M5DomMarketDataProbe.cs` + `Consumers/M5DomMarketDataConsumer.cs` |
| M6 | TradeDelta / OrderFlow | `Probes/M6TradeDeltaProbe.cs` + `Consumers/M6TradeDeltaConsumer.cs` |
| M7 | Strategies / Risk Gate | `Probes/M7StrategyRiskGateProbe.cs` + `Consumers/M7StrategyConsumer.cs` |
| M8 | Safety / Acceptance | `Probes/M8SafetyAcceptanceProbe.cs` |

زیرساخت مشترک: `Core/Models/Observations/*` (هشت مدل مشاهده)،
`Core/Engine/FindingFactory.cs`، `Core/Engine/SnapshotAssembler.cs`،
`Core/Engine/EnrichedDataProvider.cs`، `Core/Engine/AttestationReasonCodes.cs`،
`Core/Session/RuntimeCounters.cs`، و `Nt8HostDataProvider.GetSnapshot()`.

### نتیجهٔ راستی‌آزمایی (offline)

```
dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
  → Passed!  Failed: 0, Passed: 149, Total: 149

python3 verification/addon-smoke/test_diagnostic_safety.py
  → STATIC SAFETY CHECK: PASS  (75 production source files, 34 forbidden patterns)
```

---

## 2. معماری داده — مسیر تک‌جهته

```
NinjaTrader public API
        │  (فقط لایهٔ Host می‌خواند)
        ▼
Nt8HostDataProvider.GetSnapshot()  ──►  DiagnosticSnapshot (Core، بدون نوع NT8)
        │                                   │
        │ (EnrichedDataProvider)            │
        ▼                                   ▼
RuntimeCounters ──► SnapshotAssembler ──► snapshot نهایی
        ▲                                   │
        │ (Consumers روی drain thread)      ▼
NormalizedRecord ─────────────────────► ProbeContext.Snapshot ──► M1..M8 Probes
```

- `Core/` هیچ نوع NinjaTrader ندارد؛ تنها لایهٔ `Host/` به NT8 دست می‌زند.
- Counterهای رخداد (drain thread) با مقادیر snapshot میزبان (point-in-time) **جمع** می‌شوند،
  نه بازنویسی؛ پس یک snapshot فقط-میزبان هم شواهدش را گزارش می‌کند.
- Callbackهای میزبان **سبک و thread-safe**اند؛ کار سنگین روی drain thread انجام می‌شود.

---

## 3. ماژول ۱ — Environment / API Compatibility

**پیاده‌سازی:** `Core/Engine/Probes/M1EnvironmentProbe.cs`، `Host/Nt8HostDataProvider.GetEnvironment()`،
`BuildRuntimeObservation()`، `Host/DiagnosticAddOn.cs` (ثبت lifecycle).

| Check | معنا |
|---|---|
| `M1.API_SURFACE` | APIهای لازم در دسترس‌اند (`GetPresentApis`/`GetMissingApis`) |
| `M1.PROCESS_ARCHITECTURE` | ۳۲/۶۴-bit از `Environment.Is64BitProcess` |
| `M1.CLR_RUNTIME` | نسخهٔ CLR از `Environment.Version` |
| `M1.CAPABILITY_MATRIX` | Available / Unavailable / Unobservable برای هر قابلیت |
| `M1.LIFECYCLE` | توالی حالت‌ها: `SetDefaults → Active → Terminated` |
| `M1.BUILD_MODE` | `UseStubs` و `TargetFramework` |
| `M1.ENVIRONMENT_CLASS` | Live / Simulation / Unknown |
| `M1.HOST_VERSION` | همیشه `INCONCLUSIVE` (Unobservable) |

**مرجع API:** `NinjaTrader.NinjaScript.AddOnBase`, `State`/`OnStateChange`, `System.Environment`.
**تست‌ها:** `Phase3ModuleTests.M1_*` (۵ تست) + `ProbeTests.M1_*`.
**محدودیت:** نسخهٔ دقیق Runtime میزبان و نسخهٔ provider با API عمومی **قابل مشاهده نیست**؛ از روی
نام فایل یا BuildId **حدس زده نمی‌شود**.

---

## 4. ماژول ۲ — Connection / Provider Identity

**پیاده‌سازی:** `Probes/M2ConnectionProbe.cs`، `Host/Nt8HostDataProvider.GetConnections()/GetProvider()`،
`BuildProviderEvidence()`، `BuildTargetAccountConnectionObservation()`، `BuildAccountAssociations()`،
`Identity/TargetAccountConnectionVerifier.cs`.

| Check | معنا |
|---|---|
| `M2.CONNECTIONS` | وجود Connection |
| `M2.CONNECTION.<role>` | PriceStatus و Status (کانال سفارش) جداگانه |
| `M2.PROVIDER_IDENTITY` | Provider واقعی با `ConnectOptions.Provider.ToString()`، **منسوب به Connection خودِ حساب هدف** (نه اولین Connection) |
| `M2.RECONNECT` | شناسایی Reconnect از توالی وضعیت |
| `M2.ACCOUNT_CONNECTION_ASSOCIATION` | **اثبات** تعلقِ حساب هدف به Connection/provider موردنیاز (نه فرض اولین Connection) |
| `M2.ACCOUNT_PROVIDER_AGREEMENT` | تطابق provider هر حساب با Connection گزارش‌شدهٔ همان حساب |
| `M2.ACCOUNT_IDENTITY` | تطابق **دقیق** هویت حساب با هویت دائم RiskPilot.BOT (D-095/D-096) |

**بررسی PR #275 (BOT):** در BOT تشخیص Provider با `provider.ToString()` انجام شده چون عضو enum
`Provider.Rithmic` تضمین‌شده نیست. این ابزار همان روش را mirror می‌کند و **علاوه بر آن**:

- `ResolutionMethod = "String"` را ثبت می‌کند؛
- `ProviderEvidence.ResolutionBasis` را ثبت می‌کند: `TARGET_ACCOUNT_CONNECTION` (از Connection خودِ
  حساب هدف)، `SINGLE_CONNECTION` (تنها Connection قابل‌شمارش؛ بدون ابهام) یا `UNAVAILABLE`. Provider
  **هرگز** از اولین Connection در میان چند Connection برداشته نمی‌شود؛ اگر منسوب نشود،
  `M2.PROVIDER_IDENTITY` **`INCONCLUSIVE`** می‌شود (reason: `PROVIDER_NOT_ATTRIBUTED`).
- `ProviderEvidence.IsStubOrMockValue` — اگر مقدار از stub/mock بیاید، `M2.PROVIDER_IDENTITY`
  **`INCONCLUSIVE`** می‌شود (reason: `PROVIDER_VALUE_FROM_STUB_OR_MOCK`). صرف نتیجهٔ Mock/Stub
  برای PASS کافی نیست.

### ۴.۱ تعلقِ دقیق حساب به Connection/provider (`M2.ACCOUNT_CONNECTION_ASSOCIATION`)

این check اثبات می‌کند که **حساب هدف** به Connection موردنیاز (`My Rithmic`) و provider موردنیاز
(`Rithmic`) **متعلق** است، به‌جای بازرسی یک Connection دلخواه یا اولین Connection موجود. لایهٔ Host
(`Nt8HostDataProvider.BuildTargetAccountConnectionObservation()`) تنها Connection خودِ حساب هدف
(`Account.Connection`) را می‌خواند و همهٔ نام‌های خام را به boolean/count و یک `SyntheticRef` کاهش
می‌دهد؛ تصمیم با `TargetAccountConnectionVerifier` است (fail-closed):

- نبودِ سطح حساب یا مقدار stub/mock ⇒ `INCONCLUSIVE`؛
- نبودِ حساب ⇒ `INCONCLUSIVE`؛ نامِ متفاوت (case/مشابه) ⇒ `FAIL`؛
- بیش از یک حساب با تطابق دقیق ⇒ `INCONCLUSIVE` (ابهام)؛
- بیش از یک Connection با هویت موردنیاز ⇒ `INCONCLUSIVE` (ابهام؛ `TARGET_ACCOUNT_CONNECTION_AMBIGUOUS`)؛
- حساب بدون Connection گزارش‌شده ⇒ `INCONCLUSIVE`؛
- فیلد مشاهده‌شده و مغایر ⇒ `FAIL` (outrank بر فیلد غیرقابل‌مشاهده)؛
- فیلد موردنیازِ غیرقابل‌مشاهده ⇒ `INCONCLUSIVE`؛
- تنها تطابق کامل و کاملاً مشاهده‌شده ⇒ `PASS`.

خروجی هیچ‌گاه نام خام حساب/Connection را در finding، log یا artifact نمی‌گذارد؛ `accounts.json` یک
بلوک `connectionAssociation` (status/reasonCodes/counts + `accountRef` مصنوعی) دارد. همین قاعده بر
check عمومی `M2.ACCOUNT_PROVIDER_AGREEMENT` و artifactهای `connections.json` و `accounts.json`
(`associations[]`) نیز اعمال می‌شود: نام Connection فقط به‌صورت توکن `SyntheticRef`
(`connection#<hash>`، فیلد `connectionRef`) نوشته می‌شود.


### ۴.۲ هویت دقیق حساب (`M2.ACCOUNT_IDENTITY`)

هویت دائم مورد نیاز BOT: حساب `SimRP.BOT`، Connection `My Rithmic`، provider `Rithmic`،
mode `Simulation`، currency `USD`.

- لایهٔ Host (`Nt8HostDataProvider.BuildAccountIdentityObservation()`) نام واقعی حساب را
  **فقط به‌صورت داخلی** با تطابق ordinal دقیق مقایسه می‌کند و خروجی را به یک
  `AccountIdentityObservation` بدون هویت خام (فقط boolean/count + `SyntheticRef`) تبدیل می‌کند.
- `AccountIdentityVerifier` (Core، host-free) حکم را می‌دهد: `PASS` فقط وقتی همهٔ فیلدها
  مشاهده شده و منطبق باشند؛ یک نام مشابه/حروف بزرگ‌وکوچک متفاوت هرگز تطابق نیست؛ فیلد
  مشاهده‌نشده ⇒ `INCONCLUSIVE`؛ تعارض قطعی (provider/mode/currency/connection اشتباه) ⇒ `FAIL`.
- نام خام حساب/Connection هرگز در finding، log، report یا artifact نوشته نمی‌شود؛ در
  `accounts.json` فقط `identityVerification` با status/reasonCodes/masked ref می‌آید.
- تست‌ها: `AccountIdentityTests.M2_*` و `HostAccountIdentityTests.Host_*`.

**مرجع API:** `NinjaTrader.Cbi.Connection` (`PriceStatus`, `Status`), `ConnectOptions.Provider`, `Account.Provider`.
**تست‌ها:** `Phase3ModuleTests.M2_*` (۳ تست) + `ProbeTests.M2_*`.
**محدودیت:** نسخهٔ build provider قابل مشاهده نیست؛ مقدار واقعی Runtime باید روی میزبان واقعی
ثبت شود (بخش ۱۱).

---

## 5. ماژول ۳ — Account / Order Lifecycle (F-05)

**پیاده‌سازی:** `Probes/M3AccountOrderProbe.cs`، `Consumers/M3OrderLifecycleConsumer.cs`،
`Host/Nt8HostDataProvider.BuildOrderLifecycleObservation()`.

هدف: بررسی **مستقل و دقیق** مشکل F-05 با API عمومی.

| Check | معنا |
|---|---|
| `M3.ACCOUNT_SURFACE` | دسترسی به `Account.Orders` |
| `M3.ORDERID_CLASSIFICATION` | طبقه‌بندی OrderId |
| `M3.CORRELATION` | ارتباط OrderId ↔ Execution |
| `M3.F05_ORDER_FILL_CONTINUITY` | **Finding ID = `F-05`** |
| `M3.TERMINAL_RETENTION` | نگه‌داشت حالت‌های پایانی |
| `M3.MULTI_SCOPE` | چند ابزار و چند Strategy |

**طبقه‌بندی OrderId:** `NULL` / `EMPTY` / `WHITESPACE` / `VALID` / `UNAVAILABLE`
(`M3OrderLifecycleConsumer.Classify`). `EMPTY` و `WHITESPACE` → `FAIL`.

**Correlation:** هر snapshot/رویداد با `CorrelationId`، `OrderRef` (ماسک‌شده با `SyntheticRef`)،
`Source` و `IngestedAtUtc` ثبت می‌شود. **هیچ ارتباطی بدون مدرک قطعی حدس زده نمی‌شود**؛ اگر
`ExecutionsInScope=false` یا رویداد بی‌جفت وجود داشته باشد → `F-05` = `INCONCLUSIVE`، نه PASS.

**مرجع API:** `Account.Orders`, `Order.OrderState`, `Order.OrderId`, `Order.Instrument`,
رویدادهای `OrderUpdate` / `ExecutionUpdate` / `PositionUpdate`.
**تست‌ها:** `Phase3ModuleTests.M3_*` (۵ تست).
**محدودیت:** هیچ سفارش جدیدی ارسال/تغییر داده نمی‌شود؛ بنابراین این ماژول فقط **مشاهده‌گر** است.
حالت F-05 روی دادهٔ واقعی حساب باید ثبت شود.

---

## 6. ماژول ۴ — Instrument Attestation / D-078

**پیاده‌سازی:** `Probes/M4InstrumentAttestationProbe.cs`، `Instruments/CanonicalInstrumentId.cs`،
`Core/Engine/AttestationReasonCodes.cs`، `Core/Identity/InstrumentIdentityVerifier.cs`،
`Host/Nt8HostDataProvider.BuildAttestationObservation()`،
`Host/Nt8HostDataProvider.BuildInstrumentIdentityObservation()`.

| Check | معنا |
|---|---|
| `M4.INSTRUMENT_IDENTITY` | تطابق هویت واقعی instrument انتخاب‌شده با ES/CME (PASS/FAIL/INCONCLUSIVE) |
| `M4.HOST_METADATA` | Name, Contract/Expiry, Exchange, Tick Size/Value, Currency |
| `M4.FIELD_METADATA` | کامل‌بودن فیلدهای Master Instrument |
| `M4.D096_ROUTE_CANDIDATE` | کاندید مسیر Globex + Rithmic-only |
| `M4.REGISTRY_MATCH` | تطابق Canonical Instrument Id |
| `M4.D078_APPROVAL` | وضعیت تأیید D-078 |
| `M4.ATTESTATION_OUTCOME` | علل شکست به‌صورت جدا |

**هویت instrument (M4.INSTRUMENT_IDENTITY):** میزبان سطح `Instrument.All` (instrument‌های
بارگذاری‌شدهٔ NinjaTrader) را می‌خواند — نه یک نام ساخته‌شده از یک رشتهٔ پیکربندی — و هر فیلد را به
boolean/count + `instrument#<hash>` کاهش می‌دهد. مقایسهٔ ریشهٔ نماد **exact و ordinal** است
(`ES`)، نوع `Future`، ارز `USD` و صرافی فقط وقتی تأیید می‌شود که متادیتای مسیر پیکربندی‌شدهٔ D-096
(مسیر انتخاب‌شدهٔ Globex، مجموعهٔ پیکربندی‌شدهٔ دقیقاً {Globex}، تنها مسیر provider متصل Rithmic)
برقرار باشد. **CME هرگز از متن «ES» استنتاج نمی‌شود** و صرافی تأییدنشده هرگز PASS نمی‌شود.

- wrong instrument (ریشهٔ مشابه/متفاوت) → `FAIL` (`INSTRUMENT_ROOT_MISMATCH`)
- missing instrument → `INCONCLUSIVE` (`INSTRUMENT_IDENTITY_NO_INSTRUMENT_OBSERVED`)
- ambiguous instrument (بیش از یک تطابق) → `INCONCLUSIVE` (`INSTRUMENT_IDENTITY_AMBIGUOUS`)
- missing exchange metadata → `INCONCLUSIVE` (`EXCHANGE_METADATA_UNOBSERVABLE`)
- unsupported route (Globex/provider/مجموعهٔ صرافی مغایر) → `FAIL` (`EXCHANGE_MISMATCH`)
- فیلد غیرقابل‌مشاهده → `INCONCLUSIVE`؛ یک تضاد مشاهده‌شده بر فیلد غیرقابل‌مشاهده **اولویت** دارد.

**علل شکست (جداگانه ثبت می‌شوند):**

- `HOST_UNAVAILABLE`
- `D078_FIELD_APPROVAL_NOT_APPROVED`
- `MISSING_INSTRUMENT_SPECIFICATION`

**قواعد سخت:**

- `CanonicalInstrumentId.Build` فقط یک **حدس امن** است؛ در صورت عدم‌تطابق با `EvidenceIdentity`
  (`ES_FUTURES_CME_DEC26`) → `MISSING_INSTRUMENT_SPECIFICATION`.
- هویت دائمی **ریشهٔ ES** است (D-095 §5) و expiry پین نمی‌شود؛ فقط فرم canonical ریشه/venue لازم است.
- `D078Approved` در ابزار **همیشه false** است؛ ابزار **هرگز خودکار Approve نمی‌کند**. تأیید
  D-078 یک فرآیند Owner-controlled و خارج از ابزار است.
- `MasterInstrument.*` فقط بعد از `DataLoaded`/`Realtime` خوانده می‌شود. آمادگی یک probe زندهٔ
  `Func<bool>` است که در هر خواندن ارزیابی می‌شود (نه مقدار ضبط‌شده در سازنده)؛ AddOn در
  `State.Active` ساخته می‌شود، پس مقدار ضبط‌شده همیشه false می‌ماند. AddOn مقدار `() => HostIsLive`
  را می‌دهد. قبل از آمادگی `INCONCLUSIVE` است، نه FAIL.
- هدف attestation **همان instrument واقعی بارگذاری‌شده** است: بدون id پیکربندی‌شده، میزبان
  `Instrument.All` را می‌خواند و instrumentی را attest می‌کند که ریشهٔ `MasterInstrument.Name` آن با
  ریشهٔ دائمی ES مطابقت دارد؛ صفر یا بیش از یک تطابق ⇒ `HOST_UNAVAILABLE` (fail-closed).

**مرجع API:** `Instrument.All`, `Instrument.FullName`, `Instrument.Expiry`, `Instrument.Exchange`,
`Instrument.MasterInstrument.{Name,InstrumentType,Currency,TickSize,PointValue,Exchanges}`.
**تست‌ها:** `Phase3ModuleTests.M4_*`, `InstrumentIdentityTests` (verifier + probe),
`HostInstrumentIdentityTests` (آداپتور میزبان روی stub).
**محدودیت:** Registry و وضعیت D-078 توسط فرآیند بازبینی‌شدهٔ Owner تأمین می‌شوند؛ ابزار فقط
**مشاهده** می‌کند. صرافی یک mapping مسیر پیکربندی‌شده است، نه attestation مستقل از صرافی.
اطلاعات تأییدنشده هرگز معتبر نمایش داده نمی‌شود.

---

## 7. ماژول ۵ — DOM / Market Data

**پیاده‌سازی:** `Probes/M5DomMarketDataProbe.cs`، `Consumers/M5DomMarketDataConsumer.cs`،
`Host/MarketDataCapture.cs`، `SnapshotAssembler.MergeDomMarketData`.

| Check | معنا |
|---|---|
| `M5.DOM_SURFACE` | دسترس‌پذیری سطح دادهٔ DOM/Market Data (Level I یا Level II یا شمارنده‌های Host Adapter) |
| `M5.LEVEL1` | Level I Quote + Last Trade |
| `M5.DEPTH` | Level II DOM (جریانی جدا از Level I؛ یک Quote هرگز به‌عنوان Depth گزارش نمی‌شود) |
| `M5.HOST_LAYER_COUNTERS` | شمارنده‌های لایهٔ Host Adapter |
| `M5.RECORDER_LAYER_COUNTERS` | شمارنده‌های لایهٔ Recorder |
| `M5.SNAPSHOT_SYNC` | Snapshot Sync / Failure / Out-of-Bounds |
| `M5.VALIDITY` | Invalid Price/Size/Position و رویدادهای Malformed |
| `M5.DATA_INTEGRITY` | Sequence Gap و Drop روی مسیر Ingest |
| `M5.FRESHNESS` | تازگی داده (سن آخرین رویداد مشاهده‌شده) |

**جریان داده (از API میزبان تا نتیجه):** `Instrument.MarketData` (Level I) و
`Instrument.MarketDepth` (Level II) توسط `MarketDataCapture` — فقط روی ES ای که NT8
واقعاً بارگذاری کرده (`ResolveLoadedInstrument`) — اشتراک می‌شوند و هر رویداد به یک
`MarketDataRecord` / `DomRecord` نرمال‌سازی و وارد `EventIngestPipeline` می‌شود. سپس
`M5DomMarketDataConsumer` روی رشتهٔ Drain شمارنده‌ها و زمان آخرین رویداد را در
`SessionContext` ثبت می‌کند و `SnapshotAssembler` آن‌ها را روی `DomMarketDataObservation`
ادغام می‌کند. هیچ مقدار ساختگی تولید نمی‌شود.

**قرارداد صحت (Fail-Closed):**

- `PASS` فقط روی دادهٔ واقعاً مشاهده‌شده. بدون سطح دادهٔ مشاهده‌شده، `M5.DOM_SURFACE`،
  `M5.DEPTH`، `M5.LEVEL1`، `M5.DATA_INTEGRITY` و `M5.FRESHNESS` همگی `INCONCLUSIVE`.
- یک شمارندهٔ مثبت (Sync Failure، Out-of-Bounds، Invalid، Gap، Drop) خودش شاهد است و
  `FAIL` می‌دهد؛ صفر فقط پس از مشاهدهٔ واقعی جریان «پاک» است.
- `M5.VALIDITY` از رکوردهای واقعیِ وارد‌شده مشتق می‌شود: `M5DomMarketDataConsumer` هر
  رکورد DOM با Price منفی/غیرمتناهی، Volume منفی، Position منفی یا Operation/Instrument
  نامعتبر را شمارش می‌کند (Reset اعتبارسنجی نمی‌شود). یک رویداد Malformed خودش سطح داده را
  «مشاهده‌شده» می‌کند تا از‌دست‌رفتن پشت سطح «غیرقابل‌مشاهده» پنهان نشود.
- `M5.FRESHNESS` همیشه `INCONCLUSIVE` است و فقط `FRESH`/`STALE` را با سن واقعی گزارش
  می‌کند؛ سنِ منفی (رویداد آینده نسبت به ساعت ابزار) قابل‌اعتماد نیست و `INCONCLUSIVE`
  می‌ماند.
- شاهدِ مشاهده‌شده باید به **هر دو مسیر** برسد: Probe زنده (از طریق `ProbeContext` →
  `SnapshotAssembler.Assemble`) و بستهٔ شواهد صادرشده
  (`DiagnosticRuntime.BuildEvidenceDataset`). تنظیم فقط در یک مسیر باعث می‌شود
  `market-data-summary.json` / `dom-summary.json` سطحی «مشاهده‌نشده» را گزارش کنند، درحالی‌که
  رکوردهای واقعی وارد شده‌اند.

**دو لایه جداگانه گزارش می‌شوند** (شمارنده‌ها هرگز با هم مخلوط نمی‌شوند):

- **Host Adapter** (Baseline لاگ RiskPilot): Snapshot Sync = 265، Sync Failure = 1،
  Update Out of Bounds = 264، Insert Out of Bounds = 1، Native Snapshot = 64 Bid / 64 Ask،
  Internal DOM = 10 Bid / 6 Ask.
- **Recorder** (Baseline Recorder): `DOM_DEPTH` = 102,300؛ UPDATE = 92,988؛ INSERT = 7,007؛
  REMOVE = 2,078؛ RESET = 227.

**مرجع API:** `Instrument.MarketData` (Level I، `MarketDataEventArgs`)،
`Instrument.MarketDepth` (Level II، `MarketDepthEventArgs`)، `MarketDataType` و `Operation`
(هر دو در `NinjaTrader.Data`)، `Instrument.MarketDepth.Reset`.
**تست‌ها:** `M5DomMarketDataTests` (معتبر/گم‌شده/کهنه/نامعتبر + آداپتور میزبان روی stub)،
`Phase3ModuleTests.M5_*`، `Phase5ScenarioTests` (سناریوهای DOM).
**محدودیت:** Out-of-Bounds به‌تنهایی = از‌دست‌رفتن قطعی داده نیست؛ فقط نقض ایندکس است. Gap/loss
واقعی با شمارندهٔ Sync Failure و Sequence جدا گزارش می‌شود. یک Build سبز روی stub
**اثبات سازگاری با میزبان واقعی NT8 نیست**.

---

## 8. ماژول ۶ — TradeDelta / OrderFlow

**پیاده‌سازی:** `Probes/M6TradeDeltaProbe.cs`، `Consumers/M6TradeDeltaConsumer.cs`،
`SnapshotAssembler.MergeTradeDelta`.

| Check | معنا |
|---|---|
| `M6.TRADE_STREAM` | آیا جریان معاملات اصلاً دیده شد (سطح order-flow قابل‌دسترس بود) |
| `M6.DIRECTION_SOURCE` | منبع جهت (Provider-supplied vs Quote-derived) |
| `M6.UNKNOWN_DIRECTION` | Unknown و علتش (INSIDE_SPREAD / NO_BBO / ...) |
| `M6.TRADEDELTA_COMPLETENESS` | کامل‌بودن TradeDelta (تنها با شاهد جریان تأیید می‌شود) |
| `M6.RESET_HANDLING` | مدیریت Reset (Level I و Level II جدا) |
| `M6.SEQUENCE_CONTINUITY` | پیوستگی توالی (فقط با توالی منبع مشاهده‌شده) |
| `M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP` | بررسی اختلاف ظاهری |

**Baseline:** BUY derived = 12,074؛ SELL derived = 12,414؛ Unknown/INSIDE_SPREAD = 96.
تمام Source Timestampها `SERVER_TIME_UNVERIFIED` بوده‌اند.

**دادهٔ واقعی میزبان (اصلاح‌شده):** سطح عمومی Level I هیچ aggressor side ندارد، بنابراین چاپ‌هایی
که بدون طبقه‌بندی می‌رسند در `TradeDirectionClassifier` با مقایسهٔ قیمت با بهترین Bid/Ask همان
رویداد (یا آخرین Quote مشاهده‌شده) مشتق می‌شوند. نتیجه همیشه `DerivedFromQuote` شمرده می‌شود و
هیچ‌وقت به‌عنوان provider-confirmed گزارش نمی‌شود. یک چاپ که قابل طبقه‌بندی نیست دلیل صریح
خود را نگه می‌دارد (هرگز حدس زده نمی‌شود)، و یک چاپ که خودش طبقه‌بندی/دلیل دارد دوباره مشتق نمی‌شود.

**Fail-closed:** `M6.TRADE_STREAM`، `M6.SEQUENCE_CONTINUITY` و `M6.TRADEDELTA_COMPLETENESS`
بدون مشاهدهٔ جریان معاملات هرگز `PASS` نمی‌دهند. NT8 هیچ توالی منبع روی Level I ندارد، پس
`M6.SEQUENCE_CONTINUITY` بدون توالی مشاهده‌شده `INCONCLUSIVE` می‌ماند (شمارش صفر به‌تنهایی
چیزی را اثبات نمی‌کند)؛ یک شمارش Gap مثبت مستقل از مشاهده، `FAIL` است. یک `COMPLETE` که با شاهد
مشاهده‌شده تناقض دارد (بدون جریان، یا با Gap، یا با جهت طبقه‌بندی‌نشده) `FAIL` می‌شود.

**تحلیل اختلاف ظاهری OrderFlow (بدون اعلام تناقض/رفع):** در لاگ، OrderFlow در آخرین Heartbeat
`Available` گزارش شده، اما استراتژی OrderFlow به علت `RESET_OPERATION_RECEIVED` کنار گذاشته شده
است. این دو **دو Scope متفاوت** هستند (readiness در سطح دادهٔ بازار vs کنارگذاری در سطح
تصمیم استراتژی). ابزار آن‌ها را در یک Check با reason code
`NOT_DECLARED_CONTRADICTION` گزارش می‌کند و **بدون مدرک، تناقض یا رفع اعلام نمی‌کند** →
`INCONCLUSIVE`. هیچ API عمومی‌ای readiness موتور OrderFlow را افشا نمی‌کند؛ وقتی عرضه نشود
`UNOBSERVABLE` گزارش می‌شود.

**نکتهٔ مهم:** جهت‌های Quote-derived به‌عنوان **تأییدشده** گزارش نمی‌شوند. وقتی
`AllDirectionsQuoteDerived=true`، `M6.DIRECTION_SOURCE` = `INCONCLUSIVE` با reason
`PROVENANCE=ALL_QUOTE_DERIVED` (زیرا aggressor side از Level I قابل استخراج قطعی نیست).

**مرجع API:** `MarketDataEventArgs` (`Price`, `Volume`, Bid/Ask), رویدادهای `MarketData`.
**تست‌ها:** `M6TradeDeltaTests` (کلاسیفایر + مصرف‌کننده + assembler + probe + capture میزبان +
انتگراسیون runtime) و `Phase3ModuleTests.M6_*` / `Phase5ScenarioTests` (سناریوهای M6).
**محدودیت:** aggressor side از API عمومی Level I **قابل استخراج قطعی نیست**؛ بنابراین Unknown
یک حالت معتبر است، نه خطا. عرضهٔ TradeDelta (شمارش خرید/فروش تجمعی) و OrderFlow readiness
داخل runtime معامله‌گر است و از بیرون قابل مشاهده نیست.

---

## 9. ماژول ۷ — Strategies / Risk Gate

**پیاده‌سازی:** `Probes/M7StrategyRiskGateProbe.cs`، `Consumers/M7StrategyConsumer.cs`.

پنج Strategy ثبت‌شده (`M7StrategyRiskGateProbe.RegisteredStrategies`):

- `STRAT_SCALPING_CANDIDATE_01`
- `STRAT_TREND_PULLBACK_01`
- `STRAT_BREAKOUT_CONTINUATION_01`
- `STRAT_LEVEL_AWARE_REVERSAL_01`
- `STRAT_ORDER_FLOW_INTRADAY_01`

| Check | معنا |
|---|---|
| `M7.STRATEGY_SURFACE` | پنج Strategy ثبت‌شده |
| `M7.STRATEGY_EVALUATION` | Candidate counts؛ Valid/Invalid/Unavailable |
| `M7.CANDIDATE_REJECTION` | علت رد کاندیدها (مثل `ALL_CANDIDATES_INVALID`) |
| `M7.STAGE3_SIZING` | Stage 3 position sizing (Zero Quantity) |
| `M7.RISK_GATE` | Stage 4 Risk Gate veto (`ANALYTICAL_NO_TRADE`, `MISSING_INSTRUMENT_SPECIFICATION`) |
| `M7.STAGE_CORRELATION` | ارتباط Decision ↔ Proposal ↔ Evaluation |
| `M7.REASON_ATTRIBUTION` | Reason codeهای قابل‌نسبت‌دادن به Stage (و کدهای نامشخص = Unattributed) |

**Baseline:** `ALL_CANDIDATES_INVALID` = 311,179؛ Stage 3 Zero Quantity در موارد متعدد؛
Risk Gate veto با `ANALYTICAL_NO_TRADE` و `MISSING_INSTRUMENT_SPECIFICATION`؛ کنارگذاری
OrderFlow با `RESET_OPERATION_RECEIVED`؛ کنارگذاری TradeDelta با `UNAVAILABLE_TRADE_DELTA`
(وضعیت Incomplete).

**قاعده:** این اعداد **علت‌یابی‌شده تلقی نمی‌شوند**. ابزار شواهد خام و ارتباط رخدادها را نشان
می‌دهد. اگر `CorrelationComplete=false` → `M7.STAGE_CORRELATION` = `INCONCLUSIVE` با
`CorrelationGapReason` (مثل `PROPOSAL_ID_MISSING`). استراتژی Unavailable هرگز Active فرض نمی‌شود.

**مرجع API:** سطح Strategy/Risk در BOT (مطالعهٔ read-only) + مدل‌های Core.
**تست‌ها:** `M7StrategyTests` (accumulator + assembler + host capture + runtime integration)
و `Phase3ModuleTests.M7_*` و `Phase5ScenarioTests.Scenario_MissingStrategyCapability_*`.
**محدودیت:** Strategyها داخل BOT اجرا می‌شوند؛ این ابزار فقط شواهد را از قرارداد فقط‌خواندنی
`IReadOnlyDiagnosticSource.ReadStrategies()` می‌خواند. بدون آن منبع، `M7.STRATEGY_SURFACE` و
همهٔ Checkهای M7 = `INCONCLUSIVE` همراه با Reason Code توضیحی؛ هیچ مقدار ساختگی یا هاردکد
جایگزین نمی‌شود. یک Strategy مشاهده‌نشده `NOT_OBSERVED` گزارش می‌شود (هرگز Active/Inactive فرض
نمی‌شود) و Availability فقط وقتی شمرده می‌شود که منبع آن را گزارش کرده باشد.

---

## 10. ماژول ۸ — Safety / Acceptance

**پیاده‌سازی:** `Probes/M8SafetyAcceptanceProbe.cs`، `Host/DiagnosticRuntime.EnrichWithSafetyEvidence`.

| Check | معنا |
|---|---|
| `M8.TRADING_DISABLED` | شواهد **Runtime** برای Trading=Disabled |
| `M8.READ_ONLY_POSTURE` | عدم فراخوانی API معاملاتی |
| `M8.SECRET_FREE_REPORT` | گزارش بدون راز |
| `M8.EVIDENCE_INTEGRITY` | یکپارچگی شواهد |
| `M8.STATIC_SAFETY_SCAN` | نتیجهٔ static scan (out-of-band) |
| `M8.UNOBSERVABLE` | فهرست موارد غیرقابل‌مشاهده |

**قاعدهٔ کلیدی:** صرف وجود `Trading=Disabled` در فایل تنظیمات **برای PASS کافی نیست**. شواهد
Runtime لازم است. `TradingDisabledRuntimeEvidence` توسط خود runtime (که هیچ کد مسیری برای فعال‌کردن
معامله ندارد) و `StaticSafetyScan` از بیرون تأمین می‌شوند. اگر نباشند → `INCONCLUSIVE` با
`TRADING_DISABLED_RUNTIME_EVIDENCE_MISSING`.

**مرجع API:** هیچ API معاملاتی فراخوانی نمی‌شود؛ صرفاً invariants خود ابزار.
**تست‌ها:** `Phase3ModuleTests.M8_*` (۴ تست).
**محدودیت:** موارد غیرقابل‌مشاهده همیشه فهرست می‌شوند (نسخهٔ Runtime میزبان، نسخهٔ provider،
aggressor side).

---

## 11. چه چیزی با API واقعی پیاده شد و چه چیزی هنوز Runtime واقعی می‌خواهد

### ۱۱.۱ پیاده‌شده و راستی‌آزمایی‌شده (offline، منطق کامل + تست)

این‌ها **منطق کامل** دارند و روی stub راستی‌آزمایی شده‌اند؛ اما «پیاده‌شده» به معنای «مقادیر
واقعی خوانده‌شده» نیست:

- طبقه‌بندی OrderId (`NULL/EMPTY/WHITESPACE/VALID`) و منطق F-05 (fail-closed).
- تشخیص Provider با string compare + رد مقدار stub/mock.
- تطابق provider حساب↔Connection.
- علل شکست Attestation (سه‌گانه) و gate سخت D-078 (هرگز auto-approve).
- جداسازی دو لایهٔ شمارندهٔ DOM و منطق validity/sync.
- منطق TradeDelta (منبع جهت، Unknown، Reset، Sequence) و تحلیل دو-Scope.
- مدل Strategy/Risk-Gate و بررسی correlation.
- منطق M8 (Trading-disabled فقط با شواهد Runtime).
- اسکلت snapshot میزبان (`Nt8HostDataProvider.GetSnapshot()`) که **فقط** از API عمومی می‌خواند.

### ۱۱.۲ پیاده‌شده اما **نیازمند Runtime واقعی NT8** برای اثبات مقادیر

- **مقدار واقعی Provider** از `ConnectOptions.Provider.ToString()` روی اتصال واقعی.
- **F-05 روی دادهٔ واقعی حساب**: snapshotهای `Account.Orders`، جفت‌شدن OrderId↔Execution،
  نگه‌داشت حالت پایانی.
- **Attestation واقعی**: `Instrument.MasterInstrument.*` بعد از `DataLoaded`/`Realtime`، تطابق
  Registry، وضعیت واقعی D-078 (Owner-controlled).
- **DOM واقعی**: شمارنده‌های Host Adapter و Recorder، Native/Internal snapshot، Out-of-Bounds.
- **TradeDelta واقعی**: توزیع BUY/SELL/Unknown و صحت `SERVER_TIME_UNVERIFIED`.
- **Strategy/Risk-Gate واقعی**: candidate counts، Stage 3/4، veto reasons و correlation از
  منبع فقط‌خواندنی BOT.
- **سازگاری میزبان**: بیلد `UseStubs=false` روی Windows با اسمبلی‌های واقعی NT8.

### ۱۱.۳ هرگز قابل‌مشاهده با API عمومی (همیشه INCONCLUSIVE / Unobservable)

- نسخهٔ دقیق Runtime میزبان NT8.
- نسخهٔ build provider (Rithmic).
- aggressor side معاملات از Level I.
- عمق کامل order book فراتر از آنچه `MarketDepth` می‌دهد.

### ۱۱.۴ قابلیت‌های باقی‌مانده تا فاز ۴ (پیشنهادی، خارج از دامنهٔ این فاز)

- قرارداد فقط‌خواندنی `IReadOnlyDiagnosticSource` در سمت BOT برای M7 (بدون تغییر BOT در این فاز).
- اجرای بیلد `UseStubs=false` روی میزبان Windows/NT8 و ثبت مقادیر واقعی بخش ۱۱.۲.

---

## 12. ایمنی (بدون تغییر نسبت به فاز ۲)

- فقط‌خواندنی؛ هیچ API ارسال/تغییر/لغو سفارش فراخوانی نمی‌شود.
- هیچ حساب/Connection/Position/تنظیم RiskPilot تغییر نمی‌کند.
- بدون Reflection، بدون `unsafe`، بدون interop بومی، بدون API مستندنشده.
- Trading غیرفعال است؛ هیچ مسیر کدی آن را فعال نمی‌کند.
- دادهٔ گمشده هرگز `PASS` نمی‌شود.
- بدون شناسهٔ حساب/راز در گزارش یا لاگ (`SecretRedactor` / `SyntheticRef`).
- مصنوعات گزارش **بیرون** از درخت Git نوشته می‌شوند.

این موارد توسط `verification/addon-smoke/test_diagnostic_safety.py` اجرا می‌شوند (PASS).

---

## 13. تست‌ها و نتیجهٔ هر ماژول

| ماژول | فایل تست | تعداد تست | نتیجه |
|---|---|---|---|
| M1 | `Phase3ModuleTests.M1_*` (4) + `ProbeTests.M1_*` (4) | 8 | PASS |
| M2 | `Phase3ModuleTests.M2_*` (3) + `ProbeTests.M2_*` (3) | 6 | PASS |
| M3 | `Phase3ModuleTests.M3_*` | 5 | PASS |
| M4 | `Phase3ModuleTests.M4_*` (4) + `InstrumentIdentityTests` (18) + `HostInstrumentIdentityTests` (10) | 32 | PASS |
| M5 | `Phase3ModuleTests.M5_*` | 2 | PASS |
| M6 | `Phase3ModuleTests.M6_*` | 4 | PASS |
| M7 | `Phase3ModuleTests.M7_*` (10) + `M7StrategyTests` (16) | 26 | PASS |
| M8 | `Phase3ModuleTests.M8_*` | 4 | PASS |
| Runtime integration | `Phase3ModuleTests.Runtime_*` | 2 | PASS |
| Counter/assembler | `Phase3ModuleTests.M6Consumer_*`, `SnapshotAssembler_*` | 2 | PASS |
| Generic fail-closed | `ProbeTests.Probes_NeverReturnPassWithoutObservation`, `Phase3ModuleTests.AllPhase3Probes_NeverPassWithoutEvidence` | 2 | PASS |
| **مجموع کل سوئیت** | | **149** | **PASS** |

Static safety scan: **PASS** (75 فایل، 34 الگو).

---

## 14. Git

- تغییرات **فقط** در `Ai00120/RiskPilot.BOOT`.
- هیچ تغییری در `Ai00120/RiskPilot.BOT` انجام نشد.
- یک commit مستقل فاز ۳ (بخش Commit در PR/تاریخچه).
