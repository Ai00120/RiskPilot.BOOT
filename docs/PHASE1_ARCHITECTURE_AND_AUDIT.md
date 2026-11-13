# RP-DIAG-01 — PHASE 1: REPOSITORY AUDIT & ARCHITECTURE

**Product (planned):** RiskPilot NT8 Diagnostic & Acceptance AddOn
**Proposed display name:** "RiskPilot NT8 Diagnostic Center"
**Proposed namespace:** `RiskPilot.Diagnostics.Nt8`
**Document type:** Phase 1 audit + architecture (no product code in this phase)
**Prepared:** 2026-11-12
**Safety invariant carried forward:** `Trading=Disabled` — read-only, no order/execution/account mutation.

> این سند بر پایهٔ **سورس واقعی** `Ai00120/RiskPilot.BOT` و **APIهای رسمی مستند** NinjaTrader
> (`https://docs.ninjatrader.com/ninjascript/...`) نوشته شده است. هیچ ادعای معماری‌ای بر پایهٔ حدس
> بنا نشده است. هر جا اطلاعات از API عمومی قابل دریافت نیست، صریحاً `Unobservable` علامت خورده است.

---

## 0. خلاصهٔ اجرایی (Executive summary)

- **مخزن BOT** (`Ai00120/RiskPilot.BOT`) فعال و خصوصی است؛ `main` در HEAD زیر قرار دارد:
  `46571d386c3e9fb8c8c8fa37817631bb075b8a7c` (Merge PR #275، 2026-11-11T19:48:57Z).
- **مخزن BOOT** (`Ai00120/RiskPilot.BOOT`) **کاملاً خالی** است (بدون هیچ commit؛ ساختهٔ 2026-11-04).
  این مخزن میزبان توسعهٔ مستقل ابزار تشخیصی است و در این فاز با همین سند مقداردهی اولیه می‌شود.
- ابزار جدید یک **AddOn مستقل، فقط‌خواندنی** است که **هیچ وابستگی اجرایی** به منطق معاملاتی
  RiskPilot.BOT ندارد. تنها رابطهٔ مجاز با BOT: **مطالعهٔ سورس** و در صورت لزوم، مصرف **قرارداد
  تشخیصی فقط‌خواندنی** (توسط خود BOT افشا شود) یا **لاگ/تلمتری موجود**. هیچ نوشتنی در BOT مجاز نیست.
- **سازگاری نسخه:** هدف‌گیری روی **NinjaTrader 8.1.9.0** (نسخهٔ هدف اعلام‌شده) با **حداقل پشتیبانی‌شدهٔ
  8.1.8.4** و **.NET Framework 4.8** و **C# 8.0**. هیچ build میزبان در زمان اجرا راستی‌آزمایی نشده است
  (کتابخانه‌های NT8 در محیط آفلاین موجود نیستند). بر پایهٔ Release Notes رسمی، نسخهٔ **8.1.9 در 2026-11-10**
  منتشر شده است؛ فقط «وجود شاخهٔ 8.1.9» قابل‌اثبات است (بخش ۵). اعلام نسخهٔ هدف در داکیومنت، سازگاری را
  اثبات‌شده نمی‌کند و ابزار هم آن را هرگز `PASS` گزارش نمی‌کند.
- **حالت‌های نتیجهٔ ابزار** فقط: `PASS / FAIL / INCONCLUSIVE / NOT_APPLICABLE`. دادهٔ گمشده هرگز `PASS`
  نمی‌شود (fail-closed).
- **خطر اصلی:** عدم‌تطابق نسخهٔ اسمبلی‌های NT8 با stubها؛ پراکندگی thread (Dispatcher)؛ نبود API عمومی
  برای برخی داده‌ها (مثل نسخهٔ Runtime دقیق، aggressor side، عمق کامل book، نسخهٔ provider).

---

## 1. وضعیت و Commit هر دو مخزن

### 1.1 روش راستی‌آزمایی
بررسی مستقیم از GitHub API (بدون فرض‌پذیری):

```
GET /repos/Ai00120/RiskPilot.BOT
GET /repos/Ai00120/RiskPilot.BOT/commits/main
GET /repos/Ai00120/RiskPilot.BOOT
GET /repos/Ai00120/RiskPilot.BOOT/contents/
```

### 1.2 RiskPilot.BOT (مخزن مرجع، فقط‌خواندنی برای ما)

| فیلد | مقدار (مشاهده‌شده) |
|---|---|
| full_name | `Ai00120/RiskPilot.BOT` |
| visibility | **private** |
| default_branch | `main` |
| created_at | 2026-10-17T09:19:00Z |
| pushed_at | 2026-11-11T19:48:57Z |
| size | ~3774 KB |
| **main HEAD SHA** | **`46571d386c3e9fb8c8c8fa37817631bb075b8a7c`** |
| HEAD commit | `Merge pull request #275 from Ai00120/fix/nt8-provider-rithmic-compile` |
| HEAD date | 2026-11-11T19:48:57Z |
| commit قبل از HEAD | `83f317fcc401` — `fix(nt8): avoid unavailable Provider.Rithmic enum member` |

> **نکتهٔ مهم شواهد:** کلون کاری shallow است (`is-shallow-repository = true`، یک commit grafted).
> برای blame/merge-base تاریخی باید `git fetch --unshallow` اجرا شود. این سند به سورس HEAD متکی است، نه تاریخ.

**دسترسی:** توکن فعلی روی BOT فقط خواندن دارد (`permissions.push = false`). این با الزام «سورس BOT را فقط
بخوان» هم‌راستاست و تصادفی نیست.

### 1.3 RiskPilot.BOOT (مخزن مقصد، محل توسعهٔ ابزار)

| فیلد | مقدار (مشاهده‌شده) |
|---|---|
| full_name | `Ai00120/RiskPilot.BOOT` |
| visibility | **public** |
| default_branch | `main` |
| created_at | 2026-11-04T19:12:33Z |
| pushed_at | 2026-11-04T19:12:34Z |
| size | 0 KB |
| **وضعیت** | **EMPTY — «This repository is empty» / «Git Repository is empty»** |
| توکن فعلی | `admin: true` (اجازهٔ push) |

**نتیجه:** مخزن BOOT خالی است و این سند نخستین محتوای آن خواهد بود. فرض «مخزن خالی است» **راستی‌آزمایی شد**،
نه مفروض.

---

## 2. نقشهٔ معماری RiskPilot.BOT (بر پایهٔ سورس واقعی)

### 2.1 ساختار پروژه‌ها

- یک پروژهٔ تولیدی: `src/RiskPilot.BOT/RiskPilot.BOT.csproj` با `EnableDefaultCompileItems=false`
  (فایل‌ها صریحاً لیست شده‌اند — برای هر فایل `.cs` جدید باید به csproj افزوده شود).
- یک پروژهٔ تست: `tests/RiskPilot.BOT.Tests/` (xUnit، `net8.0`، ~1447 `[Fact]/[Theory]` در 108 فایل).
- دو حالت build:
  - `-p:UseStubs=true` → `net8.0` + `Stubs/NinjaTraderStubs.cs` + `USE_STUBS` (CI/آفلاین).
  - `-p:UseStubs=false` → `net48` + ارجاع به اسمبلی‌های واقعی NT8 (`NinjaTrader.Core.dll`, `NinjaTrader.Gui.dll`).
- `verification/` شامل: `addon-smoke/test_addon_safety.py` (اسکن ایستای APIهای ممنوعه)،
  `data-audit/`، `nt8-account-api/` (probeهای compile-only برای امضای API)، `governance/`.

### 2.2 لایه‌ها و ماژول‌های دامنه

```
NinjaTrader.NinjaScript.AddOns   (AddOns/)        ← لایهٔ میزبان NT8 (فقط این لایه به APIهای NT8 دست می‌زند)
  RiskPilotBotAddOn.cs            (4503 خط)  — کلاس اصلی AddOnBase؛ lifecycle، اتصال، ثبت، orchestration
  RiskPilotBotStatus.cs                       — enumهای وضعیت fail-closed (lifecycle/connection/marketdata/trading)
  ObserverConsoleWindow.cs        (2581 خط)  — NTWindow بومی + تب‌ها (فقط در UseStubs=false)
  ObserverConsoleWindowManager.cs             — مدیریت single-instance پنجره + Dispatcher marshaling
  ObserverConsoleRefreshCoordinator.cs        — DispatcherTimer کم‌اولویت (۱ ثانیه) برای refresh
  Nt8HostInstrumentMetadataProvider.cs        — تنها خوانندهٔ متادیتای Instrument برای attestation
  Nt8SimAccountHostReader.cs / Nt8SimAccountAdapterFactory.cs   — خواندن فقط‌خواندنی حساب SIM
  Nt8SimPortfolioHostReader.cs / Nt8SimPortfolioProviderFactory.cs — Portfolio Provider فقط‌خواندنی (D-079)
  RecordingSnapshotFactory.cs / StrategyMetadataSnapshotFactory.cs

RiskPilot.BOT.Domain.*            (Domain/)       ← دامنهٔ خالص؛ بدون وابستگی به نوع‌های NT8
  MarketData/   — TickQuote, TickTrade, BarM1, DomDepth, adapterها, InstrumentResolver, Metadata
  Quality/      — MarketDataQualityEngine, freshness, StreamReadiness, BoundedLogAggregator, metrics
  Recording/    — envelope/manifest codec, MarketDataRecorder (bounded, append-only), reader/validator, replay
  Context/      — MarketContextEngine (past-only), MarketSessionPolicy
  Levels/       — MarketLevelRegistry/Descriptor/SourceReference (provenance-aware)
  OrderFlow/    — OrderFlowEngine, DomLadderReducer, TradePrintAggregator, TradeDelta/VolumeProfile/VWAP
  Strategy/     — Stage 1..4: CandidateValidator, CandidateSelectionEngine, AnalyticalTradingDecisionEngine,
                  PositionSizingEngine, RiskGateEngine + 5 استراتژی تحقیقاتی + مدل‌های SIM/Portfolio/RiskPolicy
  Evaluation/   — OfflineEvaluationHarness, PnLSimulationEngine, ForwardShadowEvaluationEngine
  Instruments/  — CmeEsDec26Registry (immutable), InstrumentAttestationEngine, HostAttestationCoordinator, D-078
  Alerts/       — CandidateSignalAlert, sinkها, deduplicator, formatter
  Presentation/ — snapshotهای فقط‌خواندنی برای UI
  Configuration/— اعتبارسنجی پیکربندی استراتژی (versioned, fail-closed)
```

### 2.3 مکانیزم‌های کلیدی (بر پایهٔ کد)

| مکانیزم | محل پیاده‌سازی | خلاصهٔ واقعی |
|---|---|---|
| **Trading Disabled** | `RiskPilotBotStatus.cs` → `TradingPermissionState.Disabled`؛ فیلد `readonly` در AddOn | هیچ setter یا مسیر فعال‌سازی وجود ندارد؛ در تمام لاگ‌ها `Trading=Disabled` چاپ می‌شود. |
| **Lifecycle** | `RiskPilotBotAddOn.OnStateChange()` | فقط `SetDefaults` / `Active` / `Terminated`. `SafeInitialize`/`SafeShutdown` با guard exactly-once. |
| **Connection tracking** | `SubscribeConnectionStatus()` + `OnConnectionStatusUpdate` | تفکیک Price/OrderFeed/Broker؛ تطبیق اتصال با `Options.Name` حاوی "Rithmic" (بعد از آن exact-match با نام انتخاب‌شده). |
| **Provider identity** | `HasOnlyConnectedRithmicProvider()` و factoryها | تشخیص **رشته‌ای** `Options.Provider.ToString() == "Rithmic"` (پس از PR #275، به‌خاطر نبود عضو enum). |
| **Instrument attestation (D-078/D-096)** | `HostAttestationCoordinator` + `InstrumentAttestationEngine` + `Nt8HostInstrumentMetadataProvider` | Invalidate-before-verify؛ epoch؛ registry فقط CME ES؛ نگاشت مسیر Globex→CME فقط با شروط D-096؛ D-078 پیش‌فرض NotApproved (fail-closed). |
| **DOM / snapshot sync** | `DomMarketDataAdapter` | آینهٔ bounded (MaxDepth=10)؛ در out-of-bounds، sync از snapshot بومی NT8 با شمارندهٔ trigger (`InsertOob/UpdateOob/RemoveOob/Subscription`). |
| **TradeDelta / OrderFlow** | `OrderFlowEngine`, `TradePrintAggregator`, `Level1MarketDataAdapter` | هر `RESET` → `OrderFlowStatus=Unavailable`؛ جهت معامله از BBO مشتق می‌شود (`DerivedFromQuote`)؛ دلایل نامشخص: `NoBboAvailable`, `BboIncomplete`, `InsideSpread`, ... |
| **Stage 1..4** | `CandidateValidator` → `CandidateSelectionEngine`/`AnalyticalTradingDecisionEngine` → `PositionSizingEngine` → `RiskGateEngine` | همه در AddOn سیم‌کشی شده‌اند؛ کدهای دلیل مانند `ALL_CANDIDATES_INVALID`, `ANALYTICAL_NO_TRADE`, `MISSING_INSTRUMENT_SPECIFICATION`. |
| **Recording** | `MarketDataRecorder` + envelope/manifest | فایل‌های `segment_%06d.ndjson` + `manifest.json`؛ `SessionId = rec_yyyyMMdd_HHmmss_<guid>`؛ شمارنده‌های Enqueued/Written/Dropped/Truncated. |
| **Source-time trust** | `SourceTimeInterpretation.ServerTimeUnverified` | پیش‌فرض همهٔ رکوردها؛ در codec به رشتهٔ `"SERVER_TIME_UNVERIFIED"` نگاشت می‌شود. |
| **Static safety** | `verification/addon-smoke/test_addon_safety.py` | الگوهای ممنوعه (SubmitOrder/EnterLong/.../OrderAction/OrderState/Connection.Connect) + allowlist فایل‌های حساب. |

### 2.4 D-078 و D-096 در یک نگاه

- **D-078** = تأیید میدانی اپراتور. `D078FieldApprovalRegistry` **عمداً خالی** است؛ تا یک PASS واقعی روی میزبان
  ثبت نشود، gate همیشه `NotApproved` با کد `D078_FIELD_APPROVAL_NOT_APPROVED` برمی‌گرداند.
- **D-096** = نگاشت مسیر پیکربندی‌شدهٔ Globex→CME برای ES، فقط زمانی که `Instrument.Exchange==Globex` و
  `MasterInstrument.Exchanges == {Globex}` و تنها مسیر متصل Rithmic باشد. این **جانشین D-078 نمی‌شود**.
- در شواهد ثبت‌شدهٔ شما، Attestation با `State=Failed` و
  `Reasons=HOST_UNAVAILABLE` + `D078_FIELD_APPROVAL_NOT_APPROVED` ظاهر شده — این **رفتار fail-closed طراحی‌شده**
  است، نه یک باگ؛ و دلیل ریشه‌ای آن دقیقاً همان چیزی است که ابزار جدید باید بتواند «مشاهده‌پذیر» کند.

### 2.5 پنج استراتژی فعال (نام‌های واقعی سورس)

`STRAT_BREAKOUT_CONTINUATION_01`، `STRAT_TREND_PULLBACK_01`، `STRAT_LEVEL_AWARE_REVERSAL_01`،
`STRAT_SCALPING_CANDIDATE_01`، `STRAT_ORDER_FLOW_INTRADAY_01`.

---

## 3. API Compatibility Matrix (NT8 عمومی و مستند)

مرجع: `https://docs.ninjatrader.com/ninjascript/*` (سایت مستندات رسمی NinjaScript). ستون «استفاده در BOT» از
سورس HEAD استخراج شده. ستون «قابلیت مشاهده» برای ماژول‌های تشخیصی ابزار است.

| # | API / نوع | امضا/عضو (مستند) | استفادهٔ BOT | ماژول تشخیصی | قابلیت مشاهده |
|---|---|---|---|---|---|
| 1 | `NinjaTrader.NinjaScript.AddOnBase` | کلاس پایهٔ AddOn؛ `OnStateChange`, `OnWindowCreated`, `OnWindowDestroyed`, `Log` | بله | همه | ✅ |
| 2 | `State` enum | `SetDefaults, Configure, Active, DataLoaded, Historical, Transition, Realtime, Terminated` | بله (SetDefaults/Active/Terminated) | M1 | ✅ |
| 3 | `NinjaTrader.Cbi.Connection` (static) | `Connections`, `ConnectionStatusUpdate`, `Connect()`, `CancelAllOrders()` | `Connections`, `ConnectionStatusUpdate` | M2 | ✅ |
| 4 | `Connection` instance | `Options`, `Status`, `PriceStatus`, `Accounts`, `Disconnect()` | `Options`, `Status`, `PriceStatus` | M2 | ✅ |
| 5 | `ConnectOptions` | `Name`, `Provider`, `Mode`, `BrandName`, `CanManageOrders`, ... | `Name`, `Provider`, `Mode` | M2 | ✅ |
| 6 | `ConnectionStatusEventArgs` | `Connection`, `Status`, `PriceStatus` | بله | M2 | ✅ |
| 7 | `NinjaTrader.Cbi.Account` (static) | `All`, `AccountStatusUpdate`, `SimulationAccountReset` | `All`, `AccountStatusUpdate` | M3 | ✅ |
| 8 | `Account` instance | `Name`, `Provider`, `Connection`, `Denomination`, `Get(AccountItem, Currency)`, `Positions`, `Orders`, `Executions`, `AccountItemUpdate`, `OrderUpdate`, `PositionUpdate`, `ExecutionUpdate`, `Strategies` | زیرمجموعهٔ allowlist‌شده | M3 | ✅ (زیرمجموعه) |
| 9 | `AccountItem` | `NetLiquidation`, `RealizedProfitLoss`, `UnrealizedProfitLoss`, `CashValue`, `BuyingPower`, `InitialMargin`, ... | فقط `NetLiquidation` (D-079) | M3 | ⚠️ محدود به allowlist |
| 10 | `AccountItemEventArgs` | `Account`, `AccountItem`, `Currency`, `Value`, `Time` | بله | M3 | ✅ |
| 11 | `AccountStatusEventArgs` | `Account`, `Status` | بله | M3 | ✅ |
| 12 | `NinjaTrader.Cbi.Order` | `OrderId`, `OrderAction`, `OrderType`, `OrderState`, `Quantity`, `LimitPrice`, `StopPrice`, `Name`, `Oco`, `Instrument`, `Account`, `Time`, ... | خواندن فقط برای طبقه‌بندی stop | M3 | ✅ (read-only) |
| 13 | `NinjaTrader.Cbi.Position` | `Instrument`, `MarketPosition`, `Quantity`, `AveragePrice`, `Account`, `GetUnrealizedProfitLoss()` | خواندن فقط | M3 | ✅ |
| 14 | `NinjaTrader.Cbi.Execution` | `ExecutionId`, `OrderId`, `Price`, `Quantity`, `MarketPosition`, `Time`, `Order`, `Account` | **استفاده نشده** | M3 | ⚠️ نیازمند تصمیم Owner |
| 15 | `NinjaTrader.Cbi.Instrument` | `FullName`, `Expiry`, `Exchange`, `MasterInstrument`, `MarketData`, `MarketDepth`, `Dispatcher`, `GetInstrument(string)` | بله | M4/M5 | ✅ |
| 16 | `MasterInstrument` | `Name`, `InstrumentType`, `Exchanges`, `Currency`, `PointValue`, `TickSize`, `Description`, `Url`, `GetNextExpiry()` | زیرمجموعه | M4 | ✅ (پس از DataLoaded) |
| 17 | `InstrumentType` | `Future, Stock, Index, Forex, Cfd, Cryptocurrency` | بله | M4 | ✅ |
| 18 | `NinjaTrader.Data.MarketData` | `Bid, Ask, Last, DailyHigh/Low/Volume, Opening, Settlement, OpenInterest, Update` | `Update` | M5 | ✅ |
| 19 | `MarketDataEventArgs` | `Instrument, MarketDataType, Price, Time, Volume, Bid, Ask, IsReset` | بله | M5 | ✅ |
| 20 | `NinjaTrader.Data.MarketDepth` | `Bids, Asks, Instrument, Update` | `Update`, `Bids/Asks` | M5 | ✅ |
| 21 | `MarketDepthEventArgs` | `Instrument, MarketDataType, Operation, Position, Price, Volume, Time, IsReset` | بله | M5 | ✅ |
| 22 | `NinjaTrader.Data.BarsRequest` | ctor(Instrument,int)/(Instrument,DateTime,DateTime); `BarsPeriod`, `TradingHours`, `Update`, `Request(Action<...>)`, `Dispose()` | بله | M5 | ✅ |
| 23 | `Bars` | `Count`, `GetOpen/High/Low/Close/Time/Volume(i)` | بله | M5 | ✅ |
| 24 | `NinjaTrader.Code.Output.Process(string, PrintTo)` | خروجی به Output window | بله | M8 | ✅ |
| 25 | `Log(string, LogLevel)` | لاگ Control Center | بله | M8 | ✅ |
| 26 | `NinjaTrader.Gui.Tools.NTWindow` | `Caption`, `Content`, `IWorkspacePersistence` | بله | M8 | ✅ |
| 27 | `NTTabPage`, `TabControl`, `TabControlManager` | میزبان تب‌ها + link | بله | M8 | ✅ |
| 28 | `NTMenuItem`, `ControlCenter.FindFirst(id)` | افزودن آیتم منو | بله | M8 | ✅ |
| 29 | `System.Windows.Threading.Dispatcher` | `InvokeAsync`, `CheckAccess`, `HasShutdownStarted` | بله | همه | ✅ |
| 30 | `NinjaTrader.Cbi.Currency` | enum ارزها | بله | M3/M4 | ✅ |
| 31 | `NinjaTrader.Cbi.Exchange` | enum صرافی‌ها | بله | M4 | ✅ |
| 32 | **نسخهٔ دقیق Runtime NT8** | — | — | M1 | ❌ **Unobservable** (در داکیومنت عمومی APIای برای آن وجود ندارد) |
| 33 | **aggressor side هر print** | — | BOT مشتق می‌کند | M6 | ❌ **Unobservable مستقیم** (از Level I قابل دریافت نیست) |
| 34 | **عمق کامل book فراتر از snapshot بومی** | — | bounded در BOT | M5 | ⚠️ فقط به اندازهٔ `MarketDepth.Bids/Asks` |
| 35 | **نسخهٔ Provider (Rithmic build)** | — | — | M2 | ❌ **Unobservable** (فقط نام provider) |
| 36 | **حالت Trading پرتفوی/اکانت (جهانی)** | — | — | M7/M8 | ❌ API عمومی مستقیم ندارد؛ فقط غیرمستقیم از پیکربندی |

**نکات سازگاری API:**
- `MasterInstrument.*` و `Instrument.*` (به‌جز `FullName`/`Expiry`) نباید پیش از `State.DataLoaded` خوانده شوند
  (هشدار رسمی داکیومنت). BOT هم همین را رعایت می‌کند.
- `Connection.Connections` و `Account.All` باید داخل `lock` پیمایش شوند (نمونهٔ رسمی).
- اشتراک/لغو `MarketDepth.Update` باید روی **همان thread** انجام شود؛ روش توصیه‌شدهٔ رسمی: `instrument.Dispatcher.InvokeAsync`.
- `Provider` هیچ عضو `Rithmic` تضمین‌شده‌ای ندارد؛ PR #275 دقیقاً به همین دلیل به مقایسهٔ رشته‌ای روی‌آورده است.
  **ابزار جدید هم باید همین الگو را حفظ کند** و از `Provider.Rithmic` نام‌محور استفاده نکند.

---

## 4. Data Observability Matrix

هر دادهٔ موردنیاز ابزار با منبع قابل دریافت و وضعیت قابلیت مشاهده مشخص می‌شود. منابع ممکن:
**(A)** NinjaTrader Public API، **(B)** RiskPilot Read-only Diagnostic Interface، **(C)** Log/Telemetry موجود،
**(D)** غیرقابل مشاهده (Unobservable).

| داده | منبع | جزئیات دقیق | وضعیت |
|---|---|---|---|
| نسخهٔ AddOn/BuildId BOT | C | `ProductVersion=1.0.4-alert-metrics`, `BuildId=alert-hysteresis-metrics-nt8184` در لاگ | ✅ مشاهده‌پذیر |
| نسخهٔ میزبان NT8 | D | هیچ API عمومی نسخه نمی‌دهد؛ فقط از مسیر نصب/فایل نسخه/Help > About (خارج از API) | ❌ Unobservable از API |
| نام و نوع اتصال (Provider) | A/C | `Connection.Options.Name`, `Options.Provider.ToString()`, `Options.Mode` | ✅ |
| وضعیت Price/Order/Broker | A | `Connection.PriceStatus`, `Connection.Status` + رویداد `ConnectionStatusUpdate` | ✅ |
| شمارش Reconnect | B/C | BOT شمارنده دارد و در heartbeat چاپ می‌کند | ✅ (اگر BOT expose کند) |
| هویت اکانت (نام/ارز/Provider) | A | `Account.Name`, `Account.Denomination`, `Account.Provider` — **رعایت privacy D-077/D-095** | ⚠️ فقط بدون درج شناسه در لاگ |
| NetLiquidation | A | `Account.Get(AccountItem.NetLiquidation, Denomination)` | ⚠️ فقط طبق allowlist D-079 |
| Positions/Orders (read-only) | A | `Account.Positions`, `Account.Orders` | ✅ read-only |
| Executions | A | `Account.Executions` | ⚠️ نیازمند تأیید صریح Owner (خارج از allowlist فعلی BOT) |
| OrderId / همبستگی Order↔Execution↔Position | A | `Order.OrderId` (غیرپایدار)، `Execution.ExecutionId` | ⚠️ ناپایدار؛ F-05 را باید صریحاً `INCONCLUSIVE` در نظر گرفت اگر همبستگی کامل نباشد |
| متادیتای Instrument (root/type/exchange/currency/tick/point) | A | `Instrument.FullName/Expiry/Exchange`, `MasterInstrument.*` | ✅ (پس از DataLoaded) |
| تأییدیهٔ venue/attestation | B/C | `InstrumentAttestationState` + reason codes از BOT | ✅ اگر BOT expose کند |
| D-078 approval state | B | `D078FieldApprovalRegistry` همیشه NotApproved در production | ✅ (به‌عنوان NotApproved) |
| Level I (Bid/Ask/Last) | A | `Instrument.MarketData.Update` + `MarketDataEventArgs` | ✅ |
| DOM depth (snapshot + delta) | A | `Instrument.MarketDepth.Update` + `MarketDepthEventArgs` | ✅ (عمق محدود) |
| DOM RESET frequency + snapshot-sync triggers | A/B | `Operation` + شمارنده‌های BOT (`SnapshotSync*`) | ✅ (BOT بهتر expose می‌کند) |
| M1 Bars | A | `BarsRequest` + `Bars` | ✅ |
| TradeDelta / OrderFlow status | B/C | `OrderFlowStatus` (Available/Unavailable) + reason | ✅ (اگر BOT expose کند) |
| جهت معامله (Buy/Sell/نامشخص) | B/C | BOT مشتق می‌کند؛ دلایل در `TradeDirectionUnknownReason` | ⚠️ مشتق، نه منبع؛ ابزار باید منبع را «مشتق» برچسب بزند |
| شمارش استراتژی‌های فعال + reason | B/C | `ActiveStrategies`, reason codes | ✅ |
| Risk Gate verdicts + reason codes | B/C | `RiskEvaluationResult`, `RiskReasonCode.*` | ✅ |
| Trading Disabled state | B/C | `TradingPermissionState.Disabled` در همهٔ لاگ‌ها | ✅ |
| Source-time trust | A/B | `SourceTimeInterpretation.ServerTimeUnverified` | ✅ |
| نسخهٔ دقیق Runtime provider (Rithmic) | D | — | ❌ Unobservable |
| حالت Trading سطح پرتفوی (خارج از BOT) | D | — | ❌ Unobservable مستقیم |

**قاعدهٔ طراحی:** هر داده‌ای که منبع آن `D` است، در گزارش ابزار با `INCONCLUSIVE` یا `Unobservable`
برچسب می‌خورد و **هرگز** به‌عنوان `PASS` گزارش نمی‌شود.

---

## 5. سازگاری نسخهٔ NT8 و .NET Framework

### 5.1 واقعیت‌های راستی‌آزمایی‌شده (Release Notes رسمی NT8)

جدول Release Notes رسمی (`ninjatrader.com/support/helpGuides/nt8/release_notes.htm`) — **مشاهده‌شده**:

| نسخه | تاریخ انتشار |
|---|---|
| 8.1.9 | **November 10, 2026** |
| 8.1.8 | July 21, 2026 |
| 8.1.7 | May 13, 2026 |
| 8.1.6 | September 25, 2025 |
| 8.1.5 | May 28, 2025 |
| 8.1.4 | December 17, 2024 |

**نتیجه‌گیری:**
- ادعای «NT8 نسخهٔ 8.1.9.0» از گزارش طراحی قبلی: نسخهٔ **8.1.9** در 2026-11-10 منتشر شده و **واقعی** است،
  اما build دقیق `8.1.9.0` از داکیومنت عمومی تأیید نمی‌شود. → `Clue`، نه `Verified`.
- میزبان ثبت‌شدهٔ پروژه: **8.1.8.4** (نسخهٔ 8.1.8 در 2026-07-21 منتشر شده). BOT برای همین نسخه هدف‌گیری شده.

### 5.2 پشتهٔ هدف

| مؤلفه | مقدار |
|---|---|
| NT8 Host (هدف) | **8.1.9.0** (64-bit) — نسخهٔ هدف اعلام‌شده؛ **حداقل پشتیبانی‌شده 8.1.8.4**؛ هیچ build در زمان اجرا **راستی‌آزمایی نشده** |
| Target Framework | `net48` (.NET Framework 4.8) — طبق csproj و داکیومنت رسمی AddOn |
| Language | C# 8.0 |
| WPF | `PresentationCore/Framework`, `WindowsBase` (برای `NTWindow`) |
| اسمبلی‌های ارجاع | `NinjaTrader.Core.dll`, `NinjaTrader.Gui.dll` |

### 5.3 ریسک‌های سازگاری
1. **Stub drift:** تست‌های آفلاین روی `net8.0` با stub اجرا می‌شوند و **اثبات سازگاری واقعی نیستند**. خود BOT
   در `docs/NT8_REAL_COMPILE_PROCEDURE.md` هشدار می‌دهد که real-assembly drift ممکن است (نمونهٔ تاریخی:
   `ConnectOptions` vs `ConnectionOptions`). → ابزار جدید هم باید **الزام compile روی NT8 واقعی** داشته باشد.
2. **تفاوت 8.1.8 vs 8.1.9:** تغییرات رخ‌داده در 8.1.9 بررسی نشده؛ ابزار باید نسخه‌محور fail-closed عمل کند.
3. **نبود ICU/globalization در sandbox آفلاین:** تست‌های culture در این محیط شکست می‌خورند (محیطی، نه کدی).

### 5.4 به‌روزرسانی (Prompt 10 — هم‌ترازی نسخه)

نسخهٔ هدف اعلام‌شده به **NinjaTrader 8.1.9.0** هم‌تراز شد (حداقل پشتیبانی‌شده **8.1.8.4**). این یک
هم‌ترازی **داکیومنت و اعلان** است، نه اثبات سازگاری:

- نسخهٔ هدف، حداقل و نسخهٔ تست‌شده در یک منبع واحد نگه‌داری می‌شوند:
  `src/RiskPilot.Diagnostics.Nt8/Core/Identity/NinjaTraderCompatibility.cs`.
- فهرست نسخه‌های **تست‌شده** خالی است: هیچ build میزبان در زمان اجرا اجرا نشده است. بنابراین
  `CompatibilityVerifiedAtRuntime == false` و `M1.HOST_COMPATIBILITY` همیشه `INCONCLUSIVE` است (هرگز `PASS`).
- «میزبان ثبت‌شدهٔ پروژه: 8.1.8.4» در بخش ۵.۱ یک **واقعیت تاریخی** (آنچه BOT برای آن هدف‌گیری شده بود) است و
  بازنویسی نشده؛ آنچه تغییر کرده، نسخهٔ **هدف اعلام‌شدهٔ** این ابزار است.
- سازگاری واقعی بدون اسمبلی‌های نصب‌شدهٔ NinjaTrader قابل راستی‌آزمایی نیست؛ تا زمان compile/اجرا روی میزبان
  واقعی، هیچ ادعای سازگاری مطرح نمی‌شود.

---

## 6. معماری پیشنهادی AddOn: "RiskPilot NT8 Diagnostic Center"

### 6.1 اصول طراحی (غیرقابل مذاکره)
- فقط‌خواندنی؛ هیچ Order/Execution/Position/Account mutation.
- فقط API عمومی مستند NT8؛ بدون Reflection، بدون دسترسی private، بدون `unsafe`.
- عدم وابستگی اجرایی به منطق معاملاتی BOT (اتصال فقط از طریق قرارداد تشخیصی/لاگ/تلمتری).
- Fail-closed: دادهٔ گمشده = `INCONCLUSIVE` یا `NOT_APPLICABLE`، هرگز `PASS`.
- تفکیک thread با `Dispatcher.InvokeAsync`؛ عدم استفاده از `Dispatcher.Invoke` همگام.

### 6.2 لایه‌بندی (پیشنهادی)

```
RiskPilot.Diagnostics.Nt8
├── Host/                     ← تنها لایه‌ای که به APIهای NT8 دست می‌زند
│   ├── DiagnosticAddOn.cs            : AddOnBase  (lifecycle: SetDefaults/Active/Terminated)
│   ├── MenuIntegration.cs            : OnWindowCreated → آیتم منو در Control Center
│   ├── Probes/                       : یک probe به‌ازای هر ماژول (M1..M8)
│   └── HostClock.cs / DispatcherBridge.cs
├── Core/                     ← دامنهٔ خالص، بدون نوع NT8
│   ├── Result/  : DiagnosticResult { Status, Severity, Evidence[], ReasonCodes[], Source, TimestampUtc }
│   ├── Status/  : enum DiagnosticStatus { PASS, FAIL, INCONCLUSIVE, NOT_APPLICABLE }
│   ├── Contract/ : IReadOnlyDiagnosticSource (قرارداد فقط‌خواندنی اختیاری برای مصرف از BOT)
│   └── Report/  : ReportBuilder, JsonReportWriter (schema-versioned, secret-free)
├── Modules/                  ← منطق هر ماژول تشخیصی (M1..M8)
├── Ui/                       ← NTWindow + NTTabPage + TabControl (فقط UseStubs=false)
└── Diagnostics.Stubs/        ← stubهای آفلاین برای CI (خارج از build میزبان)
```

### 6.3 مدل داده (پیشنهادی)

```csharp
public enum DiagnosticStatus { PASS, FAIL, INCONCLUSIVE, NOT_APPLICABLE }

public sealed class DiagnosticEvidence {
    public string Source;        // "NinjaTrader API" | "RiskPilot DI" | "Log/Telemetry" | "Unobservable"
    public string ObservedValue; // sanitized
    public DateTime ObservedAtUtc;
    public bool IsObservable;
}

public sealed class DiagnosticResult {
    public string ModuleId;          // "M1".."M8"
    public string CheckId;           // e.g. "M4.ATTESTATION_D078"
    public DiagnosticStatus Status;
    public string[] ReasonCodes;     // reuse BOT reason codes where observable
    public IReadOnlyList<DiagnosticEvidence> Evidence;
    public string RemediationHint;   // non-operational guidance
    public DateTime EvaluatedAtUtc;
}
```

### 6.4 ماژول‌های تشخیصی

| ID | ماژول | ورودی‌ها | خروجی‌ها |
|---|---|---|---|
| M1 | Environment / API Compatibility | `State` lifecycle، ارجاع اسمبلی‌ها، حضور نوع‌های موردنیاز | سازگاری API، نسخهٔ پشته (نسخهٔ Runtime = Unobservable) |
| M2 | Connection / Provider Identity | `Connection.Connections`, `Options.Name/Provider/Mode`, `PriceStatus/Status`, `ConnectionStatusUpdate` | تشخیص provider (string-based)، تفکیک Price/Order/Broker، شمارش reconnect |
| M3 | Account / Order Lifecycle | `Account.All`, `Denomination`, `Positions`, `Orders`، (اختیاری `Executions` با تأیید) | وضعیت اکانت، همبستگی Order↔Execution↔Position، تشخیص F-05 |
| M4 | Instrument Attestation / D-078 | `Instrument.FullName/Expiry/Exchange`, `MasterInstrument.*`, attestation state از BOT | بازتولید پیش‌شرط‌های attestation و تشخیص دقیق علت fail (D-078/D-096) |
| M5 | DOM / Market Data | `MarketDepth.Update` + `MarketDataEventArgs`، `BarsRequest` | کیفیت/فراوانی RESET، snapshot-sync، عمق، gap/duplicate |
| M6 | TradeDelta / OrderFlow | OrderFlow status از BOT + داده‌های L1 | نرخ جهت نامشخص، دلایل INSIDE_SPREAD/BBO، وضعیت TradeDelta |
| M7 | Strategies / Risk Gate | reason codes و شمارنده‌های BOT | تحلیل توزیع ردها (NO_TRADE/MISSING_INSTRUMENT/...) و Stage 3 Qty=0 |
| M8 | Safety / Acceptance | کل وضعیت‌ها | تأیید Trading=Disabled، نبود secret در لاگ، تولید گزارش پذیرش |

### 6.5 مدل اجرا و Event Queue
- **Single-threaded UI** برای به‌روزرسانی UI؛ probeها روی thread رویداد NT8 اجرا می‌شوند و فقط snapshot
  غیرقابل‌تغییر منتشر می‌کنند.
- **صف رویداد bounded** (ring buffer با ظرفیت ثابت) برای هر probe؛ drop صریح شمرده می‌شود (fail-closed،
  drop = `INCONCLUSIVE` نه `PASS`).
- هیچ lock روی hot-path؛ snapshot با یک reference write منتشر می‌شود (الگوی مشابه BOT).
- Refresh UI با `DispatcherTimer` کم‌اولویت (نمونه: ۱ ثانیه) و marshaling با `Dispatcher.InvokeAsync`.

### 6.6 Lifecycle
```
SetDefaults → (Name/Description)
Active      → SafeInitialize(): ساخت probeها، اشتراک رویدادها (با Dispatcher), شروع timers
Terminated  → SafeShutdown(): لغو اشتراک، توقف timers، بستن پنجره، آزادسازی منابع (exactly-once)
```
همهٔ اشتراک‌های `MarketDepth.Update`/`MarketData.Update` **از همان thread** لغو می‌شوند (الگوی رسمی).

### 6.7 UI
- یک `NTWindow` با `Caption="RiskPilot NT8 Diagnostic Center"` و `TabControl` با یک `NTTabPage` per ماژول.
- نوار وضعیت ثابت شامل: `Trading: DISABLED`, provider، connection states، وضعیت attestation.
- عدم تغییر تنظیمات RiskPilot؛ فقط نمایش.

### 6.8 ساختار گزارش‌ها
- خروجی JSON با `schemaVersion` صریح (مثلاً `rpdiag-1.0`)، شامل: نسخهٔ ابزار، زمان UTC، هشدار عدم‌قطعیت،
  نتیجهٔ هر ماژول، evidence، reason codeها، و بخش `Unobservable`.
- **secret-free:** هیچ نام اکانت/شناسه/توکن/رمز در گزارش درج نمی‌شود.
- گزارش در مسیر خروجی خارج از Git نوشته می‌شود (پیروی از D-092).

---

## 7. ریسک‌ها، محدودیت‌ها و شکاف‌های اطلاعاتی

| # | ریسک / شکاف | نوع | اثر | کاهش ریسک |
|---|---|---|---|---|
| R1 | عدم‌تطابق stub با اسمبلی واقعی NT8 | Verified (سابقهٔ تاریخی) | build آفلاین سبز اما میزبان شکست | الزام compile واقعی روی NT8 (الگوی `NT8_REAL_COMPILE_PROCEDURE`) |
| R2 | نبود API عمومی برای نسخهٔ دقیق Runtime/provider | Verified | برخی چک‌ها `Unobservable` می‌مانند | صریحاً `INCONCLUSIVE`/`NOT_APPLICABLE` علامت بزن |
| R3 | پراکندگی thread و deadlock در Dispatcher | Verified (داکیومنت رسمی) | hang در UI/رویداد | فقط `InvokeAsync`، `CheckAccess`، لغو اشتراک روی همان thread |
| R4 | ناپایداری `Order.OrderId` | Verified (داکیومنت رسمی) | همبستگی ناقص Order↔Execution | F-05 را `INCONCLUSIVE` گزارش کن اگر همبستگی کامل نشد |
| R5 | عمق DOM محدود و RESET مکرر | Observed | OrderFlow نامعتبر | شمارندهٔ triggerها را دقیق گزارش کن؛ گیتینگ را تغییر نده |
| R6 | جهت معامله مشتق (نه منبع) | Verified | TradeDelta ناقص | منبع را «مشتق» برچسب بزن؛ نرخ نامشخص را گزارش کن |
| R7 | Privacy اکانت (D-077/D-095) | Decision | نقض حاکمیت اگر شناسه درج شود | هرگز شناسهٔ اکانت در گزارش/لاگ درج نشود |
| R8 | `Provider.Rithmic` ناموجود | Verified (PR #275) | خطای compile | مقایسهٔ رشته‌ای `ToString()` (همان الگوی BOT) |
| R9 | `MasterInstrument.*` پیش از DataLoaded | Verified (داکیومنت) | استثناء/دادهٔ نامعتبر | فقط پس از `DataLoaded`/`Active` خوانده شود |
| R10 | نبود D-078 approval واقعی | Decision | Attestation همیشه Failed | ابزار فقط «علت» را گزارش کند؛ approval نسازد |
| R11 | تفاوت 8.1.8 ↔ 8.1.9 | Open | رفتار متفاوت | چک نسخه‌محور + fail-closed |
| R12 | تعریف «Acceptance» مبهم | Open | گزارش بی‌معنا | معیار پذیرش هر ماژول در فاز ۲ تثبیت شود |

### شکاف‌های اطلاعاتی که ابزار نمی‌تواند پر کند (Unobservable)
- نسخهٔ دقیق Runtime NT8 و نسخهٔ build provider از طریق API.
- aggressor side قطعی هر print در Level I.
- عمق book فراتر از snapshot بومی `MarketDepth`.
- وضعیت Trading سطح پرتفوی/اکانت به‌صورت مستقل از پیکربندی.

---

## 8. برنامهٔ دقیق فازهای ۲ تا ۵

> قاعده: هر فاز فقط پس از تأیید صریح Owner/کاربر آغاز می‌شود. هیچ فازی خودکار پیش نمی‌رود.

### Phase 2 — Skeleton & Environment/Connection Diagnostics (M1, M2)
**پیش‌نیاز:** این سند (Phase 1) تأیید شود؛ دسترسی push به BOOT.
**کارها:**
1. اسکلت پروژه در BOOT: `DiagnosticAddOn.cs`، `DiagnosticStatus`، `DiagnosticResult`، csproj با
   `EnableDefaultCompileItems=false`، `UseStubs` dual-build.
2. M1: چک سازگاری API (حضور نوع‌ها/امضاهای موردنیاز) با نتیجهٔ `PASS/FAIL/INCONCLUSIVE`.
3. M2: تشخیص provider (رشته‌ای)، تفکیک Price/Order/Broker، شمارش reconnect.
4. `verification/` ایستا برای ممنوعیت APIهای سفارش (کپی الگوی BOT).
5. build آفلاین `-p:UseStubs=true` + تست‌های واحد دامنه.
**معیار پذیرش:** M1/M2 نتایج درست روی stub؛ بدون API ممنوعه؛ گزارش JSON معتبر.
**وابستگی:** Phase 1.

### Phase 3 — Account/Order Lifecycle + Instrument Attestation (M3, M4)
**پیش‌نیاز:** Phase 2؛ تصمیم Owner دربارهٔ اینکه آیا `Executions` در دامنهٔ فقط‌خواندنی ابزار مجاز است یا نه.
**کارها:**
1. M3: خواندن فقط‌خواندنی `Positions/Orders`؛ تلاش برای همبستگی Order↔Execution↔Position و علامت‌گذاری F-05
   به‌عنوان `INCONCLUSIVE` در صورت ناکامل بودن.
2. M4: بازتولید پیش‌شرط‌های attestation (D-096 route mapping + D-078 gate) و تشخیص دقیق علت fail.
3. عدم درج شناسهٔ اکانت در هر خروجی.
**معیار پذیرش:** تشخیص صحیح علت `HOST_UNAVAILABLE` + `D078_FIELD_APPROVAL_NOT_APPROVED` روی دادهٔ واقعی؛
حفظ privacy.
**وابستگی:** Phase 2 + تصمیم Owner برای `Executions`.

### Phase 4 — DOM/Market Data + TradeDelta/OrderFlow (M5, M6)
**پیش‌نیاز:** Phase 3.
**کارها:**
1. M5: کیفیت DOM (RESET frequency، snapshot-sync triggers، عمق، gap/duplicate)، `BarsRequest` برای M1.
2. M6: تحلیل نرخ جهت نامشخص، دلایل BBO/INSIDE_SPREAD، وضعیت TradeDelta.
3. صف رویداد bounded + شمارندهٔ drop.
**معیار پذیرش:** بازتولید آماری مشاهدات 2026-11-11 (270,979 رکورد، شمارنده‌های DOM) در محیط تست؛
بدون تغییر در گیتینگ BOT.
**وابستگی:** Phase 3.

### Phase 5 — Strategies/Risk Gate + Safety/Acceptance + UI/Report (M7, M8)
**پیش‌نیاز:** Phase 4.
**کارها:**
1. M7: تحلیل توزیع reason codeها (`ANALYTICAL_NO_TRADE`, `MISSING_INSTRUMENT_SPECIFICATION`,
   `ALL_CANDIDATES_INVALID`, Stage 3 Qty=0).
2. M8: تأیید `Trading=Disabled`، اسکن secret-free، تولید گزارش پذیرش نهایی.
3. UI: `NTWindow` + تب‌ها + نوار وضعیت؛ refresh با `DispatcherTimer`.
4. (اختیاری، فقط با تأیید) قرارداد تشخیصی فقط‌خواندنی `IReadOnlyDiagnosticSource` برای مصرف مستقیم از BOT.
**معیار پذیرش:** گزارش پذیرش کامل و secret-free؛ compile روی NT8 واقعی (نه فقط stub)؛
هیچ API سفارش/mutation در سورس.
**وابستگی:** Phase 4 + تأیید Owner برای compile میزبان و رابط تشخیصی.

### جدول وابستگی‌ها

```
Phase 1 (این سند)
   └─▶ Phase 2 (M1,M2 skeleton)
          └─▶ Phase 3 (M3,M4)   [نیازمند تصمیم Owner: Executions]
                 └─▶ Phase 4 (M5,M6)
                        └─▶ Phase 5 (M7,M8 + UI + Report)  [نیازمند تأیید compile میزبان]
```

---

## 9. محدودیت‌های صریح این سند (خود‌اظهاری)

- هیچ کد محصولی در این فاز نوشته نشده است.
- تمام ادعاهای مربوط به API بر پایهٔ **داکیومنت رسمی NinjaScript** است؛ سازگاری با build دقیق 8.1.8.4 روی
  میزبان واقعی **راستی‌آزمایی نشده** (NOT VERIFIED AT RUNTIME).
- هیچ تغییری در `Ai00120/RiskPilot.BOT` انجام نشده است.
- همهٔ شناسه‌های SHA و وضعیت مخازن از GitHub API در تاریخ 2026-11-12 استخراج شده‌اند و ممکن است جلو بروند؛
  پیش از هر فاز، `main` هر دو مخزن باید دوباره بررسی شود.

---

## 10. پیوست — مراجع

- NinjaScript Docs: `https://docs.ninjatrader.com/ninjascript/` (AddOn Overview, Lifecycle, State,
  Connection, ConnectOptions, ConnectionStatusUpdate, Account, AccountItem, AccountItemUpdate,
  AccountStatusUpdate, Instrument, MasterInstrument, MarketData, MarketDepth, MarketDepthEventArgs,
  BarsRequest, Order, Position, Execution, NTWindow, NTTabPage, TabControl, NTMenuItem, ControlCenter,
  Multi-Threading Considerations, Log).
- NT8 Release Notes: `https://ninjatrader.com/support/helpGuides/nt8/release_notes.htm`.
- RiskPilot.BOT (HEAD `46571d38`): `docs/ROADMAP.md`, `docs/REPOSITORY_AUTHORITY.md`,
  `docs/OWNER_DECISION_D095_PERMANENT_ES_IDENTITY.md`, `docs/OWNER_DECISION_D096_SIM_AND_CME_HOST_MAPPING.md`,
  `docs/P1_DOM_RESYNC_ROOT_CAUSE_REPORT_2026-11-10.md`, `docs/NT8_REAL_COMPILE_PROCEDURE.md`,
  `docs/PHASE5_READINESS_AUDIT.md`, `AGENTS.md`, `src/RiskPilot.BOT/**`, `verification/**`.
