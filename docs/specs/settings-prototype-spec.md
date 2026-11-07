# Roastline — Settings Screen Spec (`settings-prototype.html`)

Status: Detailed spec of the Settings screen: Update PIN, data export and
import, the automatic CSV backup card, theme and the Bookings columns to
show.

| | |
|---|---|
| Route | `/settings` |
| Layout | `MainLayout`, nav item **Settings** active, **no FAB** |
| Services | `ThemeService`, `PreferencesStore`, `PinPromptService`, export/import service (§4.4), `ICsvBackupService` |
| Rules | Plan §9.5, §10.1, §10.4–§10.6; decisions D12–D17, D25, D27, D29, D30 |
| Shared UI | `shared-ui-spec.md` |
| Build phase | Update PIN in Phase 1; everything else in Phase 4 |

---

## 1. Page structure

**Top bar:** title **Settings**, subtitle *PIN, data, appearance, and
bookings list*.

**Content** (18 px gap between sections), in order:

1. **Security** (§3)
2. **Data**, including the CSV card (§4, §5)
3. **Appearance** (§6)
4. **Bookings list · Columns to show** (§7)
5. Version line (§8)

**Section styling**

- Title: 11 px, uppercase, 0.06em tracking, muted, small top margin.
- Then one or more **settings cards**: `--surface`, border, radius 14,
  padding 0 14.

**Settings row**

- Min 56 px, padding 10 px 0, 12 px gap, divider between rows.
- Icon tile: 34 × 34, radius 9, `--surface-2`, accent 18 px icon.
- Text: title 15 px / 600 over subtitle 12.5 px muted.
- Trailing chevron (muted).
- Tappable rows dim to 70% when pressed.

**Overlays:** Export and Import bottom sheets (z 50/51) and the PIN
prompt.

Page bottom padding = nav + safe-bottom + 24 px.

---

## 2. State and preferences

| Item | Where | Default |
|---|---|---|
| Theme | `Preferences["theme"]` | `light` |
| Columns to show | `Preferences["bookingColumns"]` (JSON, 7 booleans) | all `true` |
| Last export time | `Preferences["lastExportAt"]` (ISO UTC) | none |
| CSV last saved | `Preferences["lastCsvAt"]` (ISO UTC) | none |
| CSV file URI | `Preferences["csvUri"]` | none |
| Open data sheet | Page state | none (`export` / `import`) |
| Chosen import file + its summary | Page state | none |
| Import error | Page state | '' |

Changes to theme and columns are saved immediately on tap; there is no
Save button.

---

## 3. Security

**Card with one row:**

- Key icon · **Update PIN** · *Change the 4–6 digit PIN that opens
  Roastline* · chevron.
- Opens `/lock?mode=change` (`signin-prototype-spec.md` §5.4).
  Finishing or cancelling returns here.

**Caption under the card** (12.5 px muted):

*Roastline locks itself whenever the app is reopened, and after 10
minutes without use. Your PIN is also asked before adding or editing menu
items (every time), before recording a payment, waiving a remaining
balance or cancelling a booking (remembered for 3 minutes), and every
time you export or import data.*

Keep this caption in step with plan §9.4–§9.5 if the rules change.

---

## 4. Data: Export and Import

### 4.1 Backup reminder

Shown above the rows when the last export is **more than 7 days ago, or
there has never been one**.

- Orange box (10% fill, 40% border), radius 12, padding 10 × 12, 13 px,
  warn icon 18 px, `role="status"`.
- **Text, first sentence:**
  - *Your last backup was {N} days ago.* (N = whole days), or
  - *No backup yet.*
- **Then:** *All data lives only on this phone. If it's lost or the app
  is removed, an exported backup is the only way to get it back.*

### 4.2 Rows

| Row | Icon | Title | Subtitle |
|---|---|---|---|
| Export | download | **Export data** | *Last export: Nov 5, 2026, 10:32 PM*, or *Save a backup file of all bookings and the menu* when never |
| Import | upload | **Import data** | *Replace this device's data with a backup file* |

Each opens its bottom sheet.

### 4.3 Export sheet ("Export data")

**Contents**

1. Caption *Creates one backup file with everything stored on this
   device:*
2. List:
   - Bookings, items, payments and discounts
   - Menu (active and inactive products)
3. Caption *Not included: your **PIN and recovery code** (they never
   leave this device), and theme and column settings.* (see §10 #1)
4. Caption *Save the file somewhere off this phone — Google Drive, email,
   or a computer.*
5. Primary full-width **Export backup** (download icon).
6. *Android only (D33):* caption *Or choose the folder on this phone
   yourself.* and a full-width secondary **Save to a folder…** (file icon).
   Windows doesn't show it.

**On tap**

1. PIN prompt: *Export all bookings, payments and the menu to a backup
   file.* → **Export** (every time).
2. The real app follows plan §10.5:
   1. `VACUUM INTO` a cache file `roastline-backup-YYYYMMDD-HHmm.roastline`.
   2. Strip `app_lock`, set `app_meta.exported_at`, then `VACUUM` the
      copy.
   3. Open the share sheet.
3. Save `lastExportAt`, audit `data.exported`.
4. The sheet closes; toast *Backup saved: roastline-backup-20261105-2232.roastline*.
5. The reminder disappears and the Export subtitle updates.

**Save to a folder… (D33):** same PIN prompt and same file steps 2.1–2.2,
then the system "save as" screen instead of the share sheet. The file is
copied to the place the owner picks, and only then are `lastExportAt` saved
and the toast shown (with the name the owner saved it under). Backing out of
the screen saves nothing, leaves the sheet open and shows no error; the PIN
must be entered again for the next try.

**Failure:** keep the sheet open and show an error toast. Don't update
`lastExportAt`.

### 4.4 Import sheet ("Import data")

The prototype handles this in the page. The app should put the work in
one service (e.g. `DataTransferService` in `RoastIT.App`, since it uses
`FileSystem`, `FilePicker` and `Share`) with:

- `ExportAsync(confirmation)`
- `InspectAsync(pickedFile)` → summary or error
- `ImportAsync(inspected, confirmation)`

**State A — no file chosen**

1. Red note (8% fill, 40% border): *Importing **replaces all bookings,
   payments and menu items on this device**. A safety copy of the current
   data is saved first.*
2. Full-width **Choose backup file** (upload icon) → `FilePicker`.
3. Caption *Pick a Roastline backup (.roastline) exported from this or
   another device.*, or the error line in its place (`role="alert"`).

**Inspection** (read-only, before any change; plan §10.5 step 2)

| Check | Error shown |
|---|---|
| Opens as SQLite and `integrity_check = ok`, and `app_meta.app_id = 'roastline'` | *This file isn't a Roastline backup. Choose a file that ends in .roastline.* |
| `schema_version` ≤ the app's | *This backup was made by a newer version of Roastline ({app version}). Update the app first, then import it.* |
| File can't be read | *Couldn't read that file. Try choosing it again.* |

On an error the sheet stays in State A with the message.

**State B — valid file chosen**

1. **File card** (border, radius 12). Rows (label muted, value bold,
   right-aligned, wraps):

   | Row | Example |
   |---|---|
   | File | roastline-backup-20261101-0915.roastline |
   | Backup made | Nov 1, 2026, 9:15 AM |
   | App version | 1.0.0 |
   | Contains | 14 orders · 20 bookings · 11 payments · 5 products |

2. **Existing-data warning** (only when this phone has ≥ 1 booking,
   `SELECT COUNT(*) FROM items`): orange box, `role="alert"`:
   - *This phone already has 20 bookings. Importing replaces them with
     the bookings in this backup. Export a backup first if you want to
     keep them.*
   - With 1 booking: *1 booking … replaces it … to keep it.*
   - Warning only; no checkbox (D25).
3. Red note: *Everything currently on this device will be replaced.
   **Your PIN and recovery code stay the same** — backups don't include
   them.*
4. **Replace data on this device** (solid red, full width).
5. **Choose a different file** (returns to inspection with a new pick).

**Replace**

1. PIN prompt: *Replace all data on this device with {file name}.* →
   **Replace** (every time).
2. The swap, per plan §10.5 step 6:
   1. Keep `app_lock`.
   2. Close contexts and clear pools.
   3. Save the safety copy.
   4. Copy the file in and migrate.
   5. Restore `app_lock`.
3. Audit `data.imported`.
4. Toast *Data imported. Your PIN stays the same. Opening Bookings…*,
   then navigate to **Bookings** (~1.8 s in the prototype). The app stays
   unlocked.
5. **Any failure after the swap starts:** restore the safety copy, stay
   on Settings, show an error. The device must never be half-imported.

Closing the sheet (X, backdrop, Back) clears the chosen file and error.

---

## 5. Automatic CSV backup card

Its own settings card under the Data rows. Behavior: plan §10.6 (Android
only, D30).

**Header block** (padding 12 px 0, 10 px gap)

- **Automatic CSV backup** (15 px / 600).
- Description: *One file, `roastline-bookings-48h.csv`, in
  **Documents/Roastline**. It holds bookings with pickup from 24 hours
  before to 24 hours after each save, and is updated every hour while the
  app is open. Anyone with this unlocked phone can open it.*

**File row states**

| State | Row |
|---|---|
| Saved | File icon · file name (mono 13.5 px) · *Saved Nov 5, 2026, 10:00 PM · 6 bookings* · line 2 *Pickups Nov 4, 10:00 PM – Nov 6, 10:00 PM* · trailing icon button (prototype: Download; app: **Share/Open**) |
| Never saved | *No CSV file yet. It's saved after you open Bookings.* (app: *…after the app has been open for a moment.* — see §10 #3) |
| Last save failed (app only) | *Last CSV save failed — tap to retry* (tapping runs `SaveAsync()`) |

**Help text in the app (plan §10.6 known limits):** after a reinstall
the file may be created as `roastline-bookings-48h (1).csv`; the old one
can be deleted in the Files app.

**Values:** name, saved time, row count and window come from the last
save (`lastCsvAt` + what `SaveAsync` recorded).

---

## 6. Appearance

**Card block:**

- **Theme** (15 px / 600).
- Segmented, `role="radiogroup"`, 16 px icon + label per segment:
  **Light** (sun) · **Dark** (moon) · **Device** (phone).
- Caption:
  - Device selected: *Matching your phone's setting — switches between
    light and dark when the phone does.*
  - Otherwise: *Choose Device to match your phone's light or dark
    setting.*

**Tap:** save `theme` and apply immediately through `ThemeService`
(shared spec §2), across every screen and the status bar.

---

## 7. Bookings list · Columns to show

**Card rows:**

1. **Booking ID** — trailing *Always shown* (muted, no switch).
2. One row per field, each with a switch (*Show {field}*):

   | Field | Key |
   |---|---|
   | Customer | `customer` |
   | Product | `product` |
   | Cooking time | `cookingTime` |
   | Pickup | `pickup` |
   | Status | `status` |
   | Total amount | `total` |
   | Amount paid | `paid` |

**Footer under the card:**

- Left caption:
  - *Every column is shown.*, or
  - *{N} column hidden on the Bookings list.* / *{N} columns hidden on
    the Bookings list.*
- Right link **Show all**, only when something is hidden: turns every
  switch on.

Each change saves `bookingColumns` immediately. The Bookings detailed
cards and expanded compact rows use these flags
(`bookings-prototype-spec.md` §7.1). They don't affect the detail sheet,
Dashboard or CSV.

---

## 8. Version line

- Centered, 12 px muted: *Roastline · version 1.0.0*.
- The app reads `AppInfo.Current.VersionString` (and may add the build
  number, e.g. *(build 1)*).
- The prototype adds *(prototype)*.

---

## 9. UI action → service

| UI action | Service | PIN | Audit | After |
|---|---|---|---|---|
| Update PIN | `/lock?mode=change` → `IAppLock.ChangeAsync` | current PIN on the lock screen | `pin.changed` | back to Settings |
| Export | `DataTransferService.ExportAsync` | every time | `data.exported` | share sheet, `lastExportAt` |
| Choose import file | `DataTransferService.InspectAsync` | — | — | summary or error |
| Replace data | `DataTransferService.ImportAsync` | every time | `data.imported` | Bookings |
| CSV retry / Share | `ICsvBackupService.SaveAsync` / `Share` | — | `csv.save_failed` logged on failure | — |
| Theme | `ThemeService.Set` | — | — | applied live |
| Columns | `PreferencesStore` | — | — | Bookings re-renders |

**Toasts**

- *Backup saved: {file name}*
- *Data imported. Your PIN stays the same. Opening Bookings…*
- Error toasts for failures.

---

## 10. Open points

1. **Export "Not included" copy** omits the list view (plan §10.5 lists
   theme, columns and list view). *Change to: "…and theme, column and
   list-view settings."*
2. **When does an export "count"?** The prototype updates the last-export
   time as soon as the file is made. Android can't reliably tell whether
   the share sheet actually saved it. *Decide: count on file creation
   (simple), or only after the share sheet returns without cancel.*
3. **CSV "never saved" text** says *after you open Bookings* because the
   prototype writes it from the Bookings page. In the app the check runs
   app-wide on start (plan §10.6). *Update the copy.*
4. **CSV card button.** The prototype downloads. *App: Share/Open, plus
   the failure row and the reinstall help text (plan §10.6).*
5. **Import file types.** The prototype accepts `.roastline` and JSON
   (its stand-in format). The app's picker should accept `.roastline`
   only; Android may need `*/*` with the extension checked in code.
6. **Reminder threshold.** "More than 7 days" is measured from the exact
   last-export time. *Confirm, or use calendar days.*
7. **Import counts** come from the backup file. *Decide whether "bookings"
   includes cancelled items (the prototype doesn't say).*

---

## 11. Acceptance checklist

**Security**

- [ ] Update PIN opens the change flow and returns here on finish or
      Cancel.

**Data**

- [ ] The reminder shows when there's no export or the last was more
      than 7 days ago, and hides after exporting.
- [ ] Export asks for the PIN every time, opens the share sheet, updates
      the subtitle and shows the toast.
- [ ] The exported file has no `app_lock` row and no leftover hash bytes.
- [ ] A non-Roastline file, a corrupt file and a newer-schema file each
      show the right message and change nothing.
- [ ] A valid file shows the summary.
- [ ] The existing-bookings warning shows only when the phone has
      bookings (singular wording for 1).
- [ ] Replace asks for the PIN, swaps the data, keeps this device's PIN,
      stays unlocked and opens Bookings.
- [ ] A force-close during the swap leaves the old data intact.

**CSV card**

- [ ] Shows the file name, saved time, count and window after a save.
- [ ] Shows the failure state with retry when a save fails.

**Appearance and columns**

- [ ] Theme switches immediately everywhere; Device follows the phone
      live.
- [ ] Column switches change the Bookings cards immediately; Show all
      appears only when something is hidden.
- [ ] Booking ID can't be hidden.

**General**

- [ ] All preferences survive a restart.
- [ ] Back closes the PIN prompt, then the Export/Import sheet.
