# RoastIT — standalone app

Booking and ordering app for a lechon roasting business (Litson Haus). It runs
offline on one shared Android phone, and all data stays on that phone.

**Name:** the app is called **RoastIT** everywhere a person sees it (window and
app title, screens, messages). It used to be "Roastline". The older name stays
in technical identifiers that are tied to existing backups and shipped
migrations, so **don't rename these**: the `.roastline` backup extension and
`roastline-backup-*` file names, `Documents/Roastline/roastline-bookings-48h.csv`,
`app_meta.app_id = 'roastline'` (import uses it to recognise backups), and the
`roastline.themeCache` browser key. Older docs and prototypes may still say
"Roastline"; they mean the same app.

- **Stack:** .NET 10 MAUI Blazor Hybrid, EF Core SQLite, xUnit.
- **Platforms:** Android first, then Windows. iOS comes later.
- **Screens:** Lock (PIN) · Bookings · Menu · Settings. The bottom nav shows
  Bookings · Menu · Settings.
- **Out of scope:** user accounts, roles, Admin Manager, sync server and cloud.

## Read first

| File | What it's for |
|---|---|
| `docs/progress.md` | **Where the build stands**: phase status, what exists, deviations from the plan, next steps. Read this first in a new session and keep it updated |
| `docs/implementation-plan.md` | **Source of truth**: rules, data model, PIN rules, build phases, and the decision log (§15) |
| `docs/prototypes/*-prototype.html` | Exact screens and interactions (signin, bookings, menu, settings). Open them at phone size. If the plan and a prototype disagree on **layout**, the prototype wins. On **rules**, the plan wins |
| `docs/specs/bookings-page-spec.md`, `docs/specs/menu-page-spec.md` | Detailed behavior (sort labels, empty states, chart rules). Ignore their desktop layout notes |

The prototypes use `localStorage` and fake data. Don't copy that. The app uses
SQLite plus MAUI `Preferences`.

## Commands (Windows, from the repo root)

```powershell
dotnet build src\RoastIT.App -f net10.0-android            # always pass -f; building all targets tries iOS and fails
dotnet build src\RoastIT.App -t:Run -f net10.0-android     # deploy to the connected phone/emulator
dotnet test  tests\RoastIT.Tests
dotnet ef migrations add <Name> -p src\RoastIT.Data -s src\RoastIT.Data
```

EF tools use `DesignTimeDbContextFactory` in `RoastIT.Data`, because the MAUI
project can't be the startup project. Migrations run on the device at startup,
after a copy of the database is saved first (plan §6.3). For the release
build, see plan §14.

## Layout

```
src/RoastIT.Core   entities, enums, abstractions, services, rules, validators. NO MAUI or EF references
src/RoastIT.Data   DbContext, configurations, migrations, repositories
src/RoastIT.App    Razor components, platform services (theme, prefs, PIN prompt, CSV file store), MauiProgram.cs
tests/RoastIT.Tests  xUnit against Core + Data with real SQLite (in-memory or a temp file)
```

## Non-negotiables

1. **Business rules live in `Core` services, never only in a component.** The
   UI may hide or disable things; the service re-checks and refuses.
2. **Every data-changing service method starts with
   `_appLock.EnsureUnlocked()`.**
3. **PIN confirmations are enforced in the service**
   (`_appLock.EnsureConfirmed(...)`), not only prompted in the UI.
4. **Money is `long` centavos** everywhere (₱1,250.50 = `125050`). Format to ₱
   only for display. Use `OrderMath.NetTotal` / `OrderMath.Balance`; never
   recompute them.
5. **Times:**
   - Instants are UTC `DateTime` (a UTC value converter; no `DateTimeOffset`).
   - Calendar dates are `DateOnly`.
   - "Today" and day boundaries always come from `IBusinessCalendar`
     (`Asia/Manila`), never the device time zone.
6. **Data access:** use `IDbContextFactory` with one short-lived context per
   operation. Never hold one context in a component.
7. **Change events** (`IChangeNotifier`) are raised **only after a successful
   save**.
8. **Never edit a migration that has shipped.** Prefer additive changes.
9. **Never log** the PIN, its hash or the recovery code. `audit_logs` is the
   business history; Serilog files are only for debugging.
10. **`ApplicationId` (`com.roastit.app`) and the signing keystore never
    change.** `ApplicationVersion` goes up by 1 on every release.

## Rules quick reference (details in the plan)

**Booking IDs**
- Format `YYMMDD-NNN`, one ID per item (product line), not per order and not per kilo.
- An item's quantity is a **weight in kilos** (`items.weight_g`, whole grams, 0.01 – 500 kg); a product's
  price is per kilo (D34). Use `OrderMath.LineTotal` and the `Weight` helpers.
- IDs come from `daily_counters`, claimed as a block in the same transaction
  as the order insert. A rollback leaves a gap, never a duplicate.

**Creating a booking**
- The total is recomputed from current catalog prices; never trust the form's
  total.
- Inactive products are rejected.
- The discount is clamped to `[0, total]`, and payments are always additive.

**Status**
- Forward only, one step at a time: Pending → Cooking → Cooked → Completed.
- The move to Cooking stamps `started_cooking_at`.
- `AdvanceStatusAsync` checks `expectedStatus`.

**Cancelling**
- Cancelling always cancels the **whole order**.
- It's allowed only while the order has a Pending or Cooking item and none
  that is Cooked or Completed. Re-check this at save time.
- `total_amount` never changes. Refunds aren't tracked, and a cancelled order
  drops out of Revenue, Collected and Balance due.

**Products** are deactivated, never deleted.

**PIN** (`PinScope`)

| Action | When the PIN is asked |
|---|---|
| Menu add / edit / activate / deactivate | Every time (single-use confirmation) |
| Settings Export / Import | Every time (`PinScope.Data`, single-use) |
| Bookings record payment, "Discount remaining balance", cancel order, change pickup time | Remembered for **3 minutes**; the window is cleared on lock |
| Create booking, manual discount, advance status | Not asked |

**App lock**
- PIN only, 4–6 digits, stored hashed in `app_lock`.
- A recovery code is shown once.
- 5 wrong tries → 30-second wait.
- The app locks on every reopen and after 10 minutes idle.

**Backups**
- **Export / Import** (`.roastline` = a SQLite copy):
  - The PIN and recovery code are never included. Export strips `app_lock`,
    then runs `VACUUM`.
  - Import always **replaces** data, never appends.
  - Import validates the file, saves a safety copy first, keeps the device's
    own PIN, then runs migrations.
  - If the phone already has bookings, import shows a warning.
- **Automatic CSV backup** (Android):
  - One file, `Documents/Roastline/roastline-bookings-48h.csv`, written
    through MediaStore.
  - It holds bookings with pickup from 24 h before to 24 h after the save.
  - It's rewritten **hourly, only while the app is in the foreground**.
    Nothing runs in the background, and nothing is saved after individual
    changes.
  - A CSV save must never block or fail a booking save.

**Preferences keys**

| Key | Values |
|---|---|
| `theme` | `light` / `dark` / `system` |
| `bookingColumns` | JSON |
| `bookingListView` | `detailed` / `compact` |
| `lastExportAt` | ISO timestamp |
| `lastCsvAt` | ISO timestamp |
| `lastCsvCount` | bookings in the last CSV save |
| `csvUri` | MediaStore URI of the CSV file |

Security data never goes in Preferences.

## Working in this repo

- **Build phase by phase** (plan §13), with Phase 0 first. Finish a phase's
  checkpoint before starting the next.
- **Before changing a behavior,** check the decision log (plan §15.2).
  - To change a decision, add a new `D` row that says which one it replaces,
    and update the section it points to.
  - New questions go in §15.1 as the next `Q` number. Don't silently pick an
    answer for anything listed there.
- **Tests:** add or extend tests in `RoastIT.Tests` for every rule touched,
  using real SQLite, not mocks of EF.
- **UI checks:** check UI changes against the prototype at phone width, in
  light and dark mode.
- **Plain CSS** ported from the prototypes into `wwwroot/css/app.css`. No CSS
  framework.
- **Indexes and performance (D21)** were done in Phase 4. The Bookings page
  reads only its scope: one day's rows, SQL counts, SQL Dashboard totals.
  To time a change against ten years of data, run
  `$env:ROASTIT_PERF = "1"; dotnet test tests\RoastIT.Tests --filter "FullyQualifiedName~TenYearData" --logger "console;verbosity=detailed"`.
