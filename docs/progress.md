# RoastIT (Roastline) — Build Progress

Last updated: 2026-11-07 (Phase 2 built). Read this first in a new session, then `CLAUDE.md` and
`docs/implementation-plan.md` (the source of truth for rules and decisions).

## Status

| Phase | State | Notes |
|---|---|---|
| 0 — Foundation | **Done.** Checkpoint passed in Visual Studio | Layout, theme, shared components, fonts, DB startup, logging |
| 1 — App lock | **Built. Code and tests done; device checkpoint not yet run** | See "Phase 1 device checkpoint" below |
| 2 — Menu | **Built. Code and tests done; device checkpoint not yet run** | See "Phase 2 device checkpoint" below |
| 3 — Bookings | Not started — **next** | `AddBookingsDomain` migration, `IBusinessCalendar`, `OrderMath`, booking services, Bookings page |
| 4 — Settings, backup, hardening | Not started | Settings has Security + Appearance only so far |
| 5 — Release | Not started | |

Build state: Android and Windows targets build with 0 errors and
0 warnings. 173 tests pass (`dotnet test tests\RoastIT.Tests`).

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
- **Not yet created** (later phases): `IBusinessCalendar`, `OrderMath`, `BookingService`,
  `OrderService`, `DashboardService`.

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
- `Components/Pages/`: `Home` (redirects to bookings), `Bookings` (empty placeholder with a FAB toast),
  `Menu` (the real screen: search, chips, sort, cards, FAB; reloads on `CatalogChanged`), `Settings`
  (Security: Update PIN; Appearance: theme), `Lock`, `NotFound`.
- `wwwroot/css/app.css` (tokens + shared blocks ported from the prototypes), `wwwroot/js/app.js`
  (theme, Escape, scroll lock, activity reporting, keypad capture), bundled IBM Plex fonts.

### `RoastIT.Tests`
Real SQLite temp files (`TempDb`, `AppLockFixture`, `FakeClock`). The App project only targets
Android/Windows, so the tests **link MAUI-free App source files** (`BackNavigationService`,
`ToastService`, `ThemeMode`, `PinPromptService`) via `<Compile Include ... Link=...>` in the test
csproj. Any new App service worth testing must be free of MAUI types and added there.

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
- Open questions Q12–Q20 added to plan §15.1: the Menu spec's open points (whole-minute cooking time,
  centavos allowed, duplicate names allowed, field limits, silent discard, error placement, sort ties)
  plus two behaviours I chose (no-op saves, one audit row per edit). Their current assumptions are implemented.
- The Menu starts empty: the prototype's seed products are fake data (CLAUDE.md), so none are seeded.
- `DatabaseInitializerTests` now expects `schema_version = 2` (two migrations applied).
- Android Back from Menu/Settings leaves the app instead of returning to Bookings (not specified
  in the plan; left as is — ask the user if they want it changed).

## Phase 1 device checkpoint (to run in Visual Studio, Android)

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

Not yet exercised on a device: the in-app `PinPrompt` (first used by the Phase 2 Menu; see below).

## Phase 2 device checkpoint (to run in Visual Studio, Android; plan §13 Phase 2)

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

## Next: Phase 3 — Bookings (plan §7, §13; `docs/specs/bookings-prototype-spec.md`, `docs/prototypes/bookings-prototype.html`)

Start with the `AddBookingsDomain` migration, `IBusinessCalendar` (Asia/Manila), booking-ID allocation,
`OrderMath`, then `BookingService` / `OrderService` / `DashboardService`, then the Bookings page. Phase 2
already provides `IChangeNotifier` (only `CatalogChanged` so far; add `BookingsChanged(date)` and
`OrderChanged(key)`), `Money`/`DurationFormat` display helpers, and `ProductService.ListAsync` for the
New booking product picker (filter to `Active`; `CreateAsync` must still reject inactive products itself).

## Working agreements (from CLAUDE.md, repeated because they matter)

Build phase by phase and finish each checkpoint. Check the decision log before changing behaviour;
changes get a new `D` row, new questions get the next `Q` number (next free: Q21 / D31). Add tests
for every rule touched, with real SQLite. Money is `long` centavos; times are UTC `DateTime`,
"today" comes from `IBusinessCalendar` (Asia/Manila). Never log the PIN, its hash or the recovery code.
