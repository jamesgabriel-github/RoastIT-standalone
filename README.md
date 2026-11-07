# RoastIT

Booking and ordering app for a lechon roasting business (Litson Haus). It
runs **offline on one shared Android phone**, and all data stays on that
phone. There are no user accounts, no sync server and no cloud.

> RoastIT used to be called "Roastline". The old name survives in a few
> technical identifiers tied to existing backups (the `.roastline` backup
> extension, `Documents/Roastline/`, `app_meta.app_id`). See
> [CLAUDE.md](CLAUDE.md) before renaming anything.

## What it does

| Screen | Purpose |
|---|---|
| **Lock** | First-run PIN setup, unlock, forgot PIN (recovery code), change PIN |
| **Bookings** | Date strip, Dashboard (revenue, balance due, kitchen load, product mix), booking list, booking detail, new booking |
| **Menu** | Product catalog: add, edit, activate / deactivate |
| **Settings** | Update PIN, export / import data, automatic CSV backup, theme, which columns the booking list shows |

Navigation is a bottom bar: **Bookings · Menu · Settings**.

Highlights:

- **One booking ID per product line**, formatted `YYMMDD-NNN` (e.g. `261009-001`).
- **Quantities are weights in kilos** (0.01 – 500 kg); product prices are per kilo.
- **Status pipeline:** Pending → Cooking → Cooked → Completed, forward only.
  Cancelling always cancels the whole order, and only before anything is cooked.
- **Payments are additive**, with discounts, a "waive the small remaining
  balance" shortcut, and an audit trail.
- **Kitchen load chart** showing how many items are cooking at each hour of the day.
- **PIN lock:** 4–6 digits, locks on every reopen and after 10 minutes idle;
  critical actions (menu edits, payments, cancels, export/import) ask for the PIN again.
- **Backups:** manual export/import of a `.roastline` file (the PIN never
  travels with it), plus an hourly CSV of bookings picked up within ±24 hours.

## Tech stack

- .NET 10, **.NET MAUI Blazor Hybrid**
- SQLite via **EF Core** (migrations run on the device at startup)
- FluentValidation, Serilog, MAUI `Preferences`
- xUnit tests against real SQLite
- Plain CSS, no CSS framework

Platforms: **Android first**, then Windows. iOS comes later.

## Project layout

```
src/RoastIT.Core     entities, enums, services, rules, validators (no MAUI or EF references)
src/RoastIT.Data     DbContext, configurations, migrations, repositories
src/RoastIT.App      Razor components, platform services, MauiProgram.cs
tests/RoastIT.Tests  xUnit tests for Core + Data
docs/                plan, progress, prototypes, specs, release guide
scripts/             build-release.ps1
```

## Getting started (Windows)

**Prerequisites:** Visual Studio 2026 with the *.NET Multi-platform App UI
development* workload (installs the .NET 10 SDK, Android SDK and emulator),
Git, and either the Android emulator (needs Windows Hypervisor Platform) or a
real phone with USB debugging on. Full setup is in
[docs/implementation-plan.md](docs/implementation-plan.md) §3.

Keep the repo on a short path without spaces (e.g. `D:\RoastIT`); Android
builds fail on long paths.

```powershell
dotnet build src\RoastIT.App -f net10.0-android            # always pass -f; building all targets tries iOS and fails
dotnet build src\RoastIT.App -t:Run -f net10.0-android     # deploy to the connected phone/emulator
dotnet test  tests\RoastIT.Tests
dotnet ef migrations add <Name> -p src\RoastIT.Data -s src\RoastIT.Data
```

You can also open `RoastIT.slnx` in Visual Studio and run `RoastIT.App`.

## Release

Signed APK build, install and upgrade testing are covered in
[docs/release-guide.md](docs/release-guide.md); the short version:

```powershell
.\scripts\build-release.ps1 -KeyStore D:\Keys\roastit.keystore
adb install -r artifacts\release\com.roastit.app-Signed.apk
```

`ApplicationId` (`com.roastit.app`) and the signing keystore **never change**,
and `ApplicationVersion` goes up by 1 on every release. Uninstalling the app
deletes all data, so export a backup first. Release history is in
[CHANGELOG.md](CHANGELOG.md).

## Documentation

| File | What it's for |
|---|---|
| [docs/progress.md](docs/progress.md) | Where the build stands: phase status, what exists, next steps |
| [docs/implementation-plan.md](docs/implementation-plan.md) | Source of truth: rules, data model, PIN rules, build phases, decision log |
| [docs/prototypes/](docs/prototypes) | HTML prototypes of each screen (open at phone size; layout reference) |
| [docs/specs/](docs/specs) | Detailed page behavior for Bookings, Menu, Settings, Sign-in and shared UI |
| [docs/release-guide.md](docs/release-guide.md) | Step-by-step release and install |
| [CLAUDE.md](CLAUDE.md) | Conventions and non-negotiable rules for working in this repo |

## Contributing rules (short version)

- Business rules live in `Core` services, never only in a component. The UI
  may hide things; the service re-checks and refuses.
- Money is `long` centavos; "today" always comes from `IBusinessCalendar`
  (`Asia/Manila`), not the device time zone.
- Never edit a migration that has shipped; prefer additive changes.
- Never log the PIN, its hash or the recovery code.
- Add or extend tests in `RoastIT.Tests` for every rule you touch.
- To change a past decision, add a new row to the decision log (plan §15.2).

The complete list is in [CLAUDE.md](CLAUDE.md).
