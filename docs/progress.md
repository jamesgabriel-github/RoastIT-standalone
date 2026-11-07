# RoastIT (Roastline) — Build Progress

Last updated: 2026-11-07 (Phase 3 built, device checkpoint pending). Read this first in a new session, then `CLAUDE.md` and
`docs/implementation-plan.md` (the source of truth for rules and decisions).

## Status

| Phase | State | Notes |
|---|---|---|
| 0 — Foundation | **Done.** Checkpoint passed in Visual Studio | Layout, theme, shared components, fonts, DB startup, logging |
| 1 — App lock | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07) | Checklist kept below for reference |
| 2 — Menu | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07), no problems reported | Committed as `6657006`. Checklist kept below for reference |
| 3 — Bookings | **Done.** Checkpoint run by the user in Visual Studio (2026-11-07), no problems reported | `AddBookingsDomain` migration, `IBusinessCalendar`, `OrderMath`, `BookingService` / `OrderService` / `DashboardService`, Bookings page with all sheets. Checklist kept below for reference |
| 4 — Settings, backup, hardening | **In progress** | Settings has Security + Appearance only so far |
| 5 — Release | Not started | |

Build state: Android and Windows targets build with 0 errors and
0 warnings. 332 tests pass (`dotnet test tests\RoastIT.Tests`).

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

### `RoastIT.Tests`
Real SQLite temp files (`TempDb`, `AppLockFixture`, `FakeClock`). The App project only targets
Android/Windows, so the tests **link MAUI-free App source files** (`BackNavigationService`,
`ToastService`, `ThemeMode`, `PinPromptService`) via `<Compile Include ... Link=...>` in the test
csproj. Any new App service worth testing must be free of MAUI types and added there.

Phase 3 tests: `BookingsTestBase` (services over a migrated temp DB, clock at Sat 2026-11-07 11:00 Manila), `BookingServiceTests`,
`OrderServiceTests`, `BookingRulesTests` (OrderMath, statuses, calendar, window, scope state, formats),
`BookingListingAndDashboardTests`, `BookingUiRulesTests` (columns, prompts, chart layout, list state).

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
- `<input type="datetime-local">` can't use `@bind` to a string (Blazor wants a `DateTime`), so New booking binds it by hand.
- **D31 (owner's request):** the kitchen load chart now has a fixed 12 AM – 11:59 PM axis for the selected date, an hourly tick
  every 40 px, and scrolls sideways (y-axis labels pinned). Today shows a dashed now line and opens centred on it. Items counted
  are unchanged. `KitchenChartLayout.Build` takes the date and now; `app.js` gained `scrollLeftTo`. The bookings prototype, spec §11 and plan
  §7.6 are updated to match.

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
2. **New booking.** The product cards show active products only (not the inactive one). Two lines (qty 3 + qty 1) create four
   sequential IDs (`YYMMDD-001 … 004`). The *Booking created* dialog lists them, stays open until Done, ignores a backdrop tap,
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

## Next: finish the Phase 3 checkpoint, then Phase 4 — Settings, backup, hardening (plan §10.5, §10.6, §13 Phase 4)

**Next step (start of the next session):** ask the user how the Phase 3 checkpoint went and fix what they found. Then commit Phase 3
and build Phase 4 in the order of plan §13: Settings → Columns to show (the `BookingColumns` model, the `bookingColumns`
preference and the card/columns wiring already exist; only the Settings UI is missing), the list-view preference (already wired
on the Bookings page), Export/Import (§10.5), the 7-day backup warning, the automatic CSV backup (§10.6), the D21 indexes /
`<Virtualize>` / SQL totals, and `android:allowBackup="false"`. Open Phase 4 questions go in plan §15.1 as Q29+.

Things to keep in mind from Phase 3:
- Lists, the Dashboard and the date badges are computed **in memory** from `BookingService.ListAsync` (every booking, joined
  to order and product). That is fine for now and is exactly what D21 schedules for rework in Phase 4.
- The CSV backup needs "bookings with pickup within ±24 h of the save": `IBookingStore.ListRowsAsync` + `BookingRow` already carry
  the order and product fields it needs (notes are in `BookingRow.Notes`; started cooking in `StartedCookingAt`).
- Import replaces the database file: `BookingScopeState` and any open page hold stale data afterwards; reload the app's data as
  plan §10.5 step 7 says (the change notifier has no "everything changed" event yet).

## Working agreements (from CLAUDE.md, repeated because they matter)

Build phase by phase and finish each checkpoint. Check the decision log before changing behaviour;
changes get a new `D` row, new questions get the next `Q` number (next free: Q29 / D32). Add tests
for every rule touched, with real SQLite. Money is `long` centavos; times are UTC `DateTime`,
"today" comes from `IBusinessCalendar` (Asia/Manila). Never log the PIN, its hash or the recovery code.
