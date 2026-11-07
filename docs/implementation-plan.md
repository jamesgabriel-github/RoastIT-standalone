# RoastIT Standalone — Implementation Plan

Status: Build reference for the **standalone mobile app**: a .NET MAUI
Blazor Hybrid app with a local SQLite database, for a lechon roasting
business. Use it to build the app step by step, or hand it to Claude Code
in VS Code.

**Where the details live**

| Source | Use it for |
|---|---|
| This document | Stack, setup, project layout, data model, business rules, app lock and PIN rules, build phases, release |
| `signin-prototype.html`, `bookings-prototype.html`, `menu-prototype.html`, `settings-prototype.html` (same folder) | **Exact screens and interactions**: open them in a browser at phone size. When this doc and a prototype disagree on layout, the prototype wins |
| `bookings-page-spec.md`, `menu-page-spec.md` | Detailed behavior of Bookings and Menu (sorting labels, empty states, chart rules). Ignore their desktop layout notes; the prototypes define the mobile layout |

---

## 1. Scope

**What the app is:** an offline app the shop runs on **one shared phone or
tablet**. It manages bookings (orders, cooking status, payments) and the
product menu. The data lives only on that device.

**Screens**

| Screen | Route | Purpose |
|---|---|---|
| Lock | `/lock` | First-run PIN setup, unlock, forgot PIN (recovery code), change PIN |
| Bookings | `/bookings` (default after unlock) | Date strip, Dashboard tab, Booking list tab, booking detail, new booking |
| Menu | `/menu` | Product catalog: add, edit, activate/deactivate |
| Settings | `/settings` | Update PIN, export/import data, automatic CSV backup (48-hour file), Light/Dark/Device theme, Bookings columns to show |

Navigation is a bottom bar: **Bookings · Menu · Settings**.

**Platforms, in order:**
1. Android (primary)
2. Windows (nice to have)
3. iOS (later; needs a Mac and a paid Apple Developer account)

**Not in scope:**
- user accounts or roles
- syncing between devices
- a web version
- online payments

---

## 2. Tech stack

| Concern | Choice |
|---|---|
| Runtime | **.NET 10** (LTS, supported to Nov 2028) |
| App framework | **.NET MAUI Blazor Hybrid** (`dotnet new maui-blazor`) |
| UI | Razor components + plain CSS ported from the prototypes (no CSS framework) |
| Database | **SQLite** file in `FileSystem.AppDataDirectory`, via **EF Core** (`Microsoft.EntityFrameworkCore.Sqlite`) |
| Migrations | EF Core Migrations, applied on the device at startup |
| PIN hashing | `PasswordHasher<T>` from `Microsoft.Extensions.Identity.Core` |
| Validation | FluentValidation (in `Core`), surfaced in forms |
| Logging | `Microsoft.Extensions.Logging` + Serilog file sink in app data |
| Per-device settings | MAUI `Preferences` |
| Tests | xUnit against `Core` + `Data` with real SQLite (in-memory or temp file) |
| IDE | Visual Studio 2026 (run/debug/publish); VS Code + Claude Code for writing code |

---

## 3. Development environment (Windows)

1. Install **Visual Studio 2026 Community** with the **.NET Multi-platform
   App UI development** workload (default options). It installs the
   .NET 10 SDK, Android SDK, OpenJDK and the Android emulator.
2. Turn on **Windows Hypervisor Platform** in "Turn Windows features on or
   off", then restart. The emulator needs it.
3. Turn on **Developer Mode** (Settings → System → For developers) to run
   the Windows version.
4. Install **Git**. In VS Code, add the **C# Dev Kit** and **.NET MAUI**
   extensions.
5. Optional, and usually faster than the emulator: a real Android phone
   with **USB debugging** on. To turn it on, open Settings → About phone
   and tap Build number 7 times, then enable USB debugging in Developer
   options.
6. Check the install:
   ```powershell
   dotnet --info              # .NET 10 SDK listed
   dotnet workload list       # maui listed
   dotnet new list maui       # ".NET MAUI Blazor Hybrid App" (maui-blazor) listed
   ```
7. Keep the repo on a **short path without spaces** (e.g. `D:\Dev\RoastIT`).
   Android builds on Windows fail on long paths.

---

## 4. Solution structure

```
D:\Dev\RoastIT\
  CLAUDE.md
  docs\
    implementation-plan.md            ← this file
    prototypes\                       ← the four *-prototype.html files
    specs\                            ← bookings-page-spec.md, menu-page-spec.md
  src\
    RoastIT.App\                      ← MAUI Blazor Hybrid (UI + platform services)
      Components\
        Layout\                       ← MainLayout (bottom nav), LockLayout
        Pages\                        ← Lock, Bookings, Menu, Settings
        Bookings\                     ← DateStrip, Dashboard, BookingList, BookingCard,
                                         CompactRow, BookingDetailSheet, NewBookingSheet,
                                         KitchenLoadChart, CalendarSheet
        Menu\                         ← ProductCard, ProductSheet
        Shared\                       ← Sheet, BottomSheet, ConfirmDialog, PinPrompt,
                                         Keypad, StatusPill, FilterChips, SortSheet,
                                         Toast, EmptyState, Switch, Segmented
      Services\                       ← ThemeService, PreferencesStore, PinPromptService,
                                         BackNavigationService, ToastService, IdleTimer
      wwwroot\css\app.css             ← tokens + styles ported from the prototypes
      MauiProgram.cs
    RoastIT.Core\                     ← no MAUI, no EF references
      Entities\                       ← Product, Order, Item, Payment, AppLockState, AuditLog
      Enums\                          ← ItemStatus, PinScope
      Abstractions\                   ← IAppLock, IBusinessCalendar, IClock, IChangeNotifier,
                                         repositories, IUnitOfWork
      Services\                       ← AppLockService, ProductService, BookingService,
                                         OrderService, DashboardService
      Rules\                          ← OrderMath, BookingIdFormat, KitchenLoad, ScheduleWindow
      Validation\                     ← FluentValidation validators
    RoastIT.Data\
      RoastItDbContext.cs
      Configurations\                 ← IEntityTypeConfiguration per entity
      Migrations\
      Repositories\
      DesignTimeDbContextFactory.cs
  tests\
    RoastIT.Tests\
```

### 4.1 Scaffold

```powershell
cd D:\Dev; mkdir RoastIT; cd RoastIT
dotnet new sln -n RoastIT
dotnet new maui-blazor -n RoastIT.App   -o src\RoastIT.App
dotnet new classlib    -n RoastIT.Core  -o src\RoastIT.Core
dotnet new classlib    -n RoastIT.Data  -o src\RoastIT.Data
dotnet new xunit       -n RoastIT.Tests -o tests\RoastIT.Tests
dotnet sln add src\RoastIT.App src\RoastIT.Core src\RoastIT.Data tests\RoastIT.Tests

dotnet add src\RoastIT.Data    reference src\RoastIT.Core
dotnet add src\RoastIT.App     reference src\RoastIT.Core src\RoastIT.Data
dotnet add tests\RoastIT.Tests reference src\RoastIT.Core src\RoastIT.Data

dotnet add src\RoastIT.Core package FluentValidation
dotnet add src\RoastIT.Core package Microsoft.Extensions.Identity.Core
dotnet add src\RoastIT.Data package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src\RoastIT.Data package Microsoft.EntityFrameworkCore.Design
dotnet add src\RoastIT.App  package Serilog.Extensions.Logging
dotnet add src\RoastIT.App  package Serilog.Sinks.File
dotnet tool install --global dotnet-ef
```

In `RoastIT.App.csproj`, set these once:

```xml
<ApplicationTitle>Roastline</ApplicationTitle>
<ApplicationId>com.roastit.app</ApplicationId>      <!-- never change after the first install -->
<ApplicationDisplayVersion>1.0.0</ApplicationDisplayVersion>
<ApplicationVersion>1</ApplicationVersion>          <!-- +1 for every release -->
```

When building from the terminal, always pass a target framework
(`-f net10.0-android`). Building every target on Windows tries iOS and
fails without a Mac.

---

## 5. Architecture rules

1. **Business rules live in `Core` services, never only in a Razor
   component.** Components hide or disable things for convenience; services
   re-check and refuse. This applies to validation, totals, discount
   clamping, the status pipeline, the app lock and PIN confirmations.
2. **`Core` depends on nothing platform-specific.** It defines interfaces
   (`IBusinessCalendar`, `IClock`, `IAppLock`, `IChangeNotifier`,
   repositories). `Data` implements persistence and `App` implements
   platform pieces.
3. **Every data-changing service method starts with
   `_appLock.EnsureUnlocked()`**, so a page left open behind the lock
   screen can't write.
4. **Critical actions require a PIN confirmation inside the service**
   (§9.5), not just a prompt in the UI.
5. **Change events are raised only after the save succeeds.**
   `IChangeNotifier` raises `BookingsChanged(date)`, `OrderChanged(orderKey)`
   and `CatalogChanged()`. Pages subscribe and re-query, so a change in
   the detail sheet updates the Dashboard and Booking list.
6. **All "what day is it" logic goes through `IBusinessCalendar`**
   (business timezone `Asia/Manila`), never the device's timezone.
7. **Money is integer centavos everywhere** (`long`). Format to ₱ only for
   display.

### 5.1 Registration (`MauiProgram.cs`)

```csharp
var dbPath = Path.Combine(FileSystem.AppDataDirectory, "roastit.db3");
builder.Services.AddDbContextFactory<RoastItDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Foreign Keys=True"));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IBusinessCalendar>(new BusinessCalendar("Asia/Manila"));
builder.Services.AddSingleton<IChangeNotifier, ChangeNotifier>();
builder.Services.AddSingleton<IAppLock, AppLockService>();   // holds locked state + PIN windows

builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<DashboardService>();

builder.Services.AddSingleton<PreferencesStore>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<PinPromptService>();
builder.Services.AddSingleton<BackNavigationService>();
builder.Services.AddSingleton<ToastService>();
```

Use `IDbContextFactory` and create a short-lived context per operation;
Blazor components live too long for one shared DbContext.

---

## 6. Data model (SQLite)

### 6.1 Type conventions

| Kind of value | C# type | Stored as | Notes |
|---|---|---|---|
| Money (₱) | `long` centavos | `INTEGER` | ₱1,250.50 → `125050`. Sum, compare and sort work in SQL |
| Instants (pickup, started cooking, payment time) | `DateTime` (UTC) | `TEXT` | Convert with a UTC value converter. Don't use `DateTimeOffset`: EF can't compare or order it in SQLite |
| Business-local calendar dates | `DateOnly` | `TEXT` | `order_date`, `daily_counters.date` |
| Item status | `ItemStatus` enum | `INTEGER` | `Pending=0, Cooking=1, Cooked=2, Completed=3, Cancelled=4` (pipeline order, so status sort is a plain `ORDER BY`) |
| IDs | `Guid` | `TEXT` | Except `items.booking_id` (text, generated) |
| Audit details | `string` (JSON) | `TEXT` | `System.Text.Json` |

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder b)
{
    b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
}

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
```

At startup, once: `PRAGMA journal_mode=WAL;`.

### 6.2 Tables

**app_lock** (single row; no row means first run; **never included in backups**, §10.5)

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER PK | always 1 |
| `pin_hash` | TEXT | `PasswordHasher` hash of the 4–6 digit PIN |
| `recovery_code_hash` | TEXT | hash of the 12-character recovery code |
| `failed_attempts` | INTEGER | shared by the lock screen and PIN prompts |
| `locked_until` | TEXT (UTC) null | set after 5 wrong tries |
| `updated_at` | TEXT (UTC) | |

**products**

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT (Guid) PK | |
| `name` | TEXT | required |
| `price` | INTEGER | centavos, ≥ 0 |
| `cooking_time_min` | INTEGER | ≥ 1 |
| `description` | TEXT null | |
| `active` | INTEGER (bool) | default 1; **no delete, deactivate only** |
| `created_at` | TEXT (UTC) | |

**orders**

| Column | Type | Notes |
|---|---|---|
| `key` | TEXT (Guid) PK | |
| `customer` | TEXT null | blank = walk-in |
| `phone` | TEXT null | |
| `order_date` | TEXT (DateOnly) | business-local date of creation |
| `pickup_at` | TEXT (UTC) | indexed |
| `total_amount` | INTEGER | centavos; computed from catalog prices at creation, never rewritten |
| `discount_amount` | INTEGER | centavos, default 0, always clamped to `[0, total_amount]` |
| `amount_paid` | INTEGER | centavos; rollup of `payments`, updated in the same transaction |
| `notes` | TEXT null | |

**items** (one row per unit of product)

| Column | Type | Notes |
|---|---|---|
| `booking_id` | TEXT PK | `YYMMDD-NNN` (§7.1) |
| `order_key` | TEXT FK → orders | |
| `product_id` | TEXT FK → products | no cascade delete |
| `status` | INTEGER | `ItemStatus`; indexed |
| `started_cooking_at` | TEXT (UTC) null | stamped on `pending → cooking` |

**payments** (one row per payment)

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT (Guid) PK | |
| `order_key` | TEXT FK → orders | indexed |
| `amount` | INTEGER | centavos, > 0 |
| `recorded_at` | TEXT (UTC) | |

**daily_counters**: `date` TEXT (DateOnly) PK, `last_seq` INTEGER.

**audit_logs**

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT (Guid) PK | |
| `action` | TEXT | e.g. `payment.recorded`, `order.balance_waived`, `discount.set`, `item.cancelled`, `product.created`, `product.updated`, `product.active_changed`, `pin.changed`, `pin.reset_with_recovery` |
| `target_type` / `target_id` | TEXT | |
| `details` | TEXT (JSON) null | amounts before/after |
| `created_at` | TEXT (UTC) | |

**app_meta**: key/value: `app_id` (`roastline`), `schema_version`, `app_version`, `exported_at` (set on the exported copy only). Import checks these (§10.5).

### 6.3 Migrations

| Migration | Phase | Creates |
|---|---|---|
| `InitialAppLock` | 1 | `app_lock`, `audit_logs`, `app_meta` |
| `AddProductsCatalog` | 2 | `products` |
| `AddBookingsDomain` | 3 | `orders`, `items`, `payments`, `daily_counters` (foreign-key indexes only) |
| `AddPerformanceIndexes` | 4 | `orders(pickup_at)`, `items(status)`, `products(active)`, and covering `items(order_key, status, booking_id)` / `items(product_id, status)`, which replace the two foreign-key indexes on `items` (D21) |

```powershell
dotnet ef migrations add InitialAppLock -p src\RoastIT.Data -s src\RoastIT.Data
```

`RoastIT.Data` has an `IDesignTimeDbContextFactory` pointing at a local
dev file, because the MAUI project can't be the EF tools' startup project.

**At app start, before the first page loads:**

```csharp
using var db = await dbFactory.CreateDbContextAsync();
if (File.Exists(dbPath) && (await db.Database.GetPendingMigrationsAsync()).Any())
{
    var prev = VersionTracking.Default.PreviousVersion ?? "unknown";
    File.Copy(dbPath, $"{dbPath}.bak-before-{AppInfo.Current.VersionString}-from-{prev}", overwrite: true);
    // keep only the newest 3 backup copies
}
await db.Database.MigrateAsync();
await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
```

Rules:
- Never edit a migration that has shipped.
- Prefer additive changes. Dropping or altering columns makes SQLite
  rebuild the table, so test those on a copy of real data.

---

## 7. Business rules (`RoastIT.Core`)

### 7.1 Booking IDs

- Format `YYMMDD-NNN`: business-local date + 3-digit daily sequence,
  e.g. `261009-001`. **One ID per item**, not per order. An order of
  3 × Lechon Belly + 1 × Lechon Chicken gets 4 IDs.
- Claim a block of N numbers (N = total units in the new order) inside the
  same transaction as the order/item insert:
  ```sql
  INSERT INTO daily_counters (date, last_seq) VALUES (@today, @n)
  ON CONFLICT(date) DO UPDATE SET last_seq = last_seq + excluded.last_seq
  RETURNING last_seq AS "Value";
  ```
  The returned value is the last number in the block; the items get
  `returned - n + 1 … returned`. Run it with `Database.SqlQuery<int>(...)`
  and read the result directly; don't compose LINQ on top.
- `@today` = `IBusinessCalendar.Today()`. A rollback leaves a gap, never a
  duplicate.

### 7.2 Orders and money

```csharp
public static class OrderMath
{
    public static long NetTotal(Order o) => o.TotalAmount - Math.Min(o.DiscountAmount, o.TotalAmount);
    public static long Balance(Order o)  => Math.Max(0, NetTotal(o) - o.AmountPaid);
}
```

- **One shared calculation.** The Dashboard, Booking list, Balance due
  panel and detail sheet all use `NetTotal` / `Balance`. Nothing
  recomputes them on its own.
- **Creating a booking** (`BookingService.CreateAsync`):
  - Requires at least one line with a product and qty ≥ 1. Customer
    name is optional; blank shows as "Walk-in customer".
  - `total_amount` is **recomputed from current catalog prices** × qty.
    Never trust a total sent from the form.
  - **Reject inactive products**, even though the form only offers
    active ones.
  - Optional discount and "amount paid now" are both clamped to ≥ 0;
    the discount is also clamped to the total. A non-zero "paid now"
    also creates a `payments` row.
  - One `Item` per unit, each `Pending`, each with its own booking ID.
- **Discount** (`OrderService.SetDiscountAsync`): flat ₱ amount, clamped
  to `[0, total_amount]`. 0 removes it.
- **Record payment** (`OrderService.RecordPaymentAsync`): **additive**.
  Insert a `payments` row and add it to `amount_paid` in one transaction.
  Amount must be > 0. Needs a Bookings PIN confirmation (§9.5).
- **Discount remaining balance** (`OrderService.WriteOffBalanceAsync`):
  - The detail sheet offers it only when `0 < Balance < 10% of
    total_amount`. The percentage is of the gross total.
  - The service reads the order **as it is now** inside one transaction
    and sets `discount_amount = min(discount_amount + Balance, total_amount)`.
    It never trusts an amount from the UI.
  - Needs a Bookings PIN confirmation (§9.5).
- Write an `audit_logs` row for discounts, payments, write-offs and
  cancellations.

### 7.3 Item status

- Forward only, one step at a time: `Pending → Cooking → Cooked →
  Completed`.
- `pending → cooking` stamps `started_cooking_at = now (UTC)`.
- `AdvanceStatusAsync(bookingId, expectedStatus)` refuses if the item is
  no longer in `expectedStatus`. This protects against double taps and
  stale screens.
- UI: advancing from a **list card asks for confirmation** ("Mark booking
  261009-001 as Cooking?"). Advancing from the **detail sheet is
  immediate**.

**Cancelling is per order, never per item.**
- `OrderService.CancelOrderAsync(orderKey)` sets **every item in the
  order** to `Cancelled` in one transaction. Items that were already
  cancelled stay as they are.
- **When it's allowed:** the order has at least one `Pending` or
  `Cooking` item **and none** that is `Cooked` or `Completed`. Otherwise
  it's refused, and the detail sheet shows "This order can't be
  cancelled: part of it is already cooked or completed." This check runs
  again at save time, not only when the button is drawn.
- **Detail sheet button:** "Cancel order" when the order has more than one
  active item, "Cancel booking" when it has one. Either way it cancels
  the whole order.
- **PIN:** needs a Bookings PIN confirmation, remembered 3 minutes
  (§9.5). The prompt lists the booking IDs, e.g. "Cancel the whole order
  for The Garden Events Co.: 2 bookings (261009-001, 261009-002)." If
  money was already paid, it adds "₱2,000 was already paid — handle any
  refund outside the app."
- **Money:** `total_amount` never changes. A partial cancel can't happen,
  so a fully cancelled order simply drops out of revenue, collected and
  balance due (§7.6). Payments stay recorded in `payments` for history.
  Refunds are not tracked in the app.
- Write an `audit_logs` row (`order.cancelled`, with the booking IDs).

### 7.4 Dates and scope

- `IBusinessCalendar`:
  - `Today()` — today's date in Asia/Manila.
  - `DayRangeUtc(DateOnly)` — that day's start and end as UTC instants,
    for filtering `pickup_at`.
  - `ToLocal(DateTime utc)` — for display.
  - Uses `TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")`.
- **Schedulable window:** only dates from `Today() − 6 months` to
  `Today() + 6 months` can be selected. `BookingScopeState.SelectDate`
  rejects anything outside, whatever path tries it.
- **One scope for both tabs:** `BookingScopeState.SelectedDate` (null =
  all dates) drives both the Dashboard and the Booking list, and the
  shared label "Showing {Today | Yesterday | Tomorrow | Wed, Sep 30, 2026 |
  all dates}".
- **The page opens on today (D32).** Tapping the selected day, or the "All"
  chip, shows all dates.
- **"All dates" means the schedulable window (D32):** bookings with pickup
  from the start of `Today() − 6 months` to the end of `Today() + 6 months`.
  This applies to the list, the status chips and the Dashboard. Older
  bookings stay in the database, in exports and in audit history, but the
  Bookings page doesn't show them.
- The device clock is the only clock. Recommend automatic time on the
  device.

### 7.5 Booking list

- **Search:** booking ID, product name or customer label (so "walk-in"
  finds blank names), case-insensitive, combined with status and date.
- **Status chips** show **global** counts that ignore the selected date and
  the search, over the same ±6-month window as "all dates" (D32):
  All · Pending · Cooking · Cooked · Completed · Cancelled.
- **Sort:** Pickup time, Status (pipeline order), Booking ID, Customer,
  Product, Cooking time, each ascending or descending with its own labels
  (see the prototype's sort sheet).
- **Date headers** appear only when showing all dates sorted by pickup
  time. Otherwise the list is flat.
- **Views** (remembered per device):
  - **Detailed:** full cards showing the fields chosen in Settings.
  - **Compact:** booking ID + pickup time only. The time shows alone when
    the day is already clear; otherwise it's date + time. Tap a row to
    expand it into the full card, tap the card to open the detail, and
    use the arrow to collapse.

### 7.6 Dashboard (scoped to the selected date or all dates, i.e. today ± 6 months, D32)

- **KPI tiles:**
  - Total bookings (items)
  - Total revenue (sum of `NetTotal`)
  - Collected
  - Balance due
  - Avg. order value
  
  Orders whose items are **all** cancelled are left out of the money
  figures.
- **Status breakdown:** shown only when a specific date is selected.
- **Kitchen load:** shown only when a specific date that is **today or
  later** is selected. It counts pending and cooking items:
  - **Cooking item:** occupies `[started_cooking_at, + cooking time)`.
  - **Pending item:** occupies `[pickup − cooking time, pickup)`. If that
    start time has already passed, it occupies `[now, now + cooking
    time)` instead, because it's running late.
  - **Chart:** add +1 at each start and −1 at each end, sort the times,
    and keep a running total. Each step between two times is one bar,
    and its height is how many items are cooking then.
  - **Axis (D31):** fixed to the whole selected date, **12 AM to 11:59
    PM** in business time, one labelled tick per hour (12 AM … 11 PM).
    It's wider than the screen, so it scrolls sideways while the y-axis
    labels stay put. Bar parts outside the day are cut off; tapping a bar
    still shows its real times. Today shows a "now" line and opens on it;
    other dates open near the first busy bar.
  - **Labels:** in business time. Tapping a bar shows "{n} cooking ·
    {start} – {end}".
  - **Empty:** "No pending or cooking items scheduled for this date."
- **Product mix:** non-cancelled items per product, ranked.
- **Balance due:** the top 5 orders with `Balance > 0` by amount, then
  "+N more". Empty text: "Everyone's paid up in this view."

### 7.7 Menu

- **Fields:** name (required), price ≥ 0, cooking time ≥ 1 min,
  description (optional), active.
- **Validation** shows the first failing field inline.
- **Search** matches name or description. Status chips show global counts.
  Sort by Name, Price or Cooking time.
- **Deactivate, never delete.** Inactive products are hidden from the New
  booking form but stay on existing bookings.
- Add, save edits and activate/deactivate need a **Menu PIN confirmation
  every time** (§9.5).

---

## 8. Data access in services (sketch)

```csharp
public async Task RecordPaymentAsync(Guid orderKey, long amount, PinConfirmation confirmation)
{
    _appLock.EnsureUnlocked();
    _appLock.EnsureConfirmed(confirmation, PinScope.Bookings);   // §9.5
    if (amount <= 0) throw new ValidationException("Enter an amount greater than 0.");

    await using var db = await _dbFactory.CreateDbContextAsync();
    await using var tx = await db.Database.BeginTransactionAsync();
    var order = await db.Orders.SingleAsync(o => o.Key == orderKey);
    db.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderKey = orderKey, Amount = amount, RecordedAt = _clock.UtcNow });
    order.AmountPaid += amount;
    db.AuditLogs.Add(AuditLog.PaymentRecorded(order, amount, _clock.UtcNow));
    await db.SaveChangesAsync();
    await tx.CommitAsync();

    _changes.OrderChanged(orderKey);   // only after commit
}
```

---

## 9. App lock and PIN

### 9.1 The PIN

- One PIN for the whole app: **4–6 digits**, entered on an on-screen
  keypad. There are no user accounts.
- Stored only as a hash in `app_lock`, never in `Preferences`, logs or
  plain text.
- `AppLockService` keeps the locked/unlocked state **in memory**, so the
  app always starts locked.

### 9.2 Flows (see `signin-prototype.html`)

| Flow | Steps |
|---|---|
| **First run** (no `app_lock` row) | Enter PIN → confirm → **recovery code shown once** (Copy button; "I've written this code down" checkbox required) → Bookings |
| **Unlock** | Keypad → Bookings |
| **Forgot PIN** | Enter recovery code → new PIN → confirm → **new recovery code** shown (old one stops working) → Bookings |
| **Change PIN** (Settings → Update PIN) | Current PIN → new PIN → confirm → "PIN updated" → back to Settings. Recovery code unchanged |

- **Mismatched confirm:** "PINs didn't match. Choose your PIN again." and
  start over.
- **Messages** stay generic: "Incorrect PIN. 3 tries left."

### 9.3 Wrong attempts

- 5 wrong tries (PIN or recovery code) → input disabled for **30 seconds**
  with a countdown.
- `failed_attempts` and `locked_until` live in `app_lock`, so restarting
  the app doesn't reset them. The lock screen and the PIN prompts **share
  the same counter**.

### 9.4 Recovery code and auto-lock

- **Recovery code:** 12 characters shown as `XXXX-XXXX-XXXX`. It skips
  look-alikes (no 0/O, 1/I/L), is generated with a secure random source
  and stored hashed. Comparison ignores case, spaces and dashes.
- **Without it,** a forgotten PIN means reinstalling, which erases the
  data unless there's a backup. The screens say this plainly.
- **Auto-lock:**
  - **When:** on every reopen or return from the background (MAUI
    `Window.Resumed` / `Stopped`), and after **10 minutes** without
    taps.
  - **How:** `IdleTimer` resets on any pointer or key event in the main
    layout.
  - **Effect:** locking clears any PIN-confirmation window (§9.5).

### 9.5 PIN confirmation for critical actions

| Screen | Action | Prompt summary → button | Remembered |
|---|---|---|---|
| Menu | Add product | "Add Crispy Pata to the menu." → **Add** | No, every time |
| Menu | Save product edits | "Save changes to Lechon Belly." → **Save** | No, every time |
| Menu | Activate / Deactivate | "Hide Liempo from new bookings." → **Deactivate** | No, every time |
| Bookings | Record payment | "Record a ₱500 payment from Diego Santos (261008-002)." → **Record** | **3 minutes** |
| Bookings | Discount remaining balance | "Waive the remaining ₱150 for Diego Santos (261008-003)." → **Waive** | **3 minutes** |
| Bookings | Cancel booking / Cancel order (whole order, §7.3) | "Cancel the whole order for The Garden Events Co.: 2 bookings (261009-001, 261009-002)." → **Confirm** | **3 minutes** |
| Settings | Export data | "Export all bookings, payments and the menu to a backup file." → **Export** | No, every time |
| Settings | Import data | "Replace all data on this device with roastline-backup-….roastline." → **Replace** | No, every time |

Behavior:
- **Form validation runs first.** The prompt only appears for a valid
  action, and a ₱0 payment just closes the editor.
- **The prompt:** a bottom sheet with a lock icon, "Enter your PIN to
  continue", the one-line summary and a keypad. Bookings prompts add
  "Payments, write-offs and cancellations won't ask again for 3 minutes."
- **Cancel, back or tapping outside** saves nothing and leaves the form or
  editor open.
- **The 3-minute window:**
  - starts when a correct PIN is entered on a Bookings action
  - applies to any booking
  - isn't extended by later actions
  - is never started or used by Menu actions
  - is cleared when the app locks
- **Not PIN-protected:**
  - creating a booking
  - manual discount apply/edit/remove
  - advancing status

Implementation:

```csharp
public enum PinScope { Menu, Bookings, Data }   // Data = Settings export/import: single-use, like Menu

public interface IAppLock   // full contract: src/RoastIT.Core/Abstractions/IAppLock.cs
{
    bool IsLocked { get; }
    event Action? LockStateChanged;
    void EnsureUnlocked();                                    // throws AccessDeniedException
    Task<ConfirmResult> ConfirmAsync(string pin, PinScope scope); // Attempt (Success/Wrong + tries left/LockedOut) + Confirmation
    bool HasRecentConfirmation(PinScope scope);               // true only for Bookings within 3 min
    void EnsureConfirmed(PinConfirmation? c, PinScope scope); // throws PinRequiredException if missing/expired/used (Menu)
    void Lock();                                              // also clears the Bookings window and unused Menu confirmations
    // IsSetUpAsync, SetUpAsync, UnlockAsync, VerifyPinAsync, ChangeAsync,
    // VerifyRecoveryCodeAsync, ResetWithRecoveryCodeAsync, GetLockoutAsync
}
```

- A `Menu` or `Data` confirmation is **single-use**, only valid for its own
  scope, and expires after ~60 seconds.
- A `Bookings` confirmation opens a 3-minute window. Within it,
  `EnsureConfirmed(null, PinScope.Bookings)` passes.
- Every check that takes a PIN or recovery code returns a `PinAttempt`
  (status, tries left, `LockedUntil`) from the one persisted counter (§9.3).
- **UI side:** `PinPromptService.RequestAsync(summary, buttonLabel, scope)`
  returns a `PinPromptResult` (`Confirmed`, `Confirmation`). `Confirmed` is
  false when the user cancels or the app locks. It skips the prompt for
  Bookings when `HasRecentConfirmation(Bookings)` is true (then `Confirmation`
  is null and the service still accepts the call); otherwise it shows
  `<PinPrompt>`, which calls `ConfirmAsync`.

---

## 10. UI implementation

### 10.1 Styling and theme

- Port the prototypes' CSS into `wwwroot/css/app.css`. Copy the
  `:root` tokens, the `:root[data-theme="dark"]` tokens and the
  component styles: sheets, chips, pills, cards, keypad, switch,
  segmented control, toast.
- Status colors:

  | Status | Light | Dark |
  |---|---|---|
  | Pending | `#8A8275` | `#A39A8A` |
  | Cooking | `#D9731E` | `#E8893A` |
  | Cooked | `#2E8B52` | `#4CAF72` |
  | Completed | `#6B7280` | `#959CA8` |
  | Cancelled | `#C1392B` | `#E25B4D` |

  Accent: `#A8471F` (light), `#C9612F` (dark).
- Fonts: IBM Plex Sans + IBM Plex Mono. **Bundle the font files in
  `wwwroot`**, because the app must work offline.
- `ThemeService` reads `Preferences["theme"]`: `light` (default), `dark`
  or `system`.
  - **Light / Dark:** set `data-theme` and `color-scheme` on `<html>`
    through JS interop, and set `Application.Current.UserAppTheme` to
    `AppTheme.Light` / `AppTheme.Dark` so the status bar and splash match.
  - **System ("Device" in Settings):** set `UserAppTheme =
    AppTheme.Unspecified`, read `Application.Current.RequestedTheme`, and
    subscribe to `Application.Current.RequestedThemeChanged`. The app then
    switches live when the phone changes between light and dark.

### 10.2 Layout and mobile behavior

- **`MainLayout`:**
  - sticky top bar (brand mark, page title, subtitle)
  - page content, max width 760 px
  - fixed bottom nav: Bookings · Menu · Settings
  - safe-area padding (`env(safe-area-inset-*)`)
- **`LockLayout`:** centered column, no nav.
- **Full-screen sheets** (booking detail, new booking, product add/edit)
  become a 440 px right-side panel on widths ≥ 768 px. **Bottom sheets**
  are used for sort, calendar and PIN prompt.
- **Back button:** `BackNavigationService` keeps a stack of open layers
  (dialog → bottom sheet → full sheet). Android back (and Escape on
  Windows) closes the top layer first and never exits the app from inside
  a sheet. Hook it from the MAUI page's `OnBackButtonPressed`.
- **Touch and typing:**
  - Touch targets ≥ 44 px.
  - Inputs use 16 px text, so iOS doesn't zoom in.
  - Typing must not re-render the input being typed in: bind on
    `@oninput` to state, and only re-render dependent parts (results,
    totals).
- **Keyboard:** the focused input in a sheet scrolls into view when the
  on-screen keyboard opens.
- **Toasts:** shown above the sheet footer when a sheet is open. Hidden
  while a bottom sheet or PIN prompt is open.

### 10.3 Screens (behavior per prototype)

**Lock:** see §9.2–§9.3.

**Bookings**
- **Date strip:** an "All" chip, then a swipeable 10-day window (starting
  2 days before today) with prev/next-week ends and count badges, then a
  calendar button.
  - The calendar bottom sheet has month navigation, "Today" and "Clear
    date", and a caption with the scheduling window.
  - Navigation stops at the ±6-month edges.
- **"Showing {scope}"** label, then a **Dashboard | Booking list**
  segmented control (default: Booking list).
- **Booking list:** search, status chips, a count, the **Detailed/Compact**
  switch and a Sort button. In Detailed view, cards show Booking ID
  (always) plus the fields chosen in Settings, and a quick-advance button
  (with confirm) when there's a next status.
- **Detail sheet**
  - **Header:** back, booking ID, status pill.
  - **Booking:** product, cooking time, pickup.
  - **Customer:** name, and phone as a `tel:` link (or "Not on file").
  - **Payment:**
    1. total
    2. "+ Apply discount" or the discount row with Edit/Remove
    3. net total
    4. amount paid
    5. "+ Record payment"
    6. balance due
    7. the write-off banner when the balance is under 10%
  - Only one inline editor (discount or payment) is open at a time.
  - **Also in this order:** sibling items, tappable.
  - **Footer:** "Cancel order" / "Cancel booking" (whole order, PIN,
    only while nothing in the order is cooked or completed — §7.3) and the
    advance button, which is immediate.
- **New booking sheet**
  - Customer (optional, "Leave blank for a walk-in"), phone (optional)
    and pickup date & time.
  - **Item lines:** a swipeable row of product cards (active only), a
    − / qty / + stepper, the subtotal, and Remove (Reset on the last
    line). "+ Add another item" adds a line.
  - **Summary:** order total, discount, net total, amount paid now.
  - **Submit:** "Create booking · ₱{net}". A toast lists the created IDs.
- **FAB:** "New booking".

**Menu**
- Search, Active/Inactive chips with global counts, and a sort sheet.
- **Product cards:** name, status pill, price (mono), cooking time,
  2-line description, Edit and Activate/Deactivate. Inactive cards are
  dimmed.
- **Add/Edit sheet:** name, price, cooking time, description and the
  "Available for booking" switch, with an inline error on the first
  invalid field.
- Add/Edit/toggle each ask for the PIN.
- **FAB:** "Add product".

**Settings**
- **Security:** "Update PIN" (opens the lock route in change mode) and a
  note on auto-lock and where the PIN is asked.
- **Data:** "Export data" (subtitle shows the last export time) and
  "Import data" (§10.5). A warning appears above them when there's been no
  export in the last 7 days, or ever.
- **Automatic CSV backup** card (§10.6): what the file holds, where it
  is, how often it's saved, and the file itself (name, last saved time,
  number of bookings, the pickup window it covers) with a Share/Open
  button.
- **Appearance:** Light / Dark / Device (matches the phone and follows it live), applied immediately.
- **Bookings list · Columns to show:** switches for Customer, Product,
  Cooking time, Pickup, Status, Total amount and Amount paid, plus "Show
  all". Booking ID is always shown.
- App version at the bottom.

### 10.4 State and preferences

| Item | Where | Notes |
|---|---|---|
| `BookingScopeState` | scoped service | `SelectedDate`, `ActiveTab`, strip start, calendar month |
| List filters/sort/search | page state | reset when the app restarts |
| Theme | `Preferences["theme"]` | `light` / `dark` / `system`, default light |
| Columns to show | `Preferences["bookingColumns"]` | JSON of 7 booleans, all true by default |
| List view | `Preferences["bookingListView"]` | `detailed` / `compact`, default detailed |
| PIN, recovery code, lockout | SQLite `app_lock` | never in Preferences |
| Last export time | `Preferences["lastExportAt"]` | ISO UTC; drives the 7-day backup warning |
| CSV last saved | `Preferences["lastCsvAt"]` | ISO UTC; drives the hourly CSV save (§10.6) |
| CSV row count | `Preferences["lastCsvCount"]` | bookings in the last CSV save, for the Settings card |
| CSV file | `Preferences["csvUri"]` | MediaStore URI of the CSV row, so later saves skip the lookup |

### 10.5 Export and import data

Everything lives on one device, so export/import is the only way to keep a
backup or move to a new phone. Both are in **Settings → Data**, and both
ask for the **PIN every time** (§9.5).

**The PIN never travels with a backup.** Backup files contain business
data only. The PIN hash, recovery-code hash and lockout state stay on the
device that made them, so a backup file on its own can't be used to learn
or replace anyone's PIN. Importing keeps the importing device's own PIN
and recovery code.

**Export**
1. The sheet lists what's included:
   - bookings, items, payments and discounts
   - the menu
   
   It also says what's not included: the **PIN and recovery code**
   ("they never leave this device"), and theme, columns and list view,
   which are per-device `Preferences`.
2. After the PIN, write a consistent copy of the database with
   `VACUUM INTO '<cache>/roastline-backup-YYYYMMDD-HHmm.roastline'`. It
   includes everything in the WAL, so no separate checkpoint is needed.
3. Open the copy and strip the lock data:
   - `DELETE FROM app_lock;`
   - set `app_meta.exported_at`
   - run `VACUUM` on the copy, so the deleted hash isn't left behind in
     free pages
   
   The copy is a throwaway file in the cache folder, so this never
   touches the live database.
4. Deliver it. There are two buttons (D33):
   - **Export backup** hands it to the share sheet
     (`Share.Default.RequestAsync(new ShareFileRequest { File = new ShareFile(path) })`)
     so the owner can save it to Google Drive, email it, or copy it to a
     computer.
   - **Save to a folder…** (Android) opens the system "save as" screen
     (`ACTION_CREATE_DOCUMENT`, suggested name = the file name). The owner
     picks the folder and may rename the file; the app copies the backup
     into that one file and deletes the cache copy. No storage permission
     is needed and no folder is remembered. If the owner backs out, nothing
     is saved and the sheet stays open.
5. Save `lastExportAt` in Preferences and write an `audit_logs` row
   (`data.exported`). With the share sheet `lastExportAt` is set once the
   sheet has opened (Q29); with **Save to a folder…** only after the file
   was written. The audit row is written when the copy is made, so it also
   exists if the owner backs out of the "save as" screen.

The `.roastline` file is an ordinary SQLite database with a custom
extension.

**Import**

> **Decision: import always replaces; it never appends or merges.**
> Appending would mean handling duplicates, booking-ID clashes between
> devices (`YYMMDD-NNN` is numbered per device), conflicting edits to the
> same order, and duplicate menu items. That is effectively a sync
> engine, and the app is single-device. If appending is ever needed, the
> only safe form is "add missing records only" (match by internal IDs,
> never change existing rows, renumber clashing booking IDs, preview
> first). Treat it as a separate feature, not a change to Import.

1. Show a warning that importing replaces all bookings, payments and menu
   items, and that a safety copy is saved first.
2. The owner picks a file (`FilePicker.Default.PickAsync()`). Copy it to
   the cache folder, open it **read-only**, and validate it:
   - It opens as SQLite, and `PRAGMA integrity_check` returns `ok`.
     Otherwise show "This file isn't a Roastline backup."
   - `app_meta.app_id = 'roastline'`.
   - `schema_version` ≤ the app's current schema. A newer one is refused:
     "This backup was made by a newer version of Roastline (x.y.z).
     Update the app first, then import it."
3. Show a summary: file name, when it was made, app version, and counts of
   orders, bookings, payments and products, plus "Your PIN and recovery
   code stay the same."
4. **Warn when the phone already has bookings.**
   - If the live database has any bookings (`SELECT COUNT(*) FROM items`
     > 0), show a warning above the button: "This phone already has 20
     bookings. Importing replaces them with the bookings in this backup.
     Export a backup first if you want to keep them."
   - It's a warning only. There's no checkbox, and the button stays
     enabled; the Replace button plus the PIN are the confirmation.
   - On an empty phone (e.g. a fresh install), there's no warning.
5. The owner taps **Replace data on this device**, then enters the PIN.
6. Replace the database:
   1. Read the current `app_lock` row into memory.
   2. Close all DbContexts and call `SqliteConnection.ClearAllPools()`.
   3. Copy the current DB to `roastit.db3.bak-before-import-<timestamp>`.
   4. Delete `-wal`/`-shm`, copy the backup in as `roastit.db3`, and run
      `Database.MigrateAsync()` so an older backup is upgraded.
   5. Write the saved `app_lock` row back in, replacing anything the file
      had. Older backups made before this rule may still contain one.
7. Write `audit_logs` (`data.imported`, with the file's `exported_at`),
   reload the app's data and open **Bookings**. The app stays unlocked,
   because the PIN didn't change.
8. If anything fails after the swap, restore the safety copy and show an
   error. The device must never end up half-imported.

### 10.6 Automatic CSV backup (bookings within 48 hours, Android)

A readable copy of the bookings around **now**, saved automatically so the
owner can still see who's picking up what if the app or database breaks
between exports. It is **not** a restorable backup: Import (§10.5) only
reads `.roastline` files, and the exported `.roastline` file stays the
real backup (D14). Android only for now (D30).

**The file**
- **One file: `roastline-bookings-48h.csv`**, overwritten on every save.
  There are no per-date files and nothing older or further ahead is kept.
- Location: **`Documents/Roastline/`**, written through MediaStore
  (`MediaStore.Files`, `RELATIVE_PATH = "Documents/Roastline"`). No
  storage permission is needed on Android 10+; the owner finds it in the
  Files app → Documents → Roastline; it survives an uninstall.
- **What's in it:** every booking (item) whose **pickup time is from 24
  hours before to 24 hours after the moment of the save**, cancelled
  included, sorted by pickup time then booking ID. Example: a save at
  Nov 4, 2:40 PM covers pickups from Nov 3, 2:40 PM to Nov 5, 2:40 PM.
  The query is one range on `orders.pickup_at` (UTC):
  `nowUtc - 24h <= pickup_at <= nowUtc + 24h`.
- Columns: Booking ID, Order, Customer, Phone, Product, Status, Pickup
  date, Pickup time, Cooking time (min), Started cooking, Order total,
  Order discount, Order net total, Order paid, Order balance, Notes.
  - Money is pesos with 2 decimals (`4400.00`), no ₱ sign or thousands
    separator, so spreadsheets read it as numbers.
  - The money columns are **per order** and repeat on each booking row of
    that order (the header says "Order …" so nobody sums them per row).
    A cancelled order shows a balance of `0.00` (D23).
  - Dates/times are Manila local time (`IBusinessCalendar`).
- Format: UTF-8 **with BOM** (so Excel shows "ñ" correctly), comma
  separated, CRLF line endings, RFC 4180 quoting (fields with `,` `"` or
  a line break are quoted, `"` doubled). Cells starting with `=`, `+`,
  `-` or `@` get a leading `'` so a customer name can't run as a formula.

**When it's saved: hourly, only while the app is open (D29, D27)**
- The file is rewritten when it was last saved **1 hour ago or more**, or
  never. That's checked when the app starts, when it comes back to the
  front (`Window.Resumed`), and every minute on a foreground timer that
  stops on `Window.Stopped`.
- **Nothing runs while the app is closed or in the background**, and
  nothing is saved when the app goes to the background.
- **No save after individual changes.** A new booking, payment or status
  change shows up in the file at the next hourly save, so the file can be
  up to an hour behind.
- After an **Import** there's no special save; the file catches up at the
  next hourly save.

**Implementation**
- `CsvBackupService` in `RoastIT.Core` (so the hourly rule is tested), over
  `ICsvFileStore` (Android MediaStore in the app) and `ICsvBackupLog`
  (`lastCsvAt` / `lastCsvCount` in Preferences):
  - `Task CheckAsync()`: reads `lastCsvAt`; if it's due, runs
    `SaveAsync()`.
  - `Task SaveAsync()`: queries the 48-hour window (one read,
    `AsNoTracking`, items joined to orders), builds the text with a small
    pure `BookingsCsvWriter` (unit-tested), hands it to the file store,
    then sets `lastCsvAt`.
  - Started from `App`/`MainPage`: call `CheckAsync()` on start and on
    `Window.Resumed`, and run a 1-minute `PeriodicTimer` while the window
    is active (cancel it on `Window.Stopped`).
- `ICsvFileStore` with the Android implementation in
  `Platforms/Android`: look up the app's own row for that display name
  and relative path in `MediaStore.Files`; if found, open it with mode
  `"wt"` (truncate) and write; otherwise insert a new row and write.
  Remember the row's URI in Preferences (`csvUri`) to skip the lookup.
- Runs off the UI thread and **never blocks or fails a booking save**. A
  failed write is logged (`csv.save_failed`, with the error) and shows
  "Last CSV save failed — tap to retry" on the Settings card.
- Skipped while the lock screen is showing; the next check after unlock
  catches up.

**Known limits**
- After an uninstall and reinstall, Android treats the existing file as
  belonging to the old install, so the app can't overwrite it and
  creates `roastline-bookings-48h (1).csv` instead. The owner can delete
  the old file in the Files app. Mention this on the card's help text.
- The file is plain text in a folder the owner can browse, so anyone
  holding the unlocked phone can open it. On Android 13+ other apps can't
  read another app's non-media files in Documents without "All files
  access"; on Android 10–12, apps with storage permission can. It
  contains customer names, phone numbers and notes, but not the PIN.
- The file is on the same phone, so it doesn't help if the phone is lost
  or broken. Only an exported `.roastline` file saved elsewhere covers
  that.

---

## 11. Logging and errors

- Serilog file sink in `AppDataDirectory/logs`, rolling daily, kept 14
  days. Log each service action by name, plus failures.
- **Never log** the PIN, its hash or the recovery code.
- `audit_logs` (in SQLite) is the business history. The log files are for
  debugging only.
- Wrap page content in `<ErrorBoundary>`, and add MAUI unhandled-exception
  handlers that log and show a friendly message.
- Services throw typed exceptions (`ValidationException`,
  `AccessDeniedException`, `PinRequiredException`, `StaleStateException`).
  Components map them to inline errors or toasts.

---

## 12. Testing

**Unit and integration tests** (`RoastIT.Tests`, real SQLite in-memory
with `Data Source=:memory:` kept open, or a temp file):
- Booking IDs: per-day sequence, multi-unit blocks, rollover at **Manila**
  midnight (not UTC), and a gap (not a duplicate) after a rollback.
- `OrderMath`: discount clamping, a fully discounted-to-paid order
  dropping out of Balance due, and cancelled-only orders left out of
  revenue.
- Create booking: total recomputed from the catalog, inactive product
  rejected, one item per unit.
- Status: forward-only, `expectedStatus` guard, `started_cooking_at`
  stamped.
- Payments: additive, a `payments` row is written, and the call is
  rejected without a Bookings confirmation.
- Write-off: uses current values, and is offered only when the balance is
  under 10%.
- Kitchen load: interval rules, including late pending items.
- Schedule window: dates outside ±6 months are rejected.
- **App lock:**
  - setup, unlock, change and recovery reset (old code invalid afterwards)
  - lockout after 5 tries, persisted across restarts
  - the Menu confirmation is single-use
  - the Bookings window expires after 3 minutes and is cleared by `Lock()`
- Cancel order: cancels every item; refused when any item is cooked or
  completed (checked at save time); refused without a Bookings PIN
  confirmation; a fully cancelled order drops out of revenue, collected
  and balance due.
- **CSV backup:**
  - `BookingsCsvWriter`: header, BOM, CRLF, quoting of `,` `"` and line
    breaks, formula-prefix guard, 2-decimal money, cancelled balance 0,
    sort order
  - window with a fake `IClock`: pickups exactly 24 h before and after are
    included, 24 h + 1 min either side are not, including across Manila
    midnight
  - hourly rule: no save at 59 min, save at 60 min, save when never saved
  - a booking change doesn't trigger a save
  - a failing file store doesn't fail the booking save

**Manual checks on a real Android device** (every phase that touches
UI):
- keyboard behavior in sheets
- back button closing layers
- dark mode
- date/time pickers
- the release build (trimming can break EF Core)
- **upgrade install over the previous APK** with existing data

---

## 13. Build phases

Each phase ends with a checkpoint that must pass before moving on.

### Phase 0 — Foundation
- Environment (§3), solution scaffold (§4.1), `CLAUDE.md`, docs copied
  into `docs/`.
- Port the CSS tokens and shared components: Sheet, BottomSheet,
  ConfirmDialog, Keypad, Switch, Segmented, StatusPill, FilterChips,
  Toast, EmptyState.
- `MainLayout`, `ThemeService`, `BackNavigationService`, bundled fonts.
- `RoastItDbContext` with the UTC converter, startup migration with
  backup copy, WAL, logging.

**Checkpoint:**
- The app runs on the emulator and a phone with the bottom nav and three
  empty pages.
- Light/Dark switches.
- Back closes a test sheet.

### Phase 1 — App lock
- `app_lock` + `InitialAppLock` migration, `AppLockService`, Lock route
  (setup, unlock, forgot, change), lockout, auto-lock, `PinPrompt` +
  `PinPromptService`, Settings → Update PIN.

**Checkpoint:**
- A fresh install forces PIN setup and shows the recovery code.
- Wrong PINs lock for 30 s, and the lockout survives a restart.
- The recovery code resets the PIN and issues a new code.
- The app locks on reopen and after 10 minutes.
- Pages can't write while locked.

### Phase 2 — Menu
- `products` migration, `ProductService`, Menu page, product sheet, PIN
  on add/edit/toggle.

**Checkpoint:**
- Add, edit and toggle all ask for the PIN every time.
- Validation messages match.
- Inactive products stay listed under "Inactive".

### Phase 3 — Bookings
- `AddBookingsDomain` migration, `IBusinessCalendar`, booking-ID
  allocation, `BookingService`, `OrderService`, `DashboardService`,
  `IChangeNotifier`.
- Date strip + calendar, Booking list (Detailed/Compact, search, chips,
  sort, grouping), detail sheet (discount, payment, write-off, siblings,
  advance/cancel), New booking sheet, Dashboard (KPIs, status breakdown,
  kitchen load, product mix, balance due).
- PIN on record payment, write-off and cancel order, with the 3-minute window.

**Checkpoint:**
- Creating multi-item bookings gives sequential per-item IDs.
- Search for "walk-in" finds blank names.
- Payments add up and are logged.
- Write-off zeroes the balance using current values.
- A second payment within 3 minutes doesn't ask; after 3 minutes or a
  lock it does.
- The Dashboard updates after changes made in the detail sheet.
- The kitchen chart hides for past dates.
- Cancelling from any item cancels the whole order after the PIN, and is
  blocked once part of the order is cooked or completed.

### Phase 4 — Settings, backup and hardening
- Settings: Columns to show and list-view preference wired into Bookings.
- **Settings → Data:** Export data and Import data exactly as in §10.5
  (PIN every time; backups never contain the PIN), plus the 7-day "no
  recent backup" warning.
- Export logs.
- **Automatic CSV backup (§10.6):** `CsvBackupService`, `BookingsCsvWriter`,
  the Android MediaStore file store, the foreground timer +
  `Window.Resumed` check, and the Settings card.
- **Indexes and performance (D21):** add the indexes, `<Virtualize>` the Booking list, compute Dashboard totals in SQL, and time the main screens with ~10 years of fake data on the cheapest target phone.
- Turn **off** Android Auto Backup (`android:allowBackup="false"` in `AndroidManifest.xml`). Backups are manual export/import only.

**Checkpoint:**
- A backup made on one install imports on a fresh install (after its
  own PIN setup) with all data, and the device **keeps its own PIN**.
- An exported file contains no `app_lock` row and no leftover hash bytes
  (check with a hex search for the stored hash).
- Importing on a phone that already has bookings shows the "This phone
  already has N bookings…" warning. On an empty phone there's no warning.
- A non-Roastline file, a corrupt file and a backup from a newer schema
  are all refused without touching current data.
- A failed import (e.g. force-close during the swap) leaves the previous
  data intact via the safety copy.
- `Documents/Roastline/roastline-bookings-48h.csv` appears when the app
  opens, opens in Excel/Sheets with names (incl. "ñ") and amounts
  correct, holds exactly the pickups from 24 h before to 24 h after the
  save, and isn't rewritten again until an hour later.
- With the app in the background for over an hour, nothing is written
  until it's brought back to the front.
- Settings persist across restarts.

### Phase 5 — Release
- See §14. First signed APK installed on the shop's device; upgrade test
  passes.

---

## 14. Build, install and update

**Signing key** (create once, keep outside the repo, back it up). Losing
it means the installed app can never be updated:

```powershell
keytool -genkeypair -v -keystore roastit.keystore -alias roastit -keyalg RSA -keysize 2048 -validity 10000
```

**Release APK:**

```powershell
$env:ROASTIT_KEY_PASS = "<keystore password>"
dotnet publish src\RoastIT.App -f net10.0-android -c Release `
  -p:AndroidPackageFormats=apk `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=D:\Keys\roastit.keystore `
  -p:AndroidSigningKeyAlias=roastit `
  -p:AndroidSigningKeyPass=env:ROASTIT_KEY_PASS `
  -p:AndroidSigningStorePass=env:ROASTIT_KEY_PASS
```

The output is `com.roastit.app-Signed.apk` under
`bin\Release\net10.0-android\publish\`.

**Install:**
1. Copy the APK to the device (USB, Drive, or `adb install -r`).
2. Allow "Install unknown apps" for the app used to open it.
3. Install, then complete the first-run PIN setup.
4. Keep **Android System WebView** up to date.

**Versioning:**
- `ApplicationDisplayVersion` = `MAJOR.MINOR.PATCH` (what people see).
- `ApplicationVersion` = an integer that **must increase every release**.
- `ApplicationId` and the keystore never change.
- Tag each release in Git and add a line to `CHANGELOG.md`.

**Updating:**
1. Install the newer APK over the old one.
2. On first launch, pending migrations run after an automatic DB backup
   copy (§6.3).
3. Always test the upgrade on a device with the previous version and real
   data before handing it over.
4. **Uninstalling deletes all data**, so export a backup first.

---

## 15. Open decisions and decision log

**How to use this section**
- New question → add a row to **15.1 Open decisions** with the next
  `Q` number.
- Once answered → move it to **15.2 Decision log** with the next `D`
  number, the date, and the section that now describes it. Update that
  section too.
- To change a past decision → add a **new** `D` row that says which one it
  replaces. Don't edit the old row, so the history stays readable.

### 15.1 Open decisions

| # | Question | Options / current assumption | Raised |
|---|---|---|---|
| Q8 | After unlocking from auto-lock, where does the app open? | **Current assumption: always Bookings** (signin spec §1). Alternative: return to the previous route (open sheets and unsaved forms can't be restored either way) | 2026-11-07 |
| Q9 | Setup is saved when the owner taps **Save** on the confirm view, before the recovery code is acknowledged. If the app is killed on the recovery-code view, the PIN exists but the owner may not have the code | **Current assumption: accept the risk.** Alternative: save the PIN and code hash only when **Continue** is tapped (signin spec §12.3) | 2026-11-07 |
| Q10 | Android Back on the lock screen | **Implemented as the spec's suggestions:** Unlock/first-run setup → send the app to the background; change flow → Cancel; Forgot → Back link; recovery-code view → does nothing (signin spec §12.2) | 2026-11-07 |
| Q11 | Locking on `Window.Stopped`/`Resumed` (D6) will also fire when the **file picker** (Import) or **share sheet** (Export) takes the screen, so the app would be locked mid-import and when returning | **Implemented (Phase 4): `ExternalActivityGuard`.** Just before the picker or a share sheet opens, the app expects one trip away; the next stop/resume within **5 minutes** doesn't lock. Staying away longer, or leaving for anything else afterwards, locks as usual. The idle lock (10 min) still applies. The app stays unlocked after an import (§10.5 step 7) | 2026-11-07 |
| Q12 | Menu: **cooking time decimals** (menu spec §10.1). The prototype accepts `1.5`; the column is an integer | **Implemented: whole minutes only.** `1.5`, text and blanks all show "Cooking time must be at least 1 minute." | 2026-11-07 |
| Q13 | Menu: **price centavos** (menu spec §10.2). The prototype steps by whole pesos | **Implemented: up to 2 decimals** (step 0.01), stored as centavos. A third decimal is rejected with the price message. Display drops ".00" for whole pesos (₱2,200) | 2026-11-07 |
| Q14 | Menu: **duplicate product names** (menu spec §10.3) | **Implemented: allowed** (as in the prototype). Alternative: warn or block, case-insensitive and trimmed | 2026-11-07 |
| Q15 | Menu: **field limits** (menu spec §10.4) | **Implemented the spec's suggestion:** name ≤ 60, description ≤ 300, cooking time ≤ 1,440 min, plus price ≤ ₱10,000,000 (my addition, to keep totals far from overflow). Each has its own message in `ProductMessages` | 2026-11-07 |
| Q16 | Menu: **unsaved changes** when closing the product sheet (menu spec §10.5) | **Implemented: discard silently**, as in the prototype. Alternative: ask "Discard changes?" when dirty | 2026-11-07 |
| Q17 | Menu: **error placement** (menu spec §10.6). Prototype: one line under all fields; plan §7.7: "inline" on the first failing field | **Implemented: the prototype's single line under the fields, and the first failing field is focused.** Alternative: show the message under that field | 2026-11-07 |
| Q18 | Menu: **sort ties** (menu spec §10.7) | **Implemented the spec's suggestion:** ties on price or cooking time sort by name A → Z; identical names by oldest first | 2026-11-07 |
| Q19 | Menu: editing a product without changing anything, then confirming the PIN | **Implemented: nothing is written, audited or announced**; the toast still says "Saved {name}". The PIN is still consumed. Likewise deactivating an already inactive product is a no-op | 2026-11-07 |
| Q20 | Menu: audit row when an edit also flips the "Available for booking" switch | **Implemented: one `product.updated` row** whose details list every changed field, including `active` before/after (no extra `product.active_changed` row, since it is one save with one PIN). Card Activate/Deactivate writes `product.active_changed` | 2026-11-07 |
| Q21 | Bookings: **money actions on a fully cancelled order** (bookings spec §14.1) | **Implemented the spec's suggestion.** `SetDiscountAsync`, `RecordPaymentAsync` and `WriteOffBalanceAsync` refuse ("This order is cancelled."); the detail sheet hides *+ Apply discount*, Edit/Remove, *+ Record payment* and the write-off banner, and shows Balance due ₱0 (`BookingRow.BalanceDue`); the list card's money line reads *Cancelled · ₱0* | 2026-11-07 |
| Q22 | Bookings: **overpayment** (bookings spec §14.2) | **Implemented: allowed.** The payment editor has no upper limit and `amount_paid` can exceed the net total; Balance floors at ₱0. Alternative: cap at the balance, or warn | 2026-11-07 |
| Q23 | Bookings: **"Amount paid now" on New booking** (bookings spec §14.3) | **Implemented the plan: clamped to ≥ 0 only**, not capped at the net total (same as Q22) | 2026-11-07 |
| Q24 | Bookings: **pickup validation** (bookings spec §14.4) | **Implemented: required** (blank shows "Choose a pickup date and time."), **default now + 5 hours** in business time **with the minutes set to 00** (11:42 → 4:00 PM; the picker always holds a value, so the blank message can't appear now) (the prototype used tomorrow 10:00; changed at the owner's request), **past pickups allowed**, and `CreateAsync` **enforces today ± 6 months** ("Pickup must be within 6 months of today.") whatever the picker allows | 2026-11-07 |
| Q25 | Bookings: **"Latest first" in the grouped view** (bookings spec §14.5) | **Implemented as the spec describes:** the day groups reverse, bookings inside a day stay earliest-first. Alternative: reverse both | 2026-11-07 |
| Q26 | Bookings: **sort ties** (bookings spec §14.6) | **Implemented: booking ID A → Z is the secondary key for every sort** (always ascending, even when the main sort is descending). Dashboard product-mix ties sort by product name A → Z (the catalog has no manual order) | 2026-11-07 |
| Q27 | Bookings: **notes and input limits** (bookings spec §14.7) | **Notes: no field and no row in Phase 3** (the `orders.notes` column exists, stays empty, and the Phase 4 CSV can include it). **Limits, my addition:** customer ≤ 80, phone ≤ 30, **at most 100 bookings (units) in one order**, each with its own message in `BookingMessages`, so a mistyped quantity can't create thousands of rows | 2026-11-07 |
| Q28 | Bookings: **audit rows for the discount and payment entered while creating a booking** | **Implemented: `discount.set` and `payment.recorded` rows (with `atCreation: true`)** are written in the create transaction, so every payment has a log row. Creating the booking itself writes no audit row. Setting a discount to the value it already has writes nothing | 2026-11-07 |
| Q29 | Settings: **when does an export count** for "Last export" and the 7-day reminder (settings spec §10 #2)? Android can't tell whether the share target saved the file | **Implemented: once the file is made and the share sheet has opened.** If either fails, `lastExportAt` isn't touched. Alternative: only after the share sheet returns without cancel (not reliable on Android) | 2026-11-07 |
| Q30 | Settings: **reminder threshold** (settings spec §10 #6) | **Implemented: more than 7 × 24 hours since the exact last-export time.** Alternative: calendar days | 2026-11-07 |
| Q31 | Settings: **do import counts include cancelled bookings** (settings spec §10 #7)? | **Implemented: yes.** "Contains … bookings" and "This phone already has N bookings" both count every item, cancelled included (the plan's `SELECT COUNT(*) FROM items`) | 2026-11-07 |
| Q32 | Settings: **import file types** (settings spec §10 #5) | **Implemented: the picker accepts any file**, and only the content decides (SQLite, `integrity_check`, `app_id`, schema). A renamed backup still imports; a non-backup named `.roastline` is still refused | 2026-11-07 |
| Q33 | CSV: **Android 7–9** (API 24–28). MediaStore `RELATIVE_PATH` needs Android 10 | **Implemented: no CSV before Android 10**; the card says "The automatic CSV backup needs Android 10 or later." Alternative: the legacy public Documents folder with a storage permission | 2026-11-07 |
| Q34 | CSV: **column contents** the plan leaves open | **Implemented:** *Order* = the order's first booking ID; a blank customer is *Walk-in customer*; *Pickup date* `2026-11-08`, *Pickup time* `5:30 PM`, *Started cooking* `2026-11-08 1:05 PM` (all Manila); *Order discount* is the clamped discount; *Notes* is empty (no notes field yet, Q27) | 2026-11-07 |
| Q35 | Import: **safety copies and a cut-off swap** | **Implemented:** the newest **3** `roastit.db3.bak-before-import-*` copies are kept. The swap writes a marker first; if the app dies before the marker is removed, the next start puts the safety copy back. That includes the rare case where the new file was already in place, so that import has to be done again | 2026-11-07 |
| Q37 | Settings: **where export/import errors show.** The spec says an error toast, but toasts are hidden while a bottom sheet is open (shared-ui-spec) | **Implemented: an inline error line in the open sheet** (`role="alert"`), and the sheet stays open | 2026-11-07 |

### 15.2 Decision log

| # | Decision | Decided | See |
|---|---|---|---|
| D1 | **Standalone app**: .NET MAUI Blazor Hybrid + SQLite, offline; Android first, then Windows, iOS later | 2026-11-04 | §1, §2 |
| D2 | **One shared device, no sync server** | 2026-11-04 | §1 |
| D3 | **Screens**: Lock, Bookings, Menu, Settings. No user accounts, roles or Admin Manager. Bottom nav: Bookings · Menu · Settings | 2026-11-04 | §1, §10.3 |
| D4 | **App lock is PIN only** (4–6 digits, no password option), hashed in SQLite | 2026-11-04 | §9.1 |
| D5 | **Forgotten PIN → recovery code** shown once at setup; a new code is issued after each reset | 2026-11-04 | §9.4 |
| D6 | **Auto-lock** on every reopen and after 10 minutes idle; 5 wrong tries → 30-second wait | 2026-11-04 | §9.3, §9.4 |
| D7 | **PIN for critical actions**: Menu add/edit/activate/deactivate asks **every time**; Bookings record payment, discount remaining balance and cancel order are **remembered 3 minutes** | 2026-11-04 | §9.5 |
| D8 | **Cancelling always cancels the whole order**; `total_amount` never changes | 2026-11-04 | §7.3 |
| D9 | **Money stored as integer centavos** (`long`): exactly 2 decimal places, formatted only for display | 2026-11-04 | §6.1 |
| D10 | **Products: deactivate only, never delete** | 2026-11-04 | §7.7 |
| D11 | **Booking list Detailed / Compact view**: Compact = booking ID + pickup time; tap to expand into the full card | 2026-11-04 | §7.5 |
| D12 | **Settings**: Update PIN, Data (export/import), Theme, Columns to show | 2026-11-04 | §10.3 |
| D13 | **Theme: Light / Dark / Device** ("match device", follows the phone live); default Light | 2026-11-04 | §10.1 |
| D14 | **Backups are manual export/import only**; Android Auto Backup **off**; 7-day "no recent backup" reminder in Settings | 2026-11-04 | §10.5, §13 Phase 4 |
| D15 | **Export and import ask for the PIN every time** | 2026-11-04 | §9.5, §10.5 |
| D16 | **Backups never contain the PIN** or recovery code; import keeps the device's own PIN | 2026-11-04 | §10.5 |
| D17 | **Import replaces all data**; it never appends or merges | 2026-11-04 | §10.5 |
| D18 | **No database encryption** | 2026-11-04 | — |
| D19 | **Phone-first, tablet-friendly** layout | 2026-11-04 | §10.2 |
| D20 | **Styling**: plain CSS ported from the prototypes (no CSS framework) | 2026-11-04 | §10.1 |
| D21 | **Indexes and performance: deferred.** Built later, in Phase 4 hardening. Starting point: `orders(pickup_at)`, `items(order_key)`, `items(status)`, `payments(order_key)`, `products(active)`, `<Virtualize>` for long lists, totals in SQL, and a ~10-year fake-data test on the cheapest phone *(was Q1)* | 2026-11-04 | §13 Phase 4 |
| D22 | **No cancelling once any item is cooked or completed.** An order can be cancelled only while every active item is Pending or Cooking, so no-shows after cooking aren't cancellable *(was Q2)* | 2026-11-04 | §7.3 |
| D23 | **Refunds are not tracked.** A fully cancelled order, including money already paid on it, drops out of Revenue, Collected and Balance due. Payment rows stay for history *(was Q3)* | 2026-11-04 | §7.3, §7.6 |
| D24 | ~~**Restore on first run + import loss warning.** A fresh install offers "Restore your data?" right after PIN setup. Import lists bookings/payments on the phone that aren't in the backup and needs a tick before Replace~~ *(was Q4)* — **replaced by D25** | 2026-11-04 | — |
| D25 | **Import warns when the phone already has bookings; no restore prompt on first run.** If the phone has any bookings, the import screen shows "This phone already has N bookings. Importing replaces them…" (warning only, no checkbox). First-run setup goes straight to Bookings. *(replaces D24)* | 2026-11-04 | §10.5 |
| D26 | ~~**Automatic CSV backup, one file per date (`YYYY-MM-DD-bookings.csv`).** Today's file is rewritten hourly, only while the app is open; any other date's file is rewritten right after one of its bookings changes. It's a readable copy, not a restorable backup, so D14's manual export/import stays the real backup~~ — **replaced by D29** | 2026-11-04 | — |
| D27 | **No CSV save when the app goes to the background.** Today's file is saved hourly only while the app is active; the foreground timer stops on `Window.Stopped`, so nothing runs in the background *(was Q6)* | 2026-11-04 | §10.6 |
| D28 | ~~**No CSV regeneration after an import.** Files catch up as the imported bookings change (each date on its next change, today at the next hourly save) *(was Q7)*~~ — **replaced by D29** | 2026-11-04 | — |
| D29 | **One CSV file, bookings within 48 hours.** `roastline-bookings-48h.csv` holds every booking with pickup from 24 h before to 24 h after the save, overwritten **hourly only, while the app is open** (no save after individual changes, none after import). No per-date files *(replaces D26 and D28)* | 2026-11-04 | §10.6 |
| D30 | **CSV backup is Android only for now**, saved to `Documents/Roastline/` through MediaStore *(was Q5)* | 2026-11-04 | §10.6 |
| D31 | **Kitchen load chart uses a fixed day axis.** It always spans the whole selected date, 12 AM to 11:59 PM (business time), with an hourly tick (12 AM … 11 PM) every 40 px, so it is wider than the phone and **scrolls sideways**; the y-axis labels stay pinned. Bar parts outside the day are cut off, but their tooltips keep the real times. Today shows a dashed "now" line and opens centred on it; other dates open an hour before the first busy bar. Which items count is unchanged (Pending and Cooking only, late pending from now). *Replaces the old "earliest start … latest end" range with 3 or 5 ticks; the bookings prototype and spec §11 are updated to match* | 2026-11-07 | §7.6, bookings spec §11 |
| D32 | **Bookings opens on today, and "all dates" means today ± 6 months** (owner's decision). `BookingScopeState` starts with today selected; tapping it again (or "All") shows all dates. "All dates" (the list, the status chips and the Dashboard) covers pickups from the start of today − 6 months to the end of today + 6 months, the same window the date strip and calendar allow. Older bookings stay in the database, in exports and in audit history, but the Bookings page doesn't show them. With ten years of generated data, the all-dates list read went from ≈540 ms to ≈90 ms on a desktop PC and no longer grows with history. *Was Q36; changes the Phase 3 default of "all dates = every booking", shown first* | 2026-11-07 | §7.4, §7.5, §7.6, bookings spec §2 |
| D33 | **Export has a second button, "Save to a folder…"** (owner's request; Android). It opens the system "save as" screen so the owner chooses where on the phone the backup goes, and because the app writes the file itself it knows the save happened, so "Last export" is exact on this path. The share-sheet button stays as it was (Q29 still applies to it). Windows shows only the share button for now | 2026-11-07 | §10.5, settings spec §4.3 |

---

## 16. `CLAUDE.md` for the repo

The repo's `CLAUDE.md` is kept as its own file next to this plan
(`standalone/CLAUDE.md`). Copy it to the repo root when scaffolding
(§4.1), and keep it in step with this plan. Whenever a decision in §15.2
changes a rule it summarizes (PIN table, backups, CSV, Preferences keys),
update both files.

It covers:
- what the app is and what's out of scope
- which docs to read first
- build, run, test and migration commands
- the project layout
- the non-negotiable architecture rules (§5)
- a quick reference of the business, PIN and backup rules
- how to work in the repo: phases, decision log, tests and UI checks
