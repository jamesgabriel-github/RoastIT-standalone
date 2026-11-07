# RoastIT (Roastline) — Build Progress

Last updated: 2026-11-07 (Phase 4 done, Phase 5 on hold). Read this first in a new session, then `CLAUDE.md` and
`docs/implementation-plan.md` (the source of truth for rules and decisions).

## Status

| Phase | State | Notes |
|---|---|---|
| 0 — Foundation | **Done.** Checkpoint passed in Visual Studio | Layout, theme, shared components, fonts, DB startup, logging |
| 1 — App lock | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07) | Checklist kept below for reference |
| 2 — Menu | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07), no problems reported | Committed as `6657006`. Checklist kept below for reference |
| 3 — Bookings | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07), no problems reported. Committed as `242f2b0` | `AddBookingsDomain` migration, `IBusinessCalendar`, `OrderMath`, `BookingService` / `OrderService` / `DashboardService`, Bookings page with all sheets. Checklist kept below for reference |
| 4 — Settings, backup, hardening | **Done.** Checkpoint cleared by the user (2026-11-07). Not committed yet. Checklist kept below for reference | Settings Data/CSV/Columns, export/import, automatic CSV, `PinScope.Data`, D21 indexes + scoped reads + `<Virtualize>` |
| 5 — Release | **On hold** by the owner (2026-11-07). Not started | |

Build state: Android and Windows targets build with 0 errors and
0 warnings. 465 tests pass, 1 skipped (`dotnet test tests\RoastIT.Tests`; the skipped one is the opt-in ten-year timing).

## Environment (this machine)

- .NET SDK 10.0.403, Android and Windows MAUI workloads. Repo at `D:\RoastIT`.
- Code is written in VS Code with Claude Code; run and debug in Visual Studio 2026
  (open `RoastIT.slnx`, startup project `RoastIT.App`).
- No Android device or emulator was attached to the Claude session, so **UI was never
  run by Claude**. All UI checks are done by the user in Visual Studio.
- `dotnet-ef` global tool is 10.0.5 while EF runtime is 10.0.14. It prints a version
  warning but works.

## What exists

### `RoastIT.Core` (no MAUI/EF references)
- `Abstractions/`: `IClock` (+ `SystemClock`), `IAppLock` (+ `PinAttempt`, `PinConfirmation`,
  `ConfirmResult`, `RecoveryResetResult`, `LockoutStatus`), `IAppLockStore`, `IProductStore`,
  `IChangeNotifier` (`CatalogChanged` only so far).
- `Entities/`: `AppLockState`, `AuditLog` (+ `AuditActions`), `AppMeta`, `Product`.
- `Enums/PinScope` (Menu, Bookings).
- `Exceptions/`: `ValidationException`, `AccessDeniedException`, `PinRequiredException`,
  `StaleStateException` (own types, namespace `RoastIT.Core.Exceptions`).
- `Rules/`: `RecoveryCode` (generate/normalize), `PinRules` + `PinMessages` (exact user-facing texts),
  `Money` (₱ format, pesos field ⇄ centavos) + `DurationFormat`, `ProductListing` (Menu search/filter/
  sort, chip counts, empty-state and sort labels).
- `Validation/`: `ProductInput` (numbers) and `ProductDraft` (form strings → input), `ProductInputValidator`
  (FluentValidation; first failing rule in form order, with the field it belongs to), `ProductMessages`,
  `ProductLimits`.
- `Services/`: `AppLockService` (singleton: locked state, lockout, Menu single-use and
  Bookings 3-minute confirmations), `IdleMonitor` (10-minute auto-lock), `ProductService`
  (`ListAsync`, `CreateAsync`, `UpdateAsync`, `SetActiveAsync`; no delete), `ChangeNotifier` (singleton).
- **Phase 3 additions.** `Enums/ItemStatus` (Pending=0 … Cancelled=4, the pipeline order). `Entities/`: `Order`, `Item`,
  `Payment`, `DailyCounter`. `Abstractions/`: `IBusinessCalendar`, `IBookingStore` (+ `NewOrderDraft`, `OrderMutation<T>`);
  `IChangeNotifier` now also has `BookingsChanged(DateOnly pickupDate)` and `OrderChanged(Guid orderKey)`.
  `Rules/`: `OrderMath` (net total, balance, discount clamp, write-off window), `ItemStatusRules` (pipeline, labels, the cancel
  rule), `BookingIdFormat`, `ScheduleWindow` (today ± 6 months), `BookingFormat` (dates, times, scope label), `BookingRow`
  (flat read model + `WithOrderInfo` for "1 of 2" tags and the fully-cancelled flag), `BookingListing` (search, status/date
  filters, sort, grouping, chip and date counts, labels), `Dashboard` + `KitchenLoad`, `KitchenChartLayout` (chart pixels),
  `BookingColumns` (the `bookingColumns` JSON), `BookingPrompts` (PIN prompt texts). `Validation/BookingMessages` + `BookingLimits`.
  `Services/`: `BusinessCalendar` (Asia/Manila), `BookingService` (list, detail, `CreateAsync`, `AdvanceStatusAsync`),
  `OrderService` (`SetDiscountAsync`, `RecordPaymentAsync`, `WriteOffBalanceAsync`, `CancelOrderAsync`), `DashboardService`,
  `BookingScopeState` (singleton: selected date, tab, strip, calendar month, and the list's search/status/sort).
  Because Core has no EF reference, the services talk to `IBookingStore`; `EfBookingStore` in Data runs the transactions
  (ID block claim + insert; `MutateOrderAsync` loads the order as it is now, runs the rule code, saves it with its audit rows).
- **Phase 4 additions.** `Enums/PinScope.Data` (export/import: single-use, own scope; `AppLockService` keeps one
  `_singleUse` map for Menu and Data). `IChangeNotifier.DataReplaced`. `Abstractions/IBackupStore` (+ `BackupFileStatus`,
  `BackupSummary`, `BackupInspection`), `ICsvFileStore`, `ICsvBackupLog` (+ `CsvSaveInfo`). `IBookingStore` gained
  `ListRowsByPickupAsync`, `CountByStatusAsync`, `ListPickupTimesAsync`, `GetDashboardSourceAsync`. `Rules/`: `BackupFormat`
  (file name, dates, contents line), `BackupReminder` (7 days), `BookingsCsv` (window, hourly rule, the writer: BOM, CRLF,
  RFC 4180, formula guard), `BookingColumnFields` (Settings switches + caption), `Dashboard.Compose` + `OrderTotalsRow` /
  `ProductStatusCount` / `DashboardSource`. `Validation/BackupMessages`. `Services/`: `BackupService` (export, inspect, count,
  import; PIN enforced here), `CsvBackupService` (singleton; never throws; skipped while locked). `BookingService` gained
  `ListAsync(DateOnly?)`, `StatusCountsAsync`, `CountsByDateAsync`; `DashboardService` uses SQL totals. Core now references
  `Microsoft.Extensions.Logging.Abstractions` (10.0.14, it already came in through Identity).

### `RoastIT.Data`
- `RoastItDbContext` (UTC `DateTime` converter via `ConfigureConventions`; configurations
  picked up from the assembly), `Configurations/` (snake_case columns), `Repositories/EfAppLockStore`
  and `EfProductStore` (a product and its audit row save in one transaction).
- `DatabaseInitializer`: runs at app start. Backs up with SQLite's backup API (not `File.Copy`)
  when migrations are pending, keeps the newest 3 `*.bak-before-*-from-*` copies, migrates,
  stamps `app_meta` (`schema_version` = number of applied migrations, `app_version`), sets WAL.
- Migrations: `InitialAppLock` (app_lock with `id = 1` check constraint, audit_logs, app_meta
  seeded with `app_id = roastline`); `AddProductsCatalog` (`products`, with check constraints
  `price >= 0` and `cooking_time_min >= 1`; no seed rows). **Shipped migrations are never edited**
  (non-negotiable 8).
- `DesignTimeDbContextFactory` for the EF tools.
- **Phase 3 additions.** Migration `AddBookingsDomain` (`orders`, `items`, `payments`, `daily_counters`, check constraints
  `discount_amount <= total_amount`, `payments.amount > 0`, no cascade deletes; the only indexes are the foreign-key ones EF
  creates by convention — the real indexes still wait for Phase 4, D21). `Configurations/BookingsConfigurations.cs`,
  `Repositories/EfBookingStore`.
- **Phase 4 additions.** Migration `AddPerformanceIndexes` (index changes only: `ix_orders_pickup_at`, `ix_items_status`,
  `ix_products_active`, covering `ix_items_order_status_id` and `ix_items_product_status`, which replace EF's two FK indexes on
  `items`). `Repositories/SqliteBackupStore`: export = `VACUUM INTO` → `journal_mode=DELETE`, `secure_delete`, delete
  `app_lock`, stamp `exported_at`, `VACUUM`, then the audit row in the live DB; inspect = header check, read-only open,
  `integrity_check`, `app_meta`, schema, counts; import = staged copy (migrate, this device's `app_lock` row, drop
  `exported_at`, audit, stamp) → safety copy → marker → clear pools → file move → WAL → remove marker; any failure restores the
  safety copy. `DatabaseFiles` (paths, side files, restore, prune import copies to 3, `RecoverInterruptedImport`, which
  `DatabaseInitializer` now runs first at startup). `AfterSwapForTests` hook (internal) to simulate a failed swap.

### `RoastIT.App`
- `MauiProgram`: DI, Serilog (daily files, 14 kept, in `AppDataDirectory/logs`), startup DB init,
  unhandled-exception logging. `App.xaml.cs`: applies theme, starts the 15 s idle check, locks the
  app on `Window.Stopped` and `Window.Resumed`. `MainPage.OnBackButtonPressed` hands Back to
  `BackNavigationService`.
- `Services/`: `PreferenceKeys`, `PreferencesStore`, `ThemeMode`/`ThemeService`,
  `BackNavigationService` (layer stack: PageBack < FullSheet < BottomSheet < Dialog < PinPrompt),
  `ToastService`, `PageHeaderState`, `PinPromptService`, `AppBackgrounder`.
- `Components/Shared/`: `Icon`/`IconPaths`, `Sheet`, `BottomSheet`, `ConfirmDialog`, `Keypad`,
  `PinDots`, `PinPrompt`, `Switch`, `Segmented`, `StatusPill`, `FilterChips`, `EmptyState`, `Fab`,
  `ToastHost`, `AppHost`, `LockGate`, `PageHeading`, `LockoutCountdown`, `LayerComponentBase`.
- `Components/Layout/`: `MainLayout` (top bar + bottom nav), `BottomNav`, `LockLayout`.
- `Components/Products/`: `ProductCard`, `ProductSortSheet`, `ProductSheet` (add/edit form: validate →
  Menu PIN prompt → `ProductService` → toast).
- `Components/Pages/`: `Home` (redirects to bookings), `Bookings` (the real screen, see below),
  `Menu` (the real screen: search, chips, sort, cards, FAB; reloads on `CatalogChanged`), `Settings`
  (Security: Update PIN; Appearance: theme), `Lock`, `NotFound`.
- `wwwroot/css/app.css` (tokens + shared blocks ported from the prototypes), `wwwroot/js/app.js`
  (theme, Escape, scroll lock, activity reporting, keypad capture), bundled IBM Plex fonts.
- **Phase 3 additions.** `Components/Bookings/`: `DateStrip`, `CalendarSheet`, `BookingSortSheet`, `BookingCard`, `CompactRow`,
  `BookingListTab`, `DashboardTab`, `KitchenLoadChart` (SVG at the real screen width; text built with the render tree because
  Razor reserves `<text>`), `BookingDetailSheet`, `NewBookingSheet`, `BookingCreatedDialog`. `Pages/Bookings.razor` is the real
  screen now. `PreferencesStore` gained `CompactBookingList` and `BookingColumns`. `app.js` gained `watchWidth`, `scrollToStart`
  and `focusSelect`. `Sheet` gained `TitleClass`. DI: `IBusinessCalendar`, `IBookingStore`, `BookingScopeState` (singletons),
  `BookingService`, `OrderService`, `DashboardService` (scoped).
- **Phase 4 additions.** `Pages/Settings.razor` is complete (Security, Data with the 7-day reminder, CSV card, Appearance
  with the spec's two captions, Columns to show with *Show all*, version + build). `Components/Settings/`: `ExportSheet`,
  `ImportSheet` (states A/B, existing-bookings warning, inline errors), `CsvBackupCard` (saved / never / failed-retry /
  unsupported, Share/Open, reinstall note). `Services/`: `ExternalActivityGuard` (Q11; MAUI-free, linked into tests),
  `DataTransferService` (cache folder, `Share`, `FilePicker`, `lastExportAt`), `CsvFileStores.cs` (`ICsvFileShare`,
  `UnsupportedCsvFileStore` for Windows), `PreferencesCsvBackupLog`; `PreferencesStore` gained `LastExportAt`, `LastCsvSave`,
  `CsvUri` (new key `lastCsvCount`). `Platforms/Android/AndroidCsvFileStore` (MediaStore: remembered URI → look up own row →
  insert; `"wt"`; opens the file with an `ACTION_VIEW` chooser). `App.xaml.cs`: the guard decides whether Stopped/Resumed lock;
  the CSV loop (check on start/resume/unlock, then every minute; cancelled on Stopped). Bookings page: loads only the scope
  (one day's rows, or all rows for the all-dates list; chips, badges and Dashboard from SQL), reloads on `DataReplaced`;
  `BookingListTab` takes `StatusCounts` and switches to `<Virtualize>` above 150 entries. New icon `share` (app-only).

### `RoastIT.Tests`
Real SQLite temp files (`TempDb`, `AppLockFixture`, `FakeClock`). The App project only targets
Android/Windows, so the tests **link MAUI-free App source files** (`BackNavigationService`,
`ToastService`, `ThemeMode`, `PinPromptService`) via `<Compile Include ... Link=...>` in the test
csproj. Any new App service worth testing must be free of MAUI types and added there.

Phase 3 tests: `BookingsTestBase` (services over a migrated temp DB, clock at Sat 2026-11-07 11:00 Manila), `BookingServiceTests`,
`OrderServiceTests`, `BookingRulesTests` (OrderMath, statuses, calendar, window, scope state, formats),
`BookingListingAndDashboardTests`, `BookingUiRulesTests` (columns, prompts, chart layout, list state).

Phase 4 tests: `TestDevice` (one "phone": its own initialised DB file, PIN and backup services; two of them simulate moving a
backup between installs), `Data/BackupTests` (export strips the PIN with a byte search for the hashes, Data PIN rules,
inspection refusals incl. corrupt / newer schema / WAL-mode file, import keeps this device's PIN, older-schema migrate, failed
swap restore, force-close recovery at start, pruning), `Core/CsvBackupTests` (writer, ±24 h across Manila midnight, hourly
rule, no save on booking changes, failing store, locked, unsupported), `Core/ScopedQueriesTests` (SQL Dashboard equals the
Phase 3 in-memory `Dashboard.Build` for all dates and four days, date-scoped lists, Manila day boundary, chips, badges,
indexes), `Core/SettingsRulesTests` (texts, columns, `ExternalActivityGuard`), `Perf/TenYearDataTests` (opt-in, below).

## How the main pieces fit

- **Lock flow:** app starts locked (in memory) → `LockGate` shows only `/lock` while locked →
  `Lock.razor` state machine (Enter / Confirm / Recovery / Unlock / Verify / Forgot / Done) →
  `AppLockService`. Unlock/setup/reset end on Bookings after ~1.4 s; change PIN returns to Settings.
- **PIN for critical actions:** UI calls `PinPromptService.RequestAsync(summary, label, scope)` →
  gets `PinPromptResult(Confirmed, Confirmation)`. The **service method** then calls
  `_appLock.EnsureConfirmed(confirmation, scope)`; it must not trust the UI. For Bookings inside the
  3-minute window `Confirmation` is null and `EnsureConfirmed(null, Bookings)` passes.
- **Every data-changing service method starts with `_appLock.EnsureUnlocked()`.**
- **Layers** (sheets, dialogs, PIN prompt) derive from `LayerComponentBase`, which registers with
  `BackNavigationService` while `Open`. Android Back and Escape close the top layer.
- **Theme:** `ThemeService` (Preferences) → `AppHost` applies `data-theme` on `<html>`; a pre-paint
  script in `index.html` reads a cached copy from `localStorage` (cache only; Preferences is the truth).

## Decisions and deviations from the plan

- iOS and Mac Catalyst removed from `TargetFrameworks` (add `net10.0-ios` back when needed).
- Pre-migration copy uses SQLite's backup API, because `File.Copy` can miss WAL content.
- `INTERNET` permission removed from the Android manifest (offline app). If debug/hot reload can't
  connect, re-add it for Debug.
- Plan §9.5 `IAppLock` snippet updated to the real shape (attempt results with tries left/lockout).
- Open questions Q8–Q11 added to plan §15.1 (unlock destination, half-done setup, Back on the lock
  screen, and lock vs system pickers in Phase 4). Their current assumptions are implemented.
- Razor gotcha: inside `@onclick="() => ..."` lambdas, `&quot;` is not decoded. Use a method.
- Razor gotcha: a **string** component parameter takes the attribute as literal text, so it needs `@` (`BookingId="@_detailId"`).
  Non-string parameters (`Open="_detailOpen"`) are read as C# without it. This made the booking detail sheet show "_detailId".
- Open questions Q12–Q20 added to plan §15.1: the Menu spec's open points (whole-minute cooking time,
  centavos allowed, duplicate names allowed, field limits, silent discard, error placement, sort ties)
  plus two behaviours I chose (no-op saves, one audit row per edit). Their current assumptions are implemented.
- The Menu starts empty: the prototype's seed products are fake data (CLAUDE.md), so none are seeded.
- Android Back from Menu/Settings leaves the app instead of returning to Bookings (not specified
  in the plan; left as is — ask the user if they want it changed).
- Open questions Q21–Q28 added to plan §15.1: the Bookings spec's open points (§14.1–§14.7: money actions on cancelled orders,
  overpayment, paid-now cap, pickup validation, "Latest first" in the grouped view, sort ties, notes) plus input limits and the
  audit rows for a discount/payment entered at creation. Their current assumptions are implemented.
- `DatabaseInitializerTests` now expects `schema_version = 3` (three migrations applied).
- The plan's §8 sketch has the services use the DbContext directly; Core has no EF reference, so they use `IBookingStore`
  (`EfBookingStore` in Data) instead, as `ProductService` already does with `IProductStore`. Same transactions, same rules.
- Bookings refresh: the page subscribes to `Scope.Changed` and all three change events and re-queries; the detail sheet
  re-queries itself on `OrderChanged`/`BookingsChanged`. The list's search/status/sort live in `BookingScopeState` (not the page),
  so they survive going to Menu and back, and reset on app restart.
- `<text>` in SVG: Razor reserves that tag name, so `KitchenLoadChart` builds the SVG text elements with the render tree.
- **New booking pickup picker** (prototype `bookings-prototype_new_book_date_picker.html`): the `datetime-local` input is replaced by
  `PickupPicker.razor`, a Date part (button → "Pickup date" calendar sheet with booking dots, plus Today / Tomorrow chips) and a Time
  part (hour 1–12 list : typed minutes : AM/PM), with a summary line that warns "This time has already passed." The parts are joined
  and split by `Core/Rules/PickupTime` (tested in `PickupTimeTests`). The picker keeps its own calendar month, so it doesn't move the
  Bookings filter. Unchanged from Q24: default is now + 5 h with minutes set to 00 (not the prototype's tomorrow 10:00), past pickups are allowed, and
  `CreateAsync` still enforces today ± 6 months. The picker always holds a value, so the "Choose a pickup date and time." message
  can no longer appear. Not yet checked on a device: light/dark at phone width, and typing non-digits in the minutes field.
- **Phase 4.** Open questions Q29–Q37 added to plan §15.1 (export counts on share-sheet open, exact 7-day reminder, cancelled
  bookings counted, any file type in the picker, no CSV before Android 10, CSV column contents, import safety copies and the
  cut-off swap, "all dates" with years of data, errors inline in the sheets); Q11 is now implemented (`ExternalActivityGuard`).
  Their current assumptions are implemented. **Q36 was decided by the owner as D32**: Bookings opens on today, and "all dates"
  (list, chips, Dashboard) means today ± 6 months, through `ScheduleWindow.RangeUtc`. Older bookings are still in the database
  and exports but no longer on the Bookings page. Ten-year timing after D32: all-dates list ≈90 ms, all-dates Dashboard ≈60 ms.
  The owner also chose to keep Q33 as is (no CSV before Android 10).
- `CsvBackupService` lives in **Core**, not the app (plan §10.6 said app), so the hourly rule and the failure handling are tested;
  the app supplies `ICsvFileStore` (MediaStore) and `ICsvBackupLog` (Preferences). The plan section is updated.
- New Preferences key **`lastCsvCount`** (the card's "· 6 bookings"); `CLAUDE.md` and plan §10.4 updated.
- `PinScope.Data` is new (plan §9.5 snippet updated). Export/import confirmations are single-use and can't be spent on Menu
  actions or the other way round.
- The export audit row is written to the live database after the copy is made, so a backup never contains its own
  `data.exported` row. The import's `data.imported` row is written into the imported data.
- Import works on a **staged copy**: migrations, the lock row and the audit row are done there first, so the live database
  is only touched by the final file move. `exported_at` is removed from the imported data (it's only for exported copies).
- D21 indexes: measured with ten years of data, the plan's starting set wasn't enough for the all-dates Dashboard (≈600 ms on
  a PC). Two **covering** indexes on `items` bring it to ≈190 ms; they replace EF's plain FK indexes on `items`. The migration
  only changes indexes, so no table is rebuilt.
- The Dashboard tie-break for equal balances and pickups now uses the order's first booking ID in `Dashboard.Build` too
  (it used whichever row came first), so the SQL and in-memory versions agree exactly.
- **D31 (owner's request):** the kitchen load chart now has a fixed 12 AM – 11:59 PM axis for the selected date, an hourly tick
  every 40 px, and scrolls sideways (y-axis labels pinned). Today shows a dashed now line and opens centred on it. Items counted
  are unchanged. `KitchenChartLayout.Build` takes the date and now; `app.js` gained `scrollLeftTo`. The bookings prototype, spec §11 and plan
  §7.6 are updated to match.

## D34 weights in kilos (owner's request after the Phase 4 checkpoint; plan §7.1, §7.2, bookings spec §9)

The New booking form's quantity was a **count that created one booking (and one ID) per unit**. It is now a **weight in
kilos**: one product line = one booking = one ID, with `items.weight_g` (whole grams). A product's price is per kilo, so a line
costs `OrderMath.LineTotal(price, grams)`, rounded to the centavo. Open points are in Q38. Both targets build with 0 warnings;
465 tests pass, 1 skipped.

- Core: `Rules/Weight` (parse "3.2" → 3200, format, ± 0.5 kg steps, limits 0.01 – 500 kg), `OrderMath.LineTotal`,
  `Item.WeightGrams`, `BookingRow.WeightGrams`, `NewBookingLine(ProductId, WeightGrams)`, `NewItemDraft`, `CreatedUnit.WeightGrams`,
  `BookingMessages.WeightInvalid`. `BookingLimits.MaxUnitsPerOrder` (100) now caps **lines**, not kilos.
- Data: migration `AddItemWeight` (`weight_g INTEGER NOT NULL DEFAULT 1000`, additive). Bookings made before it count as 1 kg.
  `DatabaseInitializerTests` now expects `schema_version = 5`.
- UI: `NewBookingSheet` has a decimal kg field with a "kg" label and − / + (0.5 kg); product cards read "₱2,200/kg"; a blank or
  invalid weight shows the inline message. The weight also shows on the booking card ("Lechon Belly · 3.2 kg"), the detail
  sheet (*Weight* row) and the Booking created dialog. The CSV has a new **Weight (kg)** column after *Product*.
- **Not done:** the HTML prototypes (`bookings-prototype*.html`) still show the old integer stepper. The Menu page still shows a
  plain price (not "/kg"). Neither the Dashboard's Product mix nor the kitchen load counts kilos (they count bookings).
- **Not run on a device.** Please check on the phone: type `3.2` and get ₱ price × 3.2; the − / + buttons; two lines make two IDs;
  a blank weight shows the message; the decimal keyboard in light and dark mode; the card, detail and created dialog show the kilos.

## Phase 1 device checkpoint (run by the user, Android)

Fresh install (or Clear storage):
1. First run shows "Set up your PIN" → confirm → recovery code (Continue disabled until ticked); a
   mismatched confirm shows "PINs didn't match. Choose your PIN again."
2. Close and reopen: Unlock screen appears.
3. 5 wrong PINs: keys disable with a 30 s countdown; force-close during it and reopen: still counting.
4. Forgot PIN: recovery code (lowercase/spaces OK) → new PIN → new code; old code no longer works.
5. Settings → Update PIN: Cancel works at each step; completing keeps the same recovery code.
6. Idle 10 minutes: the app locks.
7. Back on Unlock sends the app to the background; on the recovery-code view it does nothing.
8. Everything renders correctly in dark mode.

The in-app `PinPrompt` was exercised by the Phase 2 checkpoint.

## Phase 2 device checkpoint (run by the user; plan §13 Phase 2)

The Menu starts **empty** (the prototype's five seed products are fake data and are not seeded).
Also exercises the in-app `PinPrompt` for the first time, which was never run on a device.

1. Empty state reads "No products in the menu yet. Add your first one to get started."
2. **Add product**: submitting a blank name, a bad price or a 0 cooking time shows the spec message
   (one at a time, no PIN prompt, the failing field is focused). A valid form asks for the PIN, then the
   card appears and the toast says "Added {name}". Cancelling the PIN keeps the sheet and the typed values.
3. **Edit** asks for the PIN every time, even straight after a previous Menu PIN (and even within 3
   minutes of a Bookings PIN, once Bookings exists). Changing the switch in the edit saves with one PIN.
4. **Deactivate / Activate** on a card asks for the PIN every time; an inactive card is dimmed, stays
   under the **Inactive** chip, and the chip counts (global, ignoring search) update.
5. Search matches name and description; the three empty-state texts appear in the right situations.
6. Sort sheet: field rows, direction labels (A → Z / Low to high / Shortest first), Done; Back closes
   the PIN prompt first, then the sort sheet or the product sheet.
7. Keyboard behaviour in the product sheet (price shows a decimal keypad, cooking time a numeric one),
   and light and dark mode at phone width.

## Phase 3 device checkpoint (run by the user; plan §13 Phase 3, bookings spec §15)

The app starts with an empty Menu and no bookings, so first add two or three products in Menu (and leave one inactive).
Nothing in Phase 3 was run by Claude: **please watch the Bookings page closely on the first run** (it is the biggest screen so far).
After installing over the Phase 2 build, the `AddBookingsDomain` migration runs at start (a safety copy is saved first).

1. **Date strip and calendar.** The strip starts two days before today, shows count badges, the arrows stop at ±6 months, tapping
   the selected day clears it (All), and picking a far date in the calendar moves the strip to it (scrolled back to its start).
   Calendar: month arrows stop at the window edges, dots on days with bookings, Today / Clear date, the window caption.
2. **New booking.** The product cards show active products only (not the inactive one). *(Phase 3 wording; since D34 the
   quantity is kilos: two lines of 3.2 kg and 1 kg create two IDs, `YYMMDD-001` and `002`.)* The *Booking created* dialog lists them, stays open until Done, ignores a backdrop tap,
   and Back closes it. Totals and the *Create booking · ₱…* label update while typing without losing focus. The date-time picker
   works on Android (default is now + 5 hours). A blank customer shows as *Walk-in customer*.
3. **Booking list.** Search finds IDs, products, customers and "walk-in". Chips show global counts. The sort sheet shows the labels
   in spec §6.4; grouping by date happens only with all dates + Pickup time. Detailed ⇄ Compact (remembered after a restart);
   compact rows show time only when the day is clear; tap expands to a card and the chevron collapses. Multi-booking orders show
   *1 of N* tags in both views.
4. **Status.** The card's quick-advance asks first (*Update status?*); the detail sheet's button doesn't. Pending → Cooking
   stamps the start (the kitchen chart uses it). A second tap on a stale card shows a toast and refreshes.
5. **Payments.** *+ Record payment* is pre-filled with the balance; a ₱0 amount closes without a PIN; otherwise the PIN prompt
   (with the 3-minute note) appears; cancelling the PIN keeps the editor open. A second payment within 3 minutes does not ask;
   after 3 minutes, or after a lock, it does. Amount paid and Balance due update everywhere, including the Dashboard.
6. **Discount.** Apply, Edit, Remove need no PIN; the Net total row appears; an amount above the total is clamped.
7. **Write-off.** For a balance under 10% of the gross total the orange banner shows (e.g. total ₱2,000, paid ₱1,900); the button
   asks for the PIN (unless the window is open) and zeroes the balance.
8. **Cancel.** From any booking of a multi-booking order the button reads *Cancel order*, the PIN text lists all IDs and any money
   already paid, and every booking becomes Cancelled. Once one booking is Cooked or Completed the button is gone and the caption
   *This order can't be cancelled…* shows on Pending/Cooking bookings. A cancelled order drops out of the Dashboard money and shows
   *Cancelled · ₱0* on its cards; its detail sheet offers no discount, payment or write-off.
9. **Dashboard.** KPIs, Status breakdown (only with a date), Kitchen load (only for today or later; 12 AM – 11 PM axis that
   scrolls sideways with the 0/1/2… labels staying put; today opens on the dashed now line, other dates near the first bar; tap a
   bar to read it; redraws when the phone rotates), Product mix, Balance due. Changes made in the detail sheet show up when you switch tabs *and* while
   the Dashboard tab is open behind the sheet.
10. **Back button and layers.** Back closes PIN prompt → dialog → sort/calendar sheet → detail/new booking sheet, never the app from
    inside one. The FAB hides under a full sheet and under the created dialog. Toasts sit above the sheet footer.
11. **Light and dark mode** at phone width on every piece above, and the keyboard behaviour inside the New booking sheet (the
    focused field scrolls into view; numeric keyboards for qty and amounts).

## Phase 4 device checkpoint (run by the user, cleared 2026-11-07; plan §13 Phase 4, settings spec §11)

Nothing in Phase 4 was run by Claude on a device. Installing over the Phase 3 build runs the `AddPerformanceIndexes` migration
at start (a safety copy is saved first). **Export a backup before trying Import**, and try Import on a phone whose data you can
lose (or after exporting).

1. **Settings page.** All five sections at phone width in light and dark mode; the theme caption changes when Device is picked;
   the version line shows the build number.
2. **Columns to show.** Each switch changes the Bookings cards straight away (go to Bookings and back); the caption counts
   hidden columns (singular for 1); *Show all* appears only when something is hidden; Booking ID has no switch; the choice
   survives a restart.
3. **Backup reminder.** On a fresh install: *No backup yet…*. It disappears after an export, and the Export row shows
   *Last export: …*.
4. **Export.** The PIN prompt appears every time (even straight after another export, and without the 3-minute note); the share
   sheet opens **without locking the app** on the way there or back; save the file to Drive or Downloads; the toast reads
   *Backup saved: roastline-backup-YYYYMMDD-HHmm.roastline*. Pressing Home afterwards still locks the app.
5. **Import, refusals.** Choosing a photo, a text file, or a `.roastline` with some bytes changed shows *This file isn't a
   Roastline backup…*; current data is unchanged. (A newer-schema file can only be made by hand; the tests cover it.)
6. **Import, across installs.** Export on phone/install A. On install B (fresh install, own PIN): choose the file → the summary
   (file, date, version, counts) shows; if B already has bookings, the *This phone already has N bookings* warning shows
   (singular for 1); *Replace data on this device* → PIN → toast → Bookings opens **still unlocked**; all of A's bookings,
   payments and products are there; lock the app: **B's PIN** opens it and A's doesn't. New bookings continue A's numbering.
7. **Import, force-close.** Start an import and force-close the app during *Replacing…*; reopen: the data is either fully the
   old data or fully the new data, never a mix.
8. **Automatic CSV.** A minute or so after unlocking, the card shows the file (saved time, count, pickup window). In the Files
   app, **Documents/Roastline/roastline-bookings-48h.csv** exists; open it in Sheets/Excel: names with "ñ" and amounts look
   right, and it holds exactly the pickups from 24 h before to 24 h after the save time. The card's Share/Open button opens
   it. It isn't rewritten again until an hour later (the saved time stays). With the app in the background for over an hour,
   nothing is written until it comes back to the front.
9. **Back button.** Back closes the PIN prompt, then the Export/Import sheet; the import sheet can't be closed while
   *Replacing…* is running.
10. **Performance (D21).** On the cheapest target phone, on a test install: import `artifacts/perf/roastline-10-years-sample.roastline`
    (≈31 MB, 76k generated bookings; make it with the command in CLAUDE.md "Indexes and performance"), then time: opening
    Bookings (all dates list), Today, the Dashboard for all dates and for today, typing in search, scrolling the long list, and
    switching Detailed ⇄ Compact. Desktop numbers for comparison: all-dates list ≈90 ms, all-dates Dashboard ≈60 ms, one day
    ≈1–2 ms.
11. **D32.** Bookings opens on **Today** (the label reads *Showing Today*, today's tile is selected), also after a restart.
    Tapping today's tile or *All* shows all dates; with the ten-year file, the all-dates list and the *All · N* chip only hold
    bookings from today − 6 months to today + 6 months, and the Dashboard for all dates matches.

## D33 "Save to a folder…" device check (owner's request after the Phase 4 checkpoint; plan §10.5)

Export now has a second button under **Export backup**: *Save to a folder…* (Android only). It opens Android's "save as" screen
(`ACTION_CREATE_DOCUMENT`) through `AndroidBackupFileSaver`; `MainActivity.OnActivityResult` hands the result back; the logic is in
`DataTransferService.SaveToFolderAsync`. Nothing here was run on a device; both targets build with 0 warnings.

1. The button shows under the caption *Or choose the folder on this phone yourself.* in light and dark mode.
2. Tap it → PIN prompt (every time) → the system "save as" screen opens with `roastline-backup-YYYYMMDD-HHmm.roastline` suggested,
   **without locking the app** on the way there or back.
3. Pick Downloads (or an SD card) and save: the toast reads *Backup saved: <name>*, the sheet closes, *Last export* updates, and the
   file is in that folder with the **`.roastline` ending intact** (watch for a `.bin` added by Android) and is the right size.
4. Rename the file in the "save as" screen: the toast shows the new name.
5. Back out of the screen: the sheet stays open, no error, *Last export* unchanged. The next try asks for the PIN again.
6. Import that saved file on another install (or after clearing data) to confirm it opens.
7. Back closes the PIN prompt, then the sheet; Home afterwards still locks the app.

## Next: Phase 5 — Release is on hold (plan §14, §13 Phase 5)

The Phase 4 checkpoint is cleared. The owner has put Phase 5 on hold, so **do not start it** until they say so.

**Next step (start of the next session):** Phase 4 is still **uncommitted** in the working tree (and so are the date-picker and
D31/D32 changes); commit it if the owner asks. **D33 "Save to a folder…" is built but not yet run on a device** (checklist below).

When Phase 5 is resumed, it covers: the signing keystore (outside the repo), the signed release APK, a release build check
(trimming can break EF Core: run the release build on the phone with real data), the upgrade test over the previous APK,
`CHANGELOG.md`, and a Git tag.

Things to keep in mind from Phase 4:
- `android:allowBackup="false"` was already in the manifest from Phase 0 (D14); nothing to change.
- The CSV file store is Android 10+ only; Windows registers `UnsupportedCsvFileStore` (D30).
- The release build must keep EF Core working under trimming; check export/import there too (they use raw SQLite commands).

## Working agreements (from CLAUDE.md, repeated because they matter)

Build phase by phase and finish each checkpoint. Check the decision log before changing behaviour;
changes get a new `D` row, new questions get the next `Q` number (next free: Q39 / D35). Add tests
for every rule touched, with real SQLite. Money is `long` centavos; times are UTC `DateTime`,
"today" comes from `IBusinessCalendar` (Asia/Manila). Never log the PIN, its hash or the recovery code.
