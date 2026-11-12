# RP-DIAG-01 — PHASE 4: UI, EVIDENCE REPORTING & TESTS

**Product:** RiskPilot NT8 Diagnostic Center
**Namespace:** `RiskPilot.Diagnostics.Nt8`
**Document type:** Phase 4 implementation record
**Prepared:** 2026-11-12
**Safety invariant:** `Trading=Disabled` — read-only, no order/execution/account mutation.
**Depends on:** `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md`, `docs/PHASE2_IMPLEMENTATION.md`,
`docs/PHASE3_DIAGNOSTIC_MODULES.md` (all approved).

> این سند ثبت می‌کند که در فاز ۴ **واقعاً چه ساخته شد**، چگونه راستی‌آزمایی شد و — صریحاً —
> چه چیزی **راستی‌آزمایی نشد**. بیلد آفلاین از stubهای زمان‌کامپایل استفاده می‌کند، بنابراین
> **سازگاری واقعی با میزبان NinjaTrader 8.1.8.4 و رندر واقعی پنجرهٔ WPF با این فاز اثبات
> نمی‌شود** (بخش ۹).
>
> هیچ تغییری در `Ai00120/RiskPilot.BOT` انجام نشد. سورس BOT فقط برای شناخت رابط‌ها و منطق
> موجود **مطالعه** شد (read-only).

---

## 1. خلاصهٔ اجرایی

فاز ۴ سه چیز را به هستهٔ فاز ۲ و ماژول‌های فاز ۳ اضافه می‌کند:

1. **رابط کاربری** — یک پنجرهٔ مستقل در NinjaTrader Control Center با نام
   `RiskPilot NT8 Diagnostic Center` و سه حالت تشخیصی.
2. **بستهٔ شواهد (Evidence Package)** — ۱۷ فایل مستقل برای هر اجرا، به‌همراه `manifest.json`
   با SHA-256 هر فایل.
3. **ماژول M9 (Post-Trade Audit)** — تحلیل فقط‌مشاهده‌ای رویدادهای ثبت‌شدهٔ قبلی.

| # | قابلیت جدید | لایهٔ جدید اصلی |
|---|---|---|
| UI | پنجره، منو، مدیریت پنجره | `Host/DiagnosticCenterWindow.cs`، `Host/MenuIntegration.cs`، `Host/DiagnosticCenterWindowManager.cs` |
| حالت‌ها | Quick / Full / Post-Trade Audit | `Core/Run/DiagnosticMode.cs` |
| اجرا | Coordinator (تک‌اجرا، Cancel، marshalling) | `Core/Run/DiagnosticRunCoordinator.cs`، `Core/Run/DiagnosticViewState.cs` |
| دیسپچر | IUiDispatcher + پیاده‌سازی‌های WPF/Switchable | `Core/Run/IUiDispatcher.cs`، `Host/WpfUiDispatcher.cs`، `Core/Run/SwitchableUiDispatcher.cs` |
| بستهٔ شواهد | ۱۷ artifact + manifest | `Core/Evidence/*` |
| M9 | Post-Trade Audit | `Core/Engine/Probes/M9PostTradeAuditProbe.cs`، `Core/Models/Observations/PostTradeAuditObservation.cs` |
| Sink | نوشتن روی دیسک + In-memory | `Host/EvidenceDirectorySink.cs`، `Host/DiagnosticRuntime.InMemoryEvidenceSink` |

### نتیجهٔ راستی‌آزمایی (offline)

```
dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
  → Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
  → Passed! Failed: 0, Passed: 200, Skipped: 0, Total: 200

python3 verification/addon-smoke/test_diagnostic_safety.py
  → STATIC SAFETY CHECK: PASS (103 production source files, 34 forbidden patterns)
```

---

## 2. رابط کاربری — `RiskPilot NT8 Diagnostic Center`

پنجره یک `NTWindow` مستقل است که از منوی اصلی NinjaTrader Control Center باز می‌شود
(`MenuIntegration`). تنها یک نمونه از پنجره وجود دارد؛ درخواست دوم پنجرهٔ موجود را فعال
می‌کند (`DiagnosticCenterWindowManager`).

پنجره شامل این بخش‌هاست:

- **Mode selector** — سه حالت (Quick / Full / Post-Trade Audit).
- **Run / Cancel / Export** — اجرا، توقف پردازش تشخیصی، صدور بستهٔ شواهد.
- **Progress bar** — درصد و ماژول جاری.
- **Module grid** — یک ردیف برای هر ماژول با `PASS / FAIL / INCONCLUSIVE / NOT_APPLICABLE`،
  تعداد checks و اینکه آیا اجرا شد.
- **Findings tab** — Findings غیرِ PASS (هر رشته از `SecretRedactor` عبور می‌کند).
- **Acceptance tab** — سه موضوع پذیرش مستقل (بخش ۶).
- **Status bar** — همیشه با پیشوند `Trading: DISABLED`.

### سه حالت تشخیصی

| حالت | `ModuleIds` | توضیح |
|---|---|---|
| Quick | `M1, M2, M3, M4, M5, M8` | Environment، Provider/Connection، Account visibility، Instrument، Market-data readiness، Safety status |
| Full | `M1, M2, M3, M4, M5, M6, M7, M8` | هر هشت ماژول فاز ۳ |
| Post-Trade Audit | `M9` | فقط رویدادهای ثبت‌شدهٔ قبلی |

انتخاب ماژول در `Core/Run/DiagnosticMode.cs` (`DiagnosticModeCatalog`) به‌صورت **ثابت** تعریف
شده است؛ UI، engine و بستهٔ شواهد همه از همین یک منبع می‌خوانند، بنابراین معنیِ یک حالت در همهٔ
لایه‌ها یکسان است.

---

## 3. مدیریت اجرا

### 3.1 تک‌اجرا (Multiple execution prevention)

`DiagnosticRunCoordinator` یک `RunStateMachine` دارد. `Start` تنها وقتی موفق می‌شود که هیچ
اجرای فعالی نباشد؛ درخواست دوم `false` برمی‌گرداند و UI پیام «A diagnostic run is already in
progress» را نشان می‌دهد. این رفتار با `FakeDiagnosticRunner.MaxConcurrentRuns` تست می‌شود.

### 3.2 عدم Freeze شدن Control Center

اجرا روی یک thread پس‌زمینه (`RiskPilot.Diagnostics.Run`) انجام می‌شود، هرگز روی thread رابط
کاربری یا thread callback نینجاتریلر. هر اعلان UI از طریق `IUiDispatcher` و فقط با
`InvokeAsync` (هرگز `Invoke` مسدودکننده) marshal می‌شود.

`DiagnosticAddOn.SafeInitialize` رویدادهای coordinator (`ProgressChanged` / `RunCompleted`) را
به `DiagnosticViewState` وصل می‌کند (`WireViewState`)، و پنجره با خواندن همان state آن را
رندر می‌کند. تا وقتی پنجره‌ای وجود ندارد، `SwitchableUiDispatcher` اعلان‌ها را **inline** روی
همان thread اجرا می‌کند (state داخلاً thread-safe است) تا پیشرفت اجرا گم نشود؛ پس از اتصال
پنجره، اعلان‌ها با `InvokeAsync` به thread UI منتقل می‌شوند.

### 3.3 Cancel

`Cancel` فقط پردازش تشخیصی را متوقف می‌کند:

- `RunStateMachine` به `Cancelling` می‌رود؛
- engine بین ماژول‌ها `shouldStop` را poll می‌کند و ادامهٔ اجرا را متوقف می‌کند؛
- نتیجهٔ نهایی `Cancelled` است، هرگز `Completed`؛
- بستهٔ شواهد برای اجرای نیمه‌کاره **نوشته نمی‌شود** (یک بستهٔ ناقص نباید به‌عنوان بستهٔ کامل
  ارائه شود).

### 3.4 Shutdown صحیح

`SafeDispose` ابتدا coordinator را Dispose می‌کند (Cancel + Join با timeout ۵ ثانیه)، سپس
`ShutdownUiLayer` (فقط در بیلد میزبان واقعی) دیسپچر را جدا و پنجره را می‌بندد، و در نهایت
runtime را Dispose می‌کند. هر مرحله در `try/catch` است؛ یک خرابی در shutdown هرگز به میزبان
throw نمی‌شود. تست `Dispose_DuringActiveRun_DoesNotHang` تضمین می‌کند Dispose قفل نمی‌کند.

### 3.5 Dispatcher shutdown

`WpfUiDispatcher.HasShutdownStarted` هر دو حالت `HasShutdownStarted` و `HasShutdownFinished` را
پوشش می‌دهد. وقتی دیسپچر در حال خاموش شدن است، `Notify` اعلان را **رها** می‌کند (نه throw)،
پس thread اجرا هرگز fault نمی‌شود.

---

## 4. ماژول M9 — Post-Trade Audit

`M9PostTradeAuditProbe` فقط روی `PostTradeAuditObservation` کار می‌کند؛ یعنی فقط چیزی که
**قبلاً ثبت شده** است. هیچ مسیری در این ماژول معامله ایجاد یا تغییر نمی‌دهد.

| CheckId | چه چیزی را می‌سنجد | قاعدهٔ fail-closed |
|---|---|---|
| `M9.AUDIT_SURFACE` | آیا سطح audit قابل‌مشاهده بود | نبود observation → `INCONCLUSIVE` |
| `M9.ORDER_LIFECYCLE` | snapshot/update سفارش‌ها | نبود داده → `INCONCLUSIVE` |
| `M9.EXECUTION_LIFECYCLE` | update اجراها | `ExecutionsInScope=false` → `INCONCLUSIVE` |
| `M9.POSITION_UPDATES` | به‌روزرسانی پوزیشن‌ها | نبود داده → `INCONCLUSIVE` |
| `M9.ORDER_SNAPSHOTS` | `Account.Orders` snapshots | نبود داده → `INCONCLUSIVE` |
| `M9.ORDERID_CLASSIFICATION` | طبقه‌بندی OrderId | کلاس نامعتبر (Null/Malformed) → `FAIL` |
| `M9.CORRELATION_TIMING` | همبستگی و ترتیب زمانی | رویداد بی‌همتا یا timestamp نامعتبر → `INCONCLUSIVE`/`FAIL` |
| `M9.MISSING_OR_DUPLICATE` | رویدادهای گم‌شده/تکراری | تکرار یا گم‌شدگی → `FAIL` |

سطح audit از `DiagnosticSnapshot.PostTradeAudit` خوانده می‌شود. `DiagnosticRuntime` این
observation را از دو منبع **واقعی** می‌سازد:

1. رکوردهایی که از طریق pipeline دریافت شده‌اند (`EvidenceCollector`)، و
2. snapshotهای **`Account.Orders`** که لایهٔ میزبان قبلاً برای M3 خوانده است
   (`Snapshot.OrderLifecycle`).

این ادغام در `MergeOrderLifecycleSnapshot` انجام می‌شود و رویدادهای تکراری (بر اساس
`OrderRef` مصنوعی) را یک‌بار می‌شمارد.

مهم: این observation به دو مسیر می‌رسد و **هر دو** باید پر شوند:

- **اجرای زنده:** `EnrichWithPostTradeAudit` (یک enricher روی `EnrichedDataProvider`) snapshot
  را پیش از رسیدن به probe پر می‌کند؛ بدون آن probe همیشه `UNOBSERVABLE` می‌داد.
- **بستهٔ شواهد:** `BuildEvidenceDataset` همان observation را برای خروجی می‌سازد.

بنابراین در میزبان واقعی، حتی وقتی pipeline هیچ رویدادی دریافت نکرده باشد (ابزار به هیچ feed
رویداد میزبان subscribe نمی‌کند)، audit سطح مشاهده‌پذیر دارد و به‌جای `INCONCLUSIVE` نتیجهٔ
واقعی می‌دهد.

`ExecutionsInScope` هرگز hardcode نمی‌شود: میزبان فقط `Account.Orders` را می‌خواند و
`ExecutionsInScope=false` گزارش می‌کند، پس ادعای پوشش execution می‌توانست
`M9.CORRELATION_TIMING` را بدون هیچ execution مشاهده‌شده `PASS` کند (نقض fail-closed).

---

## 5. بستهٔ شواهد (Evidence Package)

`EvidencePackageBuilder.Build` یک تابع **خالص** است (بدون نوع میزبان، بدون فایل‌سیستم، بدون
ساعت) که ۱۷ فایل می‌سازد:

```
summary.html                full-report.html          acceptance-matrix.csv
environment.json            connections.json          accounts.json
instruments.json            orders.csv                executions.csv
positions.csv               market-data-summary.json  dom-summary.json
orderflow-summary.json      strategies.json           safety-audit.json
findings.json               manifest.json
```

### 5.1 فقط دادهٔ واقعاً قابل‌مشاهده

اگر یک منبع خوانده نشده باشد، بخش مربوطه با `status=UNAVAILABLE` و یک `reason` نوشته می‌شود —
هرگز داده ساختگی و هرگز حذفِ بی‌صدا. مثال‌ها:

- `connections.json` بدون اتصال → `UNAVAILABLE`؛
- `dom-summary.json` بدون snapshot → `UNAVAILABLE`؛
- `orders.csv` خالی → فقط سطر header، بدون سطر جعلی؛
- `market-data-summary.json` → `sourceTimeBasis` از خود رکوردهای مشاهده‌شده مشتق می‌شود
  (ضعیف‌ترین basis)، و بدون رکورد `UNOBSERVABLE` است — هرگز مقدار ثابت/فرضی نوشته نمی‌شود.

### 5.2 Manifest

`manifest.json` شامل این فیلدهاست:

| فیلد | توضیح |
|---|---|
| `schemaVersion` / `toolVersion` / `buildMode` | شناسهٔ نسخه و حالت بیلد |
| `sessionId` / `correlationId` / `modeId` | شناسهٔ اجرا |
| `startUtc` / `endUtc` | شروع و پایان به UTC |
| `ntRuntimeVersion` | همیشه `UNOBSERVABLE` (API عمومی وجود ندارد) |
| `addOnVersion` | نسخهٔ ابزار |
| `provider` / `instrument` | Provider و Instrument مشاهده‌شده |
| `accountsObserved[]` | حساب‌های مشاهده‌شده با **Mask** (`account#<hash>`) |
| `recordCount` / `droppedCount` | تعداد رکوردها و تعداد Dropped |
| `truncated` / `truncationReason` | وضعیت Truncated |
| `files[]` | هر فایل خروجی + `sha256` + `byteLength` |
| `aggregateStatus` | وضعیت کلی اجرا |
| `manifestSha256` | SHA-256 خود manifest |

### 5.3 یکپارچگی (Integrity)

`EvidencePackage.VerifyIntegrity` بررسی می‌کند که هر ۱۷ فایل موجود باشد و SHA-256 هر فایل با
بایت‌های واقعی مطابقت داشته باشد. `WriteEvidencePackage` **قبل** از نوشتن، integrity را بررسی
می‌کند؛ اگر بررسی شکست بخورد چیزی نوشته نمی‌شود و `false` برگردانده می‌شود — یک بستهٔ خراب
هرگز به‌عنوان بستهٔ معتبر ارائه نمی‌شود.

### 5.4 Findings

هر Finding در `findings.json` این فیلدها را دارد: `findingId`، `severity`، `status`،
`observation`، `timestamp`، `source`، `collectionMethod`، `evidenceReference`، `limitation`،
`recommendedNextAction`.

### 5.5 Redaction

- هر رشتهٔ خروجی از `SecretRedactor` عبور می‌کند (رمز، token، شمارهٔ حساب، email، مسیر کاربر،
  IPv4).
- هویت‌ها با `SyntheticRef` توکن می‌شوند.
- در manifest، `MaskedRef` **همیشه** یک توکن `account#<hash>` است، حتی اگر منبع از قبل
  synthetic باشد.
- `EvidencePackage.IsSecretFree` همهٔ artifactها (به‌جز manifest) را اسکن می‌کند و
  `ManifestIsIdentitySafe` تضمین می‌کند manifest هیچ هویت خام حمل نمی‌کند.

### 5.6 محل خروجی

`EvidenceDirectorySink` هر اجرا را در یک زیرپوشهٔ مستقل (به نام session id) زیر ریشهٔ
`%USERPROFILE%\Documents\RiskPilot.Diagnostics.Evidence\` می‌نویسد — **بیرون** از working tree
گیت. نوشتن هر فایل اتمیک است (temp + move). نام فایل به یک segment تک محدود می‌شود تا بسته
هرگز از پوشهٔ خودش خارج نشود.

---

## 6. سه موضوع پذیرش مستقل

`AcceptanceSummaryBuilder.Build` سه موضوع را **جدا** گزارش می‌کند و هرگز آن‌ها را در یک حکم
ادغام نمی‌کند:

1. **AddOn implementation / build** — آیا ابزار build و load می‌شود.
   در بیلد stub همیشه `INCONCLUSIVE` است (بیلد آفلاین سازگاری میزبان را اثبات نمی‌کند).
2. **Runtime diagnostic coverage / evidence quality** — از status ماژول‌ها و شمارنده‌های
   یکپارچگی داده مشتق می‌شود.
3. **RiskPilot.BOT acceptance** — همیشه `NOT_APPLICABLE`. این ابزار مستقل از RiskPilot.BOT است و
   هرگز نمی‌تواند آن را تأیید کند.

> موفقیت Build ابزار به معنی PASS شدن RiskPilot.BOT **نیست**.

---

## 7. آزمون‌های ضروری

فاز ۴ چهار فایل تست جدید اضافه می‌کند (۴۴ تست) و مجموع سوئیت را به ۱۹۳ می‌رساند.

| موضوع آزمون | فایل تست | پوشش |
|---|---|---|
| UI thread safety | `Phase4RunCoordinatorTests` | اعلان‌ها از `IUiDispatcher` عبور می‌کنند |
| Dispatcher shutdown | `Phase4RunCoordinatorTests` | خاموش‌شدن دیسپچر اجرا را fault نمی‌کند |
| Cancel behavior | `Phase4RunCoordinatorTests` | Cancel قبل از تمام ماژول‌ها متوقف می‌کند |
| Multiple execution prevention | `Phase4RunCoordinatorTests` | اجرای دوم رد می‌شود؛ `MaxConcurrentRuns==1` |
| Report generation | `Phase4RuntimeIntegrationTests` | گزارش با همهٔ ماژول‌ها ساخته می‌شود |
| CSV/JSON validity | `Phase4EvidencePackageTests` | header و invariant culture |
| HTML rendering | `Phase4EvidencePackageTests` | `<!DOCTYPE html>` و escape |
| SHA-256 verification | `Phase4EvidencePackageTests` | هش با بایت‌های واقعی مطابقت دارد |
| Manifest consistency | `Phase4EvidencePackageTests` | ۱۶ فایل، فیلدهای اجباری، mask |
| Empty data | `Phase4EvidencePackageTests` / `Phase4RuntimeIntegrationTests` | `UNAVAILABLE` / `INCONCLUSIVE` |
| Partial data | `Phase4EvidencePackageTests` (`PartialDataset_*`) | فقط بخش‌های مشاهده‌شده `AVAILABLE` |
| Missing events | `Phase4PostTradeAuditTests` | `M9.CORRELATION_TIMING` → `INCONCLUSIVE` |
| Duplicate events | `Phase4PostTradeAuditTests` | `M9.MISSING_OR_DUPLICATE` → `FAIL` |
| Queue overflow | `Phase4RuntimeIntegrationTests` | `M0.EVENT_INTEGRITY` → `FAIL` |
| Secret redaction | `Phase4EvidencePackageTests` | رمز در observation ماسک می‌شود |
| Status correctness | `Phase4PostTradeAuditTests` | PASS/FAIL/INCONCLUSIVE درست |
| M9 bridge (export) | `Phase4RuntimeIntegrationTests` (`PostTradeAudit_FoldsIn*`) | سطح `Account.Orders` واقعی + بدون شمارش مضاعف |
| M9 live (probe) | `Phase4RuntimeIntegrationTests` (`PostTradeAuditMode_ObservesHostSurface_Live`) | enricher سطح audit را در اجرای زنده پر می‌کند |
| Source-time basis | `Phase4EvidencePackageTests` (`MarketDataSummary_*`) | مقدار مشتق‌شده، نه hardcoded |
| Coordinator→ViewState | `Phase4RuntimeIntegrationTests` (`Coordinator_WiredToViewState_*`) | grid/result/acceptance پر می‌شوند |
| Execution scope fail-closed | `Phase4RuntimeIntegrationTests` (`PostTradeAudit_WithoutExecutionCoverage_*`) | بدون پوشش execution → `INCONCLUSIVE` نه `PASS` |

---

## 8. تست‌های واقعیِ اجراشده (offline)

| مجموعه | تعداد | نتیجه |
|---|---|---|
| `Phase4RunCoordinatorTests` | 9 | PASS |
| `Phase4EvidencePackageTests` | 17 | PASS |
| `Phase4PostTradeAuditTests` | 10 | PASS |
| `Phase4RuntimeIntegrationTests` | 15 | PASS |
| سوئیت قبلی (فاز ۱–۳) | 149 | PASS |
| **مجموع کل سوئیت** | **200** | **PASS** |

Static safety scan: **PASS** (103 فایل production، 34 الگو).

---

## 9. چه چیزی راستی‌آزمایی شد و چه چیزی هنوز Runtime واقعی می‌خواهد

### ۹.۱ پیاده‌شده و راستی‌آزمایی‌شده (offline)

- منطق کامل حالت‌ها، coordinator (تک‌اجرا، Cancel، marshalling دیسپچر)، بستهٔ شواهد، manifest و
  integrity، M9، redaction — همه با کد production و تست.
- همهٔ ۱۷ artifact تولید و SHA-256 آن‌ها با بایت‌های واقعی تأیید می‌شود.
- رفتار fail-closed در دادهٔ خالی/ناقص/گم‌شده/تکراری/سرریز صف.
- static safety scan روی همهٔ فایل‌های production.

### ۹.۲ پیاده‌شده اما **نیازمند Runtime واقعی NT8** برای اثبات

- **رندر واقعی پنجرهٔ WPF**: `DiagnosticCenterWindow`، `MenuIntegration`، `WpfUiDispatcher` و
  `DiagnosticCenterWindowManager` فقط در بیلد میزبان واقعی (`UseStubs=false`) کامپایل می‌شوند و
  در بیلد آفلاین **اصلاً کامپایل نمی‌شوند**؛ بنابراین صحت آن‌ها فقط روی NT8 واقعی اثبات می‌شود.
- افزوده‌شدن آیتم منو به منوی واقعی Control Center.
- marshalling واقعی `InvokeAsync` روی دیسپچر واقعی WPF.
- رفتار واقعی `Dispatcher.HasShutdownFinished` در زمان خاموش‌شدن NT8.
- نوشتن واقعی فایل روی مسیر `Documents` و باز شدن HTML در مرورگر.

### ۹.۳ هرگز قابل‌مشاهده با API عمومی

- نسخهٔ دقیق runtime میزبان NT8 → در manifest همیشه `UNOBSERVABLE`.
- نسخهٔ build پرووایدر → `UNOBSERVABLE`.
- سمت aggressor معامله از Level I → `Unobservable`.

---

## 10. محدودیت‌ها

- بیلد آفلاین stub فقط یک بررسی نحو/قرارداد است، نه اثبات سازگاری میزبان.
- لایهٔ UI در بیلد آفلاین کامپایل نمی‌شود؛ پوشش تست آن غیرمستقیم است (منطق host-free در
  `Core/Run` تست می‌شود، لایهٔ WPF فقط روی NT8 واقعی).
- سقف collector شواهد (پیش‌فرض ۱۰۰۰۰ رکورد به‌ازای هر نوع) باعث `truncated=true` می‌شود؛ این
  حالت صریحاً گزارش می‌شود، ولی یعنی یک session بسیار پرحجم ممکن است همهٔ رکوردها را نداشته باشد.

---

## 11. ایمنی (بدون تغییر نسبت به فاز ۲/۳)

- Read-only: هیچ فراخوانی order-submission/modification/cancellation وجود ندارد.
- هیچ تغییر روی account/connection/position/تنظیمات RiskPilot.
- بدون Reflection، بدون `unsafe`، بدون native interop.
- `Trading=Disabled`؛ هیچ مسیری آن را فعال نمی‌کند.
- دادهٔ گم‌شده هرگز `PASS` نمی‌شود (fail-closed).
- هیچ شناسهٔ حساب یا secret در گزارش/لاگ ظاهر نمی‌شود.
- خروجی‌ها **بیرون** از working tree گیت نوشته می‌شوند.

---

## 12. Git

- تغییرات **فقط** در `Ai00120/RiskPilot.BOOT`.
- هیچ تغییری در `Ai00120/RiskPilot.BOT` انجام نشد.
- یک commit مستقل فاز ۴ (بخش Commit در PR/تاریخچه).
