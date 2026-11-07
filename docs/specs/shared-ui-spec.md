# Roastline — Shared UI Spec

Status: The parts every screen shares: tokens, page shell, components,
layers, Back behavior, the PIN confirmation prompt and display formats.
The per-screen specs refer to this file instead of repeating it.

| Spec | Prototype | Route |
|---|---|---|
| `shared-ui-spec.md` (this file) | shared blocks in all four | — |
| `signin-prototype-spec.md` | `signin-prototype.html` | `/lock` |
| `bookings-prototype-spec.md` | `bookings-prototype.html` | `/bookings` |
| `menu-prototype-spec.md` | `menu-prototype.html` | `/menu` |
| `settings-prototype-spec.md` | `settings-prototype.html` | `/settings` |

**Which source wins**

- **Layout, copy and interactions:** the prototypes and these specs.
- **Business rules, data, security and persistence:**
  `implementation-plan.md`. The prototypes keep data in memory and the PIN
  in browser storage; never port those parts (§12).

---

## 1. Design tokens

Port these to `wwwroot/css/app.css` as `:root` and
`:root[data-theme="dark"]` custom properties (plan §10.1).

| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` | `#EEEDE6` | `#15120E` | Page background |
| `--surface` | `#FFFFFF` | `#1F1B16` | Cards, sheets, inputs |
| `--surface-2` | `#F6F4EF` | `#29241E` | Pressed states, tracks, meta buttons |
| `--border` | `#E1DDD1` | `#3A3229` | All 1 px borders |
| `--fg` | `#221E19` | `#F2EDE4` | Text |
| `--fg-muted` | `#847C6D` | `#A79E8F` | Secondary text, placeholders, icons |
| `--accent` | `#A8471F` | `#C9612F` | Primary buttons, active chips, FAB, links |
| `--accent-fg` | `#FFFFFF` | `#FFFFFF` | Text on accent |
| `--status-pending` | `#8A8275` | `#A39A8A` | Pending; Inactive pill |
| `--status-cooking` | `#D9731E` | `#E8893A` | Cooking; balance due; warnings |
| `--status-cooked` | `#2E8B52` | `#4CAF72` | Cooked; Active pill; discount amount; paid-up balance |
| `--status-completed` | `#6B7280` | `#959CA8` | Completed |
| `--status-cancelled` | `#C1392B` | `#E25B4D` | Cancelled; errors; danger buttons |
| `--nav-h` | `60px` | | Bottom nav height |
| `--safe-top` / `--safe-bottom` | `env(safe-area-inset-*)` | | Safe areas |

The lock screen uses the same values under the names `--danger` (=
cancelled) and `--ok` (= cooked). Use the shared names in the app.

**Other values**

- `color-scheme: light` / `dark` on `<html>` so native controls (date
  picker, scrollbars) match.
- Backdrop: `rgba(20,16,12,0.42)` light, `rgba(0,0,0,0.6)` dark.
- Sheet shadow `0 -8px 30px rgba(20,16,12,0.18)`.
- FAB shadow `0 6px 18px rgba(168,71,31,0.35), 0 2px 4px rgba(34,30,25,0.12)`.

**Typography**

| Use | Font | Size / weight |
|---|---|---|
| Body | IBM Plex Sans | 14 px / 400, line height 1.45 |
| Top bar title | Plex Sans | 18 px / 700, letter-spacing −0.01em |
| Sheet title | Plex Sans | 16 px / 700, ellipsis |
| Section / group labels | Plex Sans | 11 px / 700, uppercase, 0.06em tracking, muted |
| Field labels | Plex Sans | 12.5 px / 600, muted; "(optional)" at 400 |
| Captions | Plex Sans | 12.5 px, muted |
| Booking IDs, money, keypad digits, chart labels, day numbers | IBM Plex Mono | 500–600, tabular numbers |

Bundle the font files in `wwwroot` (the prototypes load Google Fonts;
the app must work offline).

**Other global rules**

- Tap highlight off.
- `:focus-visible` = 2 px accent outline, 2 px offset.
- Placeholders in `--fg-muted` at full opacity.

---

## 2. Theme

| Preference | Effect |
|---|---|
| `light` (default) | No `data-theme`; `UserAppTheme = Light` |
| `dark` | `data-theme="dark"`; `UserAppTheme = Dark` |
| `system` ("Device" in Settings) | `UserAppTheme = Unspecified`; follow `RequestedTheme` and switch live on `RequestedThemeChanged` |

- Apply the theme before the first page paints, so there is no flash of
  the wrong theme.
- If the preference can't be read, use Light.

---

## 3. Page shell

### 3.1 `MainLayout` (Bookings, Menu, Settings)

| Part | Spec |
|---|---|
| Top bar | Sticky at top. Background `--bg` at 92% with `saturate(1.4) blur(10px)`, 1 px bottom border. Inner row: max width 760 px, centered, padding 10 × 16, min height 56, gap 10. Top padding = safe-top |
| Brand mark | 30 × 30, radius 8, accent fill, white flame icon (17 px, stroke 1.6) |
| Titles | Title (18 px bold) over subtitle (12 px muted). Each page sets its own |
| Content | Max width 760 px, centered, padding 14 px top / 16 px sides, column with 12 px gap (Settings uses 18 px) |
| Bottom nav | Fixed, `--surface`, top border, bottom padding = safe-bottom. Three equal items (min height 60): 22 px icon over 11.5 px semibold label, 3 px gap. Inactive muted, active accent with `aria-current="page"` |
| Nav items | **Bookings** (list icon) · **Menu** (flame icon) · **Settings** (gear icon) |
| FAB | Fixed. Right = `max(16px, (100vw − 760px)/2 + 16px)`; bottom = nav + safe-bottom + 14 px. Height 52, padding 0 20 0 16, radius 16, accent, 15 px bold, 18 px plus icon + label. **Hidden while a full-screen sheet is open** |
| Bottom padding of page | Nav + safe-bottom + 84 px (room for FAB); Settings (no FAB) nav + safe-bottom + 24 px |

### 3.2 `LockLayout`

Centered column, max width 420 px, full dynamic height, no nav and no
FAB. Details in `signin-prototype-spec.md`.

---

## 4. Components

Names match the plan's component list (§4).

| Component | Spec |
|---|---|
| **Card** | `--surface`, 1 px border, radius 14, padding 14 |
| **Button** | Height 44, padding 0 16, radius 10, 14 px / 600, border, `--surface`; pressed `--surface-2`; no text wrap |
| Button · small | Height 38, padding 0 12, 13 px, radius 9 |
| Button · primary | Accent fill and border, white text; pressed = accent mixed 15% black |
| Button · danger | Cancelled-color text, border = 45% cancelled |
| Button · solid danger | Cancelled fill, white text (Import "Replace") |
| Button · dashed | Dashed border, transparent, muted ("Add another item") |
| Button · disabled | 45% opacity, not-allowed cursor |
| **Icon button** | 44 × 44, radius 10, transparent; pressed `--surface-2` |
| **Text / link button** | No border, accent 600 weight; link 13 px min height 36; text button 12.5 px min height 32 |
| **Input** | Height 46, radius 10, border, `--surface`, padding 0 12, **16 px text**. Textarea: min height 96, padding 12, vertical resize |
| **MoneyInput** | Input with a muted "₱" prefix inside at 12 px; input left padding 28 |
| **Search** | Input with a 16 px magnifier at left 13 px; left padding 40; `type="search"`, `enterkeyhint="search"`, autocomplete off; hidden label for screen readers |
| **Field** | Column, 6 px gap: label then control. Field row = 2-column grid, 12 px gap |
| **FilterChips** | Row scrolls sideways edge to edge (−16 px margins), 8 px gap, no scrollbar. Chip: 36 px pill, border, `--surface`, muted 13 px / 600; active = accent fill, white text; `aria-pressed` |
| **Meta button** (Sort) | 36 px, radius 9, `--surface-2`, 12.5 px / 600, 14 px icon + label (ellipsis) |
| **StatusPill** | Inline pill, padding 4 × 10, 11.5 px / 600. Background = 16% of the color on `--surface`, text = color, 6 px dot in the color |
| **Segmented** | Grid of equal columns, `--surface-2` track, border, radius 11, padding 3, gap 3. Segment 38 px, radius 8, 13.5 px / 600 muted; active = `--surface`, `--fg`, small shadow |
| **Switch** | 50 × 30 pill, off `--border`, on accent; 24 px white knob moves 3 px → 23 px (0.15 s). `role="switch"` + `aria-checked` |
| **Toggle card** | Bordered row (radius 12, padding 12 × 14): title + caption left, switch right |
| **EmptyState** | Dashed border, radius 12, padding 36 × 16 (inside cards 20 × 12), centered muted 13.5 px |
| **Toast** | See §8 |
| **ConfirmDialog** | Centered card, left/right 16 px, max width 360, radius 18, padding 20, gap 10, big shadow. Title 17 px, muted body, actions row (buttons share width). `role="alertdialog"` |
| **Sheet** (full) / **BottomSheet** | See §5 |
| **SortSheet** | BottomSheet with field rows, a direction Segmented and **Done** (details per page) |
| **Keypad** / **PinPrompt** | See §7 |
| Menu row | Full-width row, min 52 px, 15 px / 600; selected = accent text + trailing 18 px check |

---

## 5. Sheets, dialogs and layers

### 5.1 Sheet types

| Type | Used for | Phone | ≥ 768 px |
|---|---|---|---|
| **Full sheet** | Booking detail, New booking, Add/Edit product | Fills the screen (`inset: 0`), slides up 28 px + fades in (0.2 s) | 440 px panel on the right with a left border; slides in from the right |
| **Bottom sheet** | Sort, Calendar, Export, Import, PIN prompt | Docked bottom, max height 88vh, radius 18 18 0 0, grabber (38 × 4) at top | 420 px card centered, 24 px from the bottom, radius 18, no grabber, fades in |

**Structure**

- **Header:** min 56 px, bottom border, title (flex 1). Full sheets put a
  back/close icon button first and add safe-top padding on phones.
  Bottom sheets put a close (X) button last.
- **Body:** scrolls on its own (`overscroll-behavior: contain`), padding
  16, column with 16 px gap. Bottom sheets add safe-bottom padding.
- **Footer** (full sheets only): top border, padding 12 × 16 +
  safe-bottom, 10 px gap, buttons share the width (a button can opt out
  with `flex: 0 0 auto`).

### 5.2 Stacking order

| z-index | Layer |
|---|---|
| 20 | Top bar |
| 25 | FAB |
| 30 | Bottom nav |
| 50 / 51 | Backdrop / full sheet (and Menu sort sheet, Settings data sheets) |
| 60 / 61 | Backdrop / bottom sheet opened over a full sheet (Bookings sort and calendar) |
| 70 / 71 | Dialog backdrop / ConfirmDialog |
| 80 | Toast |
| 90 / 91 | PIN prompt backdrop / sheet |

**Rules**

- Tapping a backdrop closes its layer: PIN prompt = cancel, dialog =
  cancel, sheet = close.
- While any sheet, dialog or PIN prompt is open, the page under it
  doesn't scroll.
- Full sheets and bottom sheets: `role="dialog"`, `aria-modal="true"`,
  labelled by their title.

### 5.3 Back navigation (`BackNavigationService`)

Android Back (Escape on Windows) closes only the top layer, in this
order:

1. PIN prompt (same as Cancel)
2. ConfirmDialog
3. Bottom sheet
4. Full sheet

- With no layer open, Back follows normal page navigation.
- Back never exits the app from inside a layer.
- While the PIN prompt is open, the physical keyboard drives the keypad
  (§7.4) instead.

---

## 6. Rendering and input rules

- **Typing never re-renders the field being typed in.** Bind `@oninput`
  to state and re-render only the dependent parts (search results and
  count, line subtotal, totals, submit label), so the keyboard stays open
  and the caret doesn't jump.
- **Scroll positions** of horizontal scrollers (date strip, chips,
  product-card rows) and sheet bodies are kept when the page updates.
- **Focus:** opening an inline editor focuses its input and selects its
  text. Opening the PIN prompt blurs the focused field to close the
  on-screen keyboard.
- **Keyboard and sheets:** the focused input in a sheet scrolls into view
  when the keyboard opens (plan §10.2).
- **Touch targets** are at least 44 px, except the deliberate compact
  controls: chips and meta buttons (36), text buttons (32), the view
  toggle (34 × 30) and the collapse button (32).
- Buttons use `touch-action: manipulation` (no double-tap zoom delay).

---

## 7. PIN confirmation prompt (`PinPrompt` + `PinPromptService`)

Rules: plan §9.5. This section is the UI.

### 7.1 Layout (bottom sheet, z 90/91)

Body padding 8 × 20 (+ safe-bottom), column with 14 px gap:

1. Lock icon tile: 40 × 40, radius 12, accent at 12% on `--surface`,
   20 px accent icon.
2. Title **Enter your PIN to continue** (18 px bold).
3. Detail line (13.5 px muted, max width 300) — the action summary (§7.3).
4. **Bookings scope only:** second line, 12 px: *Payments, write-offs and
   cancellations won't ask again for 3 minutes.*
5. Dots row (height 22, 14 px gap): one 13 px `--fg` dot per digit, or
   the muted hint *Enter your PIN* when empty. `aria-live="polite"`.
6. Message line (min height 18, 13 px / 600, cancelled color,
   `role="alert"`).
7. Keypad 3 × 4, max width 300, 10 px gap; keys 56 px, radius 14, mono
   22 px:
   - Row 1–3: **1–9**.
   - Row 4: **Delete** (transparent, backspace icon 22 px), **0**,
     confirm key (accent, 15 px bold sans, label from the action).
8. **Cancel** button (full width up to 300).

### 7.2 Behavior

| Situation | Result |
|---|---|
| Fewer than 4 digits | Confirm key disabled |
| 6 digits entered | Further digits ignored |
| Correct PIN | Prompt closes, the action runs. Failure counter resets |
| Wrong PIN | Dots shake (0.35 s), entry clears, message *Incorrect PIN. 3 tries left.* (1 → *1 try left.*) |
| 5th wrong PIN | All keys disabled for 30 s; message *Too many tries. Try again in 30s.* counting down every second; then keys re-enable |
| Cancel / backdrop / Back | Prompt closes, nothing saved, the form or editor behind stays as it was |

**Real app**

- Wrong tries count toward the same persisted counter as the lock screen
  (`app_lock.failed_attempts`, plan §9.3).
- The prompt shows the lockout countdown if a lockout is already running
  when it opens.

### 7.3 Prompt texts

| Scope | Action | Detail line | Confirm key |
|---|---|---|---|
| Menu | Add product | Add {name} to the menu. | Add |
| Menu | Save product edits | Save changes to {name}. | Save |
| Menu | Deactivate | Hide {name} from new bookings. | Deactivate |
| Menu | Activate | Make {name} available for booking again. | Activate |
| Bookings | Record payment | Record a {₱500} payment from {customer} ({261008-002}). | Record |
| Bookings | Discount remaining balance | Waive the remaining {₱150} for {customer} ({261008-003}). | Waive |
| Bookings | Cancel, one active item | Cancel booking {261009-003} for {customer}. | Confirm |
| Bookings | Cancel, several items | Cancel the whole order for {customer}: {2} bookings ({261009-001, 261009-002}). | Confirm |
| Settings (every time) | Export | Export all bookings, payments and the menu to a backup file. | Export |
| Settings (every time) | Import | Replace all data on this device with {file name}. | Replace |

- For cancel, when the order has money paid, append *{₱2,000} was already
  paid — handle any refund outside the app.*
- `{customer}` is the customer label: *Walk-in customer* when blank.
- The booking ID in payment and waive prompts is the item whose detail
  sheet is open.

### 7.4 Physical keyboard

While the prompt is open:

| Key | Action |
|---|---|
| 0–9 | Enter digit |
| Backspace | Delete |
| Enter | Confirm |
| Escape | Cancel |

### 7.5 Remembered window (Bookings)

- After a correct PIN on a Bookings action, `PinPromptService` skips the
  prompt for 3 minutes for any Bookings action.
- The window is fixed from the moment of entry and not extended by later
  actions.
- Menu and Settings never use it.
- Locking the app clears it.

---

## 8. Toasts (`ToastService`)

| Property | Spec |
|---|---|
| Look | `--fg` background, `--bg` text, padding 10 × 16, radius 12, 13.5 px / 500, shadow, max width = screen − 32 px, centered. `role="status"` |
| Position | 80 px above the bottom nav. When a full sheet is open: safe-bottom + 84 px (above the sheet footer) |
| Hidden while | A bottom sheet, ConfirmDialog or PIN prompt is open (it shows again if still within its time) |
| Duration | Bookings ~2.6 s, Menu ~2.4 s, Settings ~3 s, Lock ~2.4 s. A new toast replaces the current one and restarts the timer |

---

## 9. Icons

Inline SVG, 20 × 20 viewBox, no fill, `currentColor` stroke 1.8, round
caps and joins, `aria-hidden="true"`.

| Name | Used for |
|---|---|
| flame | Brand mark, Menu nav |
| list | Bookings nav |
| gear | Settings nav |
| plus | FAB, Add another item |
| close (X) | Close sheets |
| back (‹) | Detail sheet back |
| search | Search fields |
| check | Advance buttons, selected sort row |
| chevron › / chevLeft ‹ | Strip arrows, calendar months, settings rows |
| chevDown / chevUp | Compact row expand / collapse |
| viewCards / viewCompact | List view toggle |
| calendar | Calendar button |
| clock | Pickup lines |
| person | Customer line |
| sortAsc / sortDesc | Sort button |
| lock | PIN prompt |
| key | Update PIN |
| sun / moon / device | Theme segments |
| download / upload | Export / Import, CSV download |
| warn | Backup warnings |
| file | CSV file row |
| copy | Copy recovery code |
| backspace | Keypad delete |

---

## 10. Display formats

All dates and times are business time (Asia/Manila) via
`IBusinessCalendar`.

| Value | Format | Example |
|---|---|---|
| Money | ₱ + `en-PH` thousands separators. Show centavos only when non-zero (prototype shows whole pesos) | ₱4,400 · ₱1,250.50 |
| Duration | < 60: *N min*; else *H hr* + *M min* when M > 0 | 45 min · 2 hr · 2 hr 30 min |
| Time | h:mm AM/PM | 5:00 PM |
| Short date | Mon D | Oct 9 |
| Date + time | Mon D · h:mm AM/PM | Oct 9 · 5:00 PM |
| Long date | Ddd, Mon D, YYYY | Fri, Oct 9, 2026 |
| Range date | Mon D, YYYY | Apr 9, 2026 |
| Settings timestamps | Mon D, YYYY, h:mm AM/PM | Nov 5, 2026, 10:32 PM |
| Customer label | Name, or *Walk-in customer* when blank | |
| Count labels | Singular / plural | 1 booking · 12 bookings · 1 product |

---

## 11. Accessibility

| Element | Treatment |
|---|---|
| Sheets | `role="dialog"`, `aria-modal`, labelled by title |
| ConfirmDialog | `role="alertdialog"` |
| Tabs | `role="tablist"` / `role="tab"` + `aria-selected` |
| Segmented choices, view toggle, theme | `role="radiogroup"` / `role="radio"` + `aria-checked` |
| Chips, date tiles | `aria-pressed` |
| Switches | `role="switch"` + `aria-checked` + `aria-label` |
| Errors | `role="alert"` |
| Toasts, chart readout, PIN dots | `role="status"` / `aria-live="polite"` |
| Icon-only buttons | `aria-label` (Back, Close, Previous week, Pick a date, Delete, Download …) |
| Search fields | Visually hidden labels |

---

## 12. Prototype-only shortcuts (all screens)

Don't port these.

| In the prototypes | In the app |
|---|---|
| Data seeded in memory, reset on reload | SQLite via EF Core |
| Money in whole pesos | `long` centavos (plan §6.1) |
| "Today" fixed to Oct 9, 2026 | `IBusinessCalendar.Today()` |
| PIN in browser storage, plain text, default `1234` | Hash in `app_lock` only |
| Prompt lockout and 3-minute window per page, in memory | `AppLockService`, shared counter persisted in `app_lock` |
| Pages linked with `<a href>` | Blazor routes; lock route guarded by `IAppLock.IsLocked` |
| Preferences in browser storage | MAUI `Preferences` (plan §10.4) |
| Google Fonts | Bundled fonts |
| Escape key = Back | `OnBackButtonPressed` → `BackNavigationService` |
| No auto-lock | Lock on resume and after 10 min idle (plan §9.4) |
