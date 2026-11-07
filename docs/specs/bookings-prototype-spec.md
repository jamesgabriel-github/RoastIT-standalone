# Roastline — Bookings Screen Spec (`bookings-prototype.html`)

Status: Detailed spec of the Bookings screen: date strip, Booking list,
Dashboard, booking detail and new booking.

| | |
|---|---|
| Route | `/bookings` (default after unlock) |
| Layout | `MainLayout`, nav item **Bookings** active |
| Services | `BookingService`, `OrderService`, `DashboardService`, `IBusinessCalendar`, `IChangeNotifier`, `PinPromptService` |
| Rules | Plan §7.1–§7.6, §9.5; decisions D7, D8, D11, D22, D23, D36 |
| Shared UI | `shared-ui-spec.md` |
| Older behavior notes | `bookings-page-spec.md` (ignore its desktop layout) |
| Build phase | Phase 3 (columns and list-view preference wired in Phase 4) |

---

## 1. Page structure

**Top bar:** title **Bookings**, subtitle = today's long date (*Fri, Oct
9, 2026*).

**Content, top to bottom:**

1. Date bar (§3)
2. Scope line (§5)
3. Tabs **Dashboard | Booking list** (§5)
4. Active tab: Booking list (§6) or Dashboard (§10)

**Overlays:**

| Overlay | Type |
|---|---|
| Booking detail (§8) | Full sheet |
| New booking (§9) | Full sheet |
| Sort (§6.4), Calendar (§4) | Bottom sheets over everything else |
| Quick-advance confirmation (§7.4) | ConfirmDialog |
| Booking created (§9.1) | Dialog, closed only by the user |
| PIN prompt | Shared |

**FAB:** **+ New booking**, hidden while a full sheet or the Booking
created dialog is open.

---

## 2. State

| State | Holder | Default | Notes |
|---|---|---|---|
| `SelectedDate` | `BookingScopeState` (scoped) | **today** (D32; null = all dates, which means today ± 6 months) | Shared by both tabs. Rejects dates outside ±6 months |
| `ActiveTab` | `BookingScopeState` | `list` | `dashboard` / `list` |
| `StripStart` | `BookingScopeState` | today − 2 days | First day in the 10-day strip |
| `CalendarMonth` | `BookingScopeState` | today's month | Month shown in the calendar sheet |
| Search, status filter, sort field/direction | Page state | '', `all`, `pickup` / `asc` | Reset when the app restarts |
| Expanded compact rows | Page state | none | Cleared when the list view changes |
| Kitchen bar selection | Page state | none | Cleared when the date changes |
| Open sheet / drawer / dialog | Page state + `BackNavigationService` | none | |
| Discount draft / payment draft | Detail sheet state | none | At most one at a time |
| New booking form | Sheet state | see §9 | Reset after a successful create |
| Created bookings | Page state | none | Set by a successful create (IDs, products, customer, pickup); cleared only when the user closes the Booking created dialog (§9.1) |
| List view | `Preferences["bookingListView"]` | `detailed` | `detailed` / `compact` |
| Columns to show | `Preferences["bookingColumns"]` | all on | Set in Settings. Keys `customer`, `product`, `cookingTime`, `pickup`, `status`, `total` (label *Order total*), `paid` (label *Order paid*) |

**Refreshing**

- Subscribe to `IChangeNotifier` (`BookingsChanged`, `OrderChanged`,
  `CatalogChanged`).
- Re-query the list, the counts and the Dashboard after any change, so an
  edit in the detail sheet updates the cards and KPIs behind it.

---

## 3. Date bar

One row, 8 px gap: **All** chip · day strip (fills the width) · calendar
button.

| Part | Spec |
|---|---|
| **All** chip | 48 px wide, radius 12, 1.5 px border, 13 px bold. Accent fill when no date is selected. Tap = clear the date |
| Day strip | Sideways scroll, snap to tiles, no scrollbar, 6 px gap. Contains: ‹ arrow, 10 day tiles, › arrow |
| Arrow | 34 × 56, radius 12, dashed border, muted chevron. ‹ = *Previous week*, › = *Next week*. Shifts the strip by ±7 days. Disabled (35%) at the window edges |
| Day tile | 46 × 56, radius 12, 1.5 px border, `--surface`. Weekday (10 px, uppercase, 75% opacity) over day number (mono 16 px bold) |
| Today tile | Accent border |
| Selected tile | Accent fill, white text |
| Booking dot (D36) | 5 px round accent dot, centered, 4 px above the bottom edge. Shown when the day has any booking (items, any status) with pickup on that day; white on the selected tile; hidden when there are none. No count is shown (it stays in the tile's label) |
| Calendar button | 48 px wide, radius 12, `--surface-2`, 20 px calendar icon. Accent border and icon when a date is selected. Label *Pick a date* |

**Behavior**

- The strip shows `StripStart` … `StripStart + 9`. `StripStart` is
  clamped between `min` and `max − 9`, where min/max = today ∓ 6 months.
- Tiles outside the window are disabled.
- **Tap a tile:** select that date. **Tap the selected tile:** clear the
  date (All).
- **Selecting a date outside the visible strip** (from the calendar or
  Today): `StripStart = date − 2` (clamped) and the strip scrolls back to
  its start. Otherwise the strip keeps its scroll position.
- Each tile's label reads e.g. *Fri, Oct 9, 2026, 3 bookings*.
- `SelectDate` re-checks the window in `BookingScopeState`, whatever
  path calls it.

---

## 4. Calendar sheet ("Pick a date")

Bottom sheet over everything (z 60/61).

1. **Month header:** ‹ button · *October 2026* (15 px bold) · › button.
   Buttons are 44 px with a border, disabled at the first/last month of
   the window. The month is clamped to the window.
2. **Grid:** 7 columns, 4 px gap, weeks start on **Sunday**.
   - Header row Sun–Sat (10.5 px, uppercase, muted).
   - Blank cells before the 1st and after the last day to complete the
     weeks.
3. **Day cell:** 42 px, radius 10, mono 14 px / 600.
   - Today: accent border. Selected: accent fill.
   - Days with bookings: 5 px accent dot at the bottom (white on
     selected).
   - Days outside the window: disabled at 40% opacity.
4. **Buttons row:**
   - **Today** selects today, switches to its month and resets the strip
     to today − 2.
   - **Clear date** shows only when a date is selected.
5. **Caption** (centered): *Scheduling window: Apr 9, 2026 – Apr 9, 2027*.

**Behavior**

- Opening shows the selected date's month, or today's month.
- Tapping a day selects it (or clears it when it's already selected) and
  closes the sheet.
- Today and Clear date also close the sheet.

---

## 5. Scope line and tabs

- **Scope line** (13 px muted): *Showing* + bold value:

  | Selected date | Value |
  |---|---|
  | Today | Today |
  | Yesterday | Yesterday |
  | Tomorrow | Tomorrow |
  | Any other date | Wed, Sep 30, 2026 |
  | None | all dates |

- **Tabs:** Segmented control, `role="tablist"`: **Dashboard** | **Booking
  list**.
  - Default Booking list. Not remembered between app starts.
  - Switching tabs keeps the date, search, filters and sort.

---

## 6. Booking list

### 6.1 Controls

1. **Search:** placeholder *Search ID, product, or customer*.
   - Matches booking ID, product name or customer label,
     case-insensitive substring. Blank names match *walk-in*.
   - Updates the results and count on each keystroke without
     re-rendering the field.
2. **Status chips:** All · Pending · Cooking · Cooked · Completed ·
   Cancelled, each with ` · {count}`.
   - The **selected** chip is accent-filled with white, bold text, a check mark before the label and a soft 2 px ring, so the
     active status is obvious (All is selected by default). `aria-pressed` is true on it.
   - Counts are **global**: they ignore the selected date and the search, and cover what "all dates" covers, today ± 6 months (D32).
   - With the seed data: All · 20, Pending · 8, Cooking · 1, Cooked · 1,
     Completed · 9, Cancelled · 1.
3. **Meta row** (min 36 px, items spaced apart):
   - Count (muted): *12 bookings* / *1 booking* — the number of result
     rows.
   - **View toggle:** 2 × (34 × 30) buttons in a `--surface-2` track
     (radius 9): **Detailed cards** and **Compact list** icons.
     `role="radiogroup"`. Saves `bookingListView` on change.
   - **Sort button:** meta button with the ↑/↓ icon for the direction and
     the field label (e.g. *Pickup time*). Opens the sort sheet.

### 6.2 Filtering

A booking (item) is shown when all of these hold:

- status = the chip (unless All)
- pickup date = `SelectedDate` (unless all dates)
- the search matches

### 6.3 Results

- **Grouped** when no date is selected **and** sort = Pickup time:
  - One group per pickup date, in the sort direction.
  - Within a group, always pickup time ascending (see §14).
  - Group header (13 px bold): *Fri, Oct 9, 2026* + a **Today** pill when
    it applies; right side *3 bookings* (muted, 12.5 px).
- **Otherwise flat**, sorted by the chosen field and direction.
- Cards stack with a 10 px gap (compact rows 6 px).

### 6.4 Sort sheet ("Sort bookings")

| Field | Sorts by | Asc label | Desc label |
|---|---|---|---|
| Pickup time *(default, asc)* | `orders.pickup_at` | Soonest first | Latest first |
| Status | Pipeline order Pending → Cooking → Cooked → Completed → Cancelled | Pending → Completed | Completed → Pending |
| Booking ID | Text | A → Z | Z → A |
| Customer | Customer label (so *Walk-in customer* sorts under W) | A → Z | Z → A |
| Product | Product name | A → Z | Z → A |
| Cooking time | Product cooking minutes | Shortest first | Longest first |

**Sheet contents**

1. Field rows (52 px, selected = accent + check).
2. Direction Segmented, showing the selected field's labels.
3. Caption: *Bookings are grouped by date only when showing all dates
   sorted by pickup time.*
4. **Done** (primary).

Picking a field or direction applies immediately; Done just closes.
Changing the field keeps the direction.

### 6.5 Empty states

| Situation | Text |
|---|---|
| Date selected, no results | No bookings scheduled for Oct 10. |
| No date selected, no results | No bookings match your filters. |

---

## 7. Booking cards

### 7.1 Detailed card

Card radius 14, padding 12 × 14, 6 px gap, pressed `--surface-2`. The
whole card is a button: tap, Enter or Space opens the detail. Label
*Booking 261009-001*, plus *, 1 of 2 in this order* when the order has
more than one booking.

| Line | Content | Controlled by |
|---|---|---|
| Top | Booking ID (mono 13 px, muted) + order tag (below) · status pill on the right | Pill: **Status** column |
| Product | Product (15 px bold) · cooking time (13 px muted, *· 2 hr*) | **Product**, **Cooking time** (line hidden if both off) |
| Customer | Person icon + customer label (ellipsis) | **Customer** |
| Pickup | Clock icon + *Oct 9 · 5:00 PM* | **Pickup** |
| Money | *Order* **₱4,400** (the **order's** net total; with a discount the gross shows struck through after it) · *Paid* **₱2,000** (the order's `amount_paid`) | **Order total**, **Order paid** (line hidden if both off) |
| Actions | Divider, then one small full-width button: check icon + **Start cooking** / **Mark cooked** / **Mark completed** | Shown when the item has a next status |

Booking ID and the order tag always show. The quick-advance button
doesn't open the detail (the tap is handled by the button).

**Money is per order, not per booking.** Every booking in an order shows
the same *Order* and *Paid* figures, so the cards of one order must not
be added together. The order tag is what tells the reader the figures
are shared.

**Order tag**

- Shown after the booking ID only when the order has **more than one
  booking**: *1 of 2*, *2 of 2*, … Single-booking orders show no tag.
- **Position** = rank of the booking ID among all the order's bookings,
  sorted by booking ID. **Count** = all the order's bookings, **any
  status** (cancelled included). The numbering never changes when a
  booking is cancelled, filtered out or hidden by search.
- Look: pill, 8 px left margin, padding 1 × 7, `--surface-2` fill, 1 px
  border, body font 11 px / 600, `--fg-muted`, no wrapping.
- Hidden from screen readers (`aria-hidden`); the card label carries it
  instead.
- Computed from all bookings, not from the filtered list, in the list
  query (`orderKey` → booking IDs).

### 7.2 Compact row

- Button, 52 px, radius 12, padding 0 12 0 14, 12 px gap.
- Content: booking ID (mono 14 px, `--fg`, fills the row) + order tag
  (§7.1) · clock icon + time · chevron-down.
- **Time format:**
  - Time only (*5:00 PM*) when the day is already clear: grouped view or
    a date selected.
  - Otherwise *Oct 9 · 5:00 PM*.
- Label: *Booking 261009-001, pickup 5:00 PM. Show details*; with the
  order tag *Booking 261009-001, 1 of 2 in this order, pickup 5:00 PM.
  Show details*.
- **Tap:** the row turns into the full detailed card (§7.1) in place,
  with:
  - an accent-tinted border
  - a **collapse** button (chevron-up, 32 px) next to the pill
    (`aria-expanded="true"`)
- In the expanded card: tap the card to open the detail; tap collapse to
  go back to the row.
- Several rows can be expanded at once.

### 7.3 Status pipeline

| Status | Color token | Next action label | Next status |
|---|---|---|---|
| Pending | `--status-pending` | Start cooking | Cooking |
| Cooking | `--status-cooking` | Mark cooked | Cooked |
| Cooked | `--status-cooked` | Mark completed | Completed |
| Completed | `--status-completed` | — | — |
| Cancelled | `--status-cancelled` | — | — |

Pending → Cooking stamps `started_cooking_at` (UTC now).

### 7.4 Quick-advance confirmation

From a list card only:

- ConfirmDialog: title *Update status?*, body *Mark booking 261009-001 as
  Cooking?*.
- Buttons **Cancel** / **{Start cooking}** (primary).
- Confirm: `AdvanceStatusAsync(bookingId, expectedStatus)`, then toast
  *261009-001 → Cooking*.
- If the item moved on meanwhile (`StaleStateException`): toast the
  error and refresh.

---

## 8. Booking detail (full sheet)

**Header:** back icon button (*Back*) · booking ID (mono, sheet title) ·
status pill.

**Body** (scrolls; position kept per booking):

### 8.1 Booking card

Section label *BOOKING*. Rows (label left muted, value right bold, 9 px
padding, divider between):

- Product
- Cooking time
- Pickup (*Oct 9 · 5:00 PM*), with a small **Edit** link (D35). It opens an inline editor under the row: the New booking
  pickup picker, the caption "Moves every booking in this order. Booking IDs stay the same.", **Cancel** and **Save pickup**.
  The link is hidden on a cancelled order and once any booking in the order is Cooked or Completed. Save asks for the Bookings
  PIN (prompt "Move the pickup for … to Nov 9 · 5:00 PM." → **Move**; 3-minute window), then closes the editor and shows the
  toast "Pickup moved to …". Saving the unchanged time just closes the editor. A past time is allowed (the picker warns).

### 8.2 Customer card

- **Name:** customer label.
- **Phone:**
  - Accent link `tel:` with spaces removed, showing the number as
    stored.
  - Or muted *Not on file*.

### 8.3 Payment card

Rows in this order; conditions decide what shows.

| # | Row | Shown when |
|---|---|---|
| 1 | Total amount — gross `total_amount` (mono) | Always |
| 2 | **+ Apply discount** (right-aligned link) | No discount, and no editor open |
| 3 | Discount — *−₱15* in cooked color. **Edit** and **Remove** text buttons after the label | Discount > 0 and the discount editor isn't open. Edit/Remove hidden while the payment editor is open |
| 4 | Net total | Same as row 3 |
| 5 | Discount editor | Editing the discount |
| 6 | Amount paid | Always |
| 7 | **+ Record payment** (link) | Balance > 0 and no editor open |
| 8 | Payment editor | Recording a payment |
| 9 | **Balance due** — 16 px; label `--fg` bold; value orange when > 0, green when 0 | Always |
| 10 | Write-off banner | 0 < balance < 10% of **gross** total, and no editor open |

Values come from `OrderMath.NetTotal` / `Balance`; nothing recomputes
them separately.

**Discount editor** (inline, divider above, 8 px gap)

- Label *Discount amount*, ₱ field (decimal keyboard) pre-filled with the
  current discount (blank if none), focused and selected.
- Caption *Can't be more than the order total (₱1,015).*
- Buttons **Cancel** / **Save discount** (primary).
- **Save:** `SetDiscountAsync(orderKey, amount)` clamps to [0, total].
  Blank, invalid or 0 removes the discount. No PIN. Audit
  `discount.set`.
- **Remove:** `SetDiscountAsync(orderKey, 0)`. No PIN.

**Payment editor**

- Label *Payment received*, ₱ field pre-filled with the current balance,
  focused and selected.
- Caption *Added to what's already been paid. Change it for a partial
  payment.*
- Buttons **Cancel** / **Save payment**.
- **Save:**
  - Blank, invalid or 0: the editor closes. No PIN, nothing saved.
  - Otherwise: PIN prompt (*Record a ₱500 payment from Diego Santos
    (261008-002).* → **Record**, Bookings window), then
    `RecordPaymentAsync`. It's additive and writes a `payments` row and
    audit `payment.recorded`.
  - Toast *Recorded ₱500 payment*. The editor closes.
- Cancelling the PIN prompt leaves the editor open with the amount.

Opening one editor closes the other.

**Write-off banner**

- Orange-tinted box (9% fill, 35% border), radius 12, padding 10 × 12.
- Text *Only **₱15** left (1.5% of total)*; the percentage is of the gross
  total, rounded to 0.1.
- Button **Discount remaining balance** (small).
- PIN prompt *Waive the remaining ₱15 for {customer} ({id}).* → **Waive**
  (Bookings window).
- Then `WriteOffBalanceAsync(orderKey)`: re-reads the order and sets
  `discount = min(discount + balance, total)`. Audit
  `order.balance_waived`. Toast *Remaining balance waived*.

### 8.4 Also in this order

- Shown when the order has other items.
- Section label *ALSO IN THIS ORDER*, then one row per sibling: 48 px
  bordered button with the ID (mono 12 px muted) over the product (13.5 px
  / 600), and the status pill on the right.
- Tapping a sibling switches the sheet to that booking (editors closed).

### 8.5 Can't-cancel caption

Shown under the cards when the **viewed** item is Pending or Cooking but
the order isn't cancellable:

*This order can't be cancelled: part of it is already cooked or
completed.*

### 8.6 Footer

Shown when at least one button applies.

**Cancel button** (danger)

- Shown while the order is cancellable: at least one Pending/Cooking item
  and no Cooked/Completed item.
- Label **Cancel order** when more than one non-cancelled item remains,
  else **Cancel booking**.
- Tap:
  1. PIN prompt (Bookings window) with the cancel text from
     `shared-ui-spec.md` §7.3, plus the paid-money line when
     `amount_paid > 0`.
  2. After the PIN, `CancelOrderAsync(orderKey)` re-checks: if any item
     is now Cooked/Completed, it refuses and the toast says *Can't cancel
     — part of this order is already cooked or completed.*
  3. Otherwise every non-cancelled item becomes Cancelled, editors close,
     and the toast says *261009-003 cancelled* (one) or *Order cancelled ·
     2 bookings*.
- Audit `order.cancelled` with the booking IDs.

**Advance button** (primary)

- Check icon + next action label.
- **Immediate**, no confirmation.
- Toast *261009-001 → Cooking*. The sheet stays open with the new status.

---

## 9. New booking (full sheet)

**Header:** close (X, *Close*) · *New booking*.

**Body:** a form (Enter in any field creates the booking). The **Create
booking** button and Enter both call the create action directly; don't
rely on native form submission, which sandboxed previews and some
WebViews block silently. Fields in order:

| Field | Control | Default | Notes |
|---|---|---|---|
| Customer name *(optional)* | Text, `enterkeyhint="next"` | blank | Placeholder *Leave blank for a walk-in*. Trimmed |
| Phone *(optional)* | `tel` input | blank | Placeholder *0917 000 0000*. Trimmed |
| Pickup date & time | Date-time picker, min/max = window | see §14 | Business time |
| *ITEMS* (group label) | | | |
| Item line(s) | Line card (below) | one line: first active product, weight blank | |
| **+ Add another item** | Dashed button | | Adds a line (first product, weight blank) |
| Summary card | see below | | |

**Item line card**

- `--surface-2`, border, radius 14, padding 12 × 14, 12 px gap.
- **Header:** *ITEM 1* (group label) + text button **Remove**, or
  **Reset** when it's the only line (Reset sets it back to the first
  product, weight blank).
- **Product picker:** sideways-scrolling row of product cards
  (`role="radiogroup"`).
  - Card 136 px wide, radius 12, 1.5 px border, padding 10: name (13.5 px
    bold), cooking time (11.5 px muted), price per kilo (mono 13 px,
    followed by a small muted */kg*).
  - Selected: accent border, accent 8% tint.
  - **Active products only.**
- **Footer row:**
  - **Weight stepper (D34):** **−** (44 px) · weight field (72 px, mono
    16 px, decimal keyboard) · a fixed *kg* label · **+**. The quantity is a
    **weight in kilos**, e.g. *3.2*, not a number of bookings: one line is
    one booking whatever it weighs. Up to 2 decimals, 0.01 – 500 kg. The
    buttons move by 0.5 kg from the current value (an empty field starts at
    0.5; − stops at 0.5).
  - Subtotal on the right: caption *Subtotal* over mono 15 px bold
    *₱7,040* (price per kilo × kilos, rounded to the centavo).

**Summary card** (border, radius 14, padding 14, 12 px gap)

1. *Order total* — sum of price per kilo × kilos for all lines.
2. *Discount (optional)* — ₱ field.
3. *Net total* (16 px bold) — only when the discount, clamped to
   [0, total], is > 0.
4. *Amount paid now (optional)* — ₱ field.

**Footer:** **Cancel** (fixed width) + primary **Create booking ·
₱{net total}**.

**Live updates while typing:** subtotals, order total, net total and the
submit label update; the fields themselves don't re-render.

**Create** (`BookingService.CreateAsync`, no PIN)

1. Every line needs a valid weight (0.01 – 500 kg). A blank or invalid
   one stops the save with the inline message *Enter a weight between
   0.01 and 500 kg for every item.* (lines are no longer silently dropped).
2. The service recomputes the total from catalog prices, rejects inactive
   products, clamps the discount to [0, total] and the paid amount to
   ≥ 0, and creates a `payments` row when paid > 0.
3. Booking IDs: one per line (not per kilo), claimed as a block from
   `daily_counters` for today (plan §7.1). The weight is stored on the
   booking.
4. The sheet closes and the form resets.
5. The **Booking created** dialog opens (§9.1) with the new booking
   ID(s). No toast.

### 9.1 Booking created dialog

Shown after every successful create, so the staff can write the ID on the
order slip or read it to the customer. It stays open until the user
closes it.

**Layout** (dialog card, radius 18, padding 20, max 360 px wide, centered;
backdrop z 70, dialog z 71)

1. **Header** (centered):
   - 44 px round icon: check mark in `--status-cooked` on a 14% tint.
   - Title (17 px bold): *Booking created* (one unit) or *3 bookings
     created* (several).
   - Caption (13 px muted): *{customer label} · Pickup {Oct 10 · 10:00
     AM}*. Blank names show *Walk-in customer*.
2. **Booking ID list** — one tile per created booking, in ID order:
   - Tile: `--surface-2`, 1 px border, radius 12.
   - **One unit:** single centered tile, ID in mono 24 px / 600, product
     (12.5 px muted) under it.
   - **Several:** one row per ID: ID in mono 18 px / 600 on the left,
     product (12.5 px muted) on the right. The list scrolls past 40% of
     the screen height.
3. **Done** (primary, full width).

**Closing**

- Only **Done** or the Back button (Escape in the prototype) closes it.
- No auto-dismiss timer, and tapping the backdrop does nothing.
- Done gets focus when the dialog opens, so Enter closes it on a
  keyboard.

**Other behavior**

- `role="alertdialog"`, labelled by the title and described by the ID
  list.
- Toasts are hidden while the dialog is open.
- The page behind it doesn't scroll.
- The list and Dashboard behind it already show the new bookings
  (`BookingsChanged`).

---

## 10. Dashboard

Scope = `SelectedDate` (pickup date) or all dates. All money figures skip
orders whose items are **all** Cancelled.

### 10.1 KPI tiles

| Tile | Value |
|---|---|
| Total bookings | Items in scope (all statuses) |
| Total revenue | Σ `NetTotal` |
| Collected | Σ `amount_paid` |
| Balance due | Σ `Balance` |
| Avg. order value | Revenue ÷ number of counted orders (₱0 when none) |

**Layout**

- **Phone:** 2-column grid, 10 px gap; the first tile spans both columns.
- **≥ 640 px:** 5 equal columns.
- **Tile:** card radius 14, padding 12 × 14; label 12 px muted; value mono
  20 px / 600 (17 px at ≥ 640 px), wraps if needed.

### 10.2 Status breakdown (date selected only)

- Card: title *Status breakdown*, caption *{N} bookings*.
- 12 px stacked bar (rounded, `--surface-2` track); one segment per
  status with count > 0, width ∝ count, in pipeline order including
  Cancelled.
- Legend: 8 px dot + label + mono count for each non-zero status.
- Empty: *No bookings on this date.*

### 10.3 Kitchen load (date selected and ≥ today)

See §11.

### 10.4 Product mix

- Card: title *Product mix*.
- Counts non-cancelled items per product, most first; ties in catalog
  order.
- Row: name · mono count, then an 8 px bar (accent) scaled to the top
  product.
- Empty: *No bookings in this view.*

### 10.5 Balance due

- Card: title *Balance due*.
- Orders with balance > 0, largest first, top 5.
- Row: customer label (14 px / 600) over pickup *Oct 8 · 3:00 PM*
  (caption); balance mono in orange on the right.
- Then *+N more* when there are more than 5.
- Empty: *Everyone's paid up in this view.*

---

## 11. Kitchen load chart

### 11.1 Data (`DashboardService` / `KitchenLoad` rule, plan §7.6)

Items in scope with status Pending or Cooking. All values on a
business-time minute axis.

| Item | Start | End |
|---|---|---|
| Cooking | `started_cooking_at` | start + cooking minutes |
| Pending, on time | pickup − cooking minutes | pickup |
| Pending, late (ideal start < now) | now | now + cooking minutes |

- End is at least start + 1 minute.
- Event map: +1 at each start, −1 at each end. Sort the times and keep a
  running total.
- Each step from time t[i] to t[i+1] (the last step ends at the range
  end) is one bar with height = running total. Zero-width steps are
  dropped.
- Range (D31): the whole selected date, **12 AM to 11:59 PM** business
  time (up to the next midnight), whatever the bars are. Bar parts outside
  it, such as cooking that starts the evening before an early pickup or
  runs past midnight, are cut off; the tooltip and readout keep the bar's
  real start and end (*2 cooking · 10:00 PM – 1:00 AM*).

### 11.2 Card and empty state

- Title *Kitchen load*, caption *Items cooking (count)*.
- No pending/cooking items: *No pending or cooking items scheduled for
  this date.*

### 11.3 Drawing (SVG)

| Part | Spec |
|---|---|
| Size (D31) | Visible width = min(screen, 760) − 62, at least 260; height 184. A fixed 30 px y-axis column on the left; the rest is a sideways-scrolling plot 40 px per hour (960 px for the day) plus 8 px right padding. Padding top 18, bottom 24 |
| Y axis | Labels (mono 10.5 px muted, right-aligned at x = 24) in the fixed column, so they stay visible while scrolling; gridlines (`--border`) across the plot. 0 to max, step 2 when max > 6 else 1 |
| Bars | `--status-cooking` at 78% opacity, radius 2, 1 px gap. Selected bar: accent, 100% |
| Bar labels | Count above the bar (mono 11 px / 600) when the bar is ≥ 18 px wide and count > 0 |
| X axis (D31) | Baseline in `--fg-muted`. A tick (3 px mark + label) every hour, *12 AM* … *11 PM*; *12 AM* left-anchored, the rest centered, 16 px below the baseline |
| Now line (D31) | Today only: a dashed accent line at the current time, not tappable |
| Opening scroll (D31) | Today: centred on now. Other dates: one hour before the first bar with a count > 0. Clamped to the plot. Reset when the date or the screen width changes, kept when the data refreshes |
| Caption | *Time of day* (plus *· dashed line = now* on today), centered under the chart (10.5 px) |
| Hit areas | Each bar has a transparent full-height tap target, with a tooltip *2 cooking · 2:00 PM – 3:00 PM* |
| Readout | Under the chart (12.5 px muted, `aria-live`): ***2 cooking** · 2:00 PM – 3:00 PM* for the selected bar, else *Tap a bar to see its time window.* |

**Behavior**

- Tap a bar to select it; tap it again to clear.
- Redraw when the screen **width** changes. Ignore height-only changes,
  which happen when the keyboard opens.

---

## 12. UI action → service

| UI action | Service call | PIN | Audit | Change event |
|---|---|---|---|---|
| Create booking | `BookingService.CreateAsync` | — | — | `BookingsChanged(pickup date)` |
| Advance (card, after confirm) | `AdvanceStatusAsync(id, expected)` | — | — | `OrderChanged`, `BookingsChanged` |
| Advance (detail) | same, no confirm | — | — | same |
| Save / remove discount | `OrderService.SetDiscountAsync` | — | `discount.set` | `OrderChanged` |
| Record payment | `OrderService.RecordPaymentAsync` | Bookings (3 min) | `payment.recorded` | `OrderChanged` |
| Discount remaining balance | `OrderService.WriteOffBalanceAsync` | Bookings (3 min) | `order.balance_waived` | `OrderChanged` |
| Cancel order / booking | `OrderService.CancelOrderAsync` | Bookings (3 min) | `order.cancelled` | `OrderChanged`, `BookingsChanged` |
| Change pickup time | `OrderService.SetPickupAsync` | Bookings (3 min) | `order.pickup_changed` | `OrderChanged`, `BookingsChanged` (old and new date) |
| List, counts, strip and calendar dots | Booking queries | — | — | — |
| Dashboard | `DashboardService` | — | — | — |

- Every write starts with `EnsureUnlocked()`.
- Services throw `ValidationException`, `PinRequiredException` or
  `StaleStateException`. Show these as a toast, or inline in the open
  editor.

---

## 13. Seed data (prototype, useful for test fixtures)

"Today" = Fri, Oct 9, 2026. The next booking number today is 004.

**Products**

| Product | Cooking time | Price |
|---|---|---|
| Lechon Head | 180 min | ₱1,200 |
| Lechon Turkey | 150 min | ₱4,500 |
| Lechon Chicken | 30 min | ₱750 |
| Lechon Belly | 120 min | ₱2,200 |
| Liempo | 45 min | ₱650 |

**Orders and items**

| Order | Customer | Pickup | Total | Disc. | Paid | Items |
|---|---|---|---|---|---|---|
| o1 | Grace Villanueva | Oct 5 11:00 | 2,850 | | 2,850 | 261005-001 Belly ✓, -002 Liempo ✓ |
| o2 | Ramon Dela Cruz | Oct 6 09:30 | 1,200 | | 1,200 | 261006-001 Head ✓ |
| o3 | Kusina ni Nena (catering) | Oct 6 16:00 | 6,700 | | 4,000 | 261006-002 Belly ✓, -003 Turkey ✓ |
| o4 | Anna Reyes | Oct 7 10:00 | 650 | | 650 | 261007-001 Liempo ✓ |
| o5 | Mendoza Family Reunion | Oct 7 12:00 | 1,950 | | 1,950 | 261007-002 Head ✓, -003 Chicken ✓ |
| o6 | Camille Ong | Oct 8 18:00 | 750 | | 750 | 261008-001 Chicken · Cooking since 17:30 |
| o7 | Diego Santos | Oct 8 15:00 | 2,850 | | 1,000 | 261008-002 Belly · Cooked, -003 Liempo · Pending |
| o8 | Paulo Tan | Oct 8 17:30 | 1,200 | | 0 | 261008-004 Head · Pending |
| o9 | Steph Lim | Oct 8 13:00 | 4,500 | | 0 | 261008-005 Turkey · Cancelled |
| o10 | The Garden Events Co. | Oct 9 17:00 | 4,400 | | 2,000 | 261009-001, -002 Belly · Pending |
| o11 | Mark Villanueva | Oct 9 10:00 | 650 | | 650 | 261009-003 Liempo · Pending |
| o12 | Jen Dizon | Oct 10 11:00 | 1,500 | | 500 | 261010-001, -002 Chicken · Pending |
| o13 | Maria Santos | Oct 12 09:00 | 1,200 | | 500 | 261012-001 Head · Pending |
| o14 | Rosario Dizon | Oct 11 14:00 | 1,015 | 15 | 1,000 | 261011-001 Belly ✓ |

✓ = Completed.

**Cases worth testing with this data**

| Order | Case |
|---|---|
| o10 | Cancel **order** (2 items), with money paid |
| o11 | Cancel **booking** (1 item), fully paid |
| o7 | Viewing 261008-003 shows the can't-cancel caption (sibling is Cooked) |
| o7 | Order tags *1 of 2* / *2 of 2* on two different products; sorted by Product the two cards are far apart but both show *Order ₱2,850 · Paid ₱1,000* |
| o10 | Order tags on two units of the same product; after cancelling the order, both cancelled cards keep *1 of 2* / *2 of 2*, and the Cancelled chip shows them with their tags |
| o11 | Single-booking order: no order tag |
| o9 | Fully cancelled order: left out of money KPIs |
| o14 | Discount row with Edit/Remove and a net total |

**Known seed quirks** (fix in fixtures)

- o14's total (₱1,015) doesn't match one Lechon Belly (₱2,200).
- o14 is Completed with a future pickup.
- No seeded order has a balance under 10%, so the write-off banner needs
  its own fixture (e.g. total 2,000, paid 1,900).

---

## 14. Open points

1. **Money actions on fully cancelled orders.** The detail sheet still
   shows *+ Apply discount*, *+ Record payment* and the write-off banner
   when a cancelled order has a balance, while the Dashboard and CSV treat
   it as ₱0 (D23). List cards of a fully cancelled order also still show
   its money (*Order ₱4,500 · Paid ₱0* on 261008-005).
   *Suggest: hide all three, show Balance due ₱0 for fully cancelled
   orders, and replace the card's money line with* Cancelled · ₱0.
2. **Overpayment.** The payment editor has no upper limit. *Decide:
   allow, cap at the balance, or warn.*
3. **"Amount paid now" on New booking** isn't clamped in the prototype
   (negative accepted). The plan clamps to ≥ 0. *Follow the plan; decide
   whether to cap at the net total.*
4. **Pickup validation.** The prototype defaults to tomorrow 10:00 and
   falls back to today 10:00 if blank. Past times are accepted, and only
   the picker limits the window.
   *Decide: required field, default (e.g. next full hour), allow past
   pickups?, enforce ±6 months in `CreateAsync`.*
5. **"Latest first" in grouped view** reverses the day groups, but
   bookings inside a day stay earliest-first. *Confirm, or reverse both.*
6. **Sort ties** have no defined order. *Suggest booking ID as the
   secondary key for every sort.*
7. **Notes** exist on orders (seeded, and in the CSV) but the detail
   sheet doesn't show them and New booking can't enter them. *Decide:
   add a Notes row and field, or drop the column.*
8. **Product cards in New booking** show the whole catalog in the
   prototype (it has no inactive products). The app shows **active only**
   (plan).
9. **Status advance from a stale card.** The prototype has no
   `expectedStatus` check. The app must use it (plan §7.3) and show a
   toast when it refuses.

---

## 15. Acceptance checklist

**Date strip**

- [ ] Starts 2 days before today and marks days with bookings with an orange dot (no count).
- [ ] Arrows stop at ±6 months.
- [ ] Tapping the selected day clears it.
- [ ] A calendar pick outside the strip moves the strip.

**Booking list**

- [ ] Chips show global counts; search finds IDs, products, customers
      and "walk-in".
- [ ] The selected status chip is clearly marked (accent fill, check mark, ring), including All on first open.
- [ ] Grouping only with all dates + pickup sort.
- [ ] Every sort field and direction label matches §6.4.
- [ ] Compact rows show time only when the day is clear, expand into the
      full card, and collapse.
- [ ] Hiding columns in Settings changes the cards; Booking ID and the
      order tag always show.
- [ ] Cards of a multi-booking order show *1 of N* … *N of N* (numbered by
      booking ID, cancelled included) in both list views; single-booking
      orders show no tag.
- [ ] The money line reads *Order* / *Paid* with the order's figures, and
      the Settings columns are labelled *Order total* / *Order paid*.
- [ ] Card advance asks for confirmation; detail advance doesn't.
- [ ] Pending → Cooking stamps the start time.

**Detail sheet**

- [ ] Payment rows show and hide exactly per §8.3; only one editor at a
      time.
- [ ] A ₱0 payment closes without a PIN.
- [ ] A second payment within 3 minutes doesn't ask for the PIN; after
      3 minutes or a lock it does.
- [ ] Write-off appears under 10% and zeroes the balance using current
      values.
- [ ] Cancel order / Cancel booking labels; the PIN text lists IDs and
      paid money.
- [ ] Cancel is refused once anything is cooked or completed (also when
      it changes while the prompt is open).
- [ ] *Edit* next to Pickup shows only while the order can still be cancelled (nothing cooked or completed); saving asks for the
      PIN (not again within 3 minutes), moves every booking in the order and keeps the IDs; an unchanged time asks for nothing;
      the list, strip dots and Dashboard follow the new date.

**New booking**

- [ ] Multiple lines create sequential per-line IDs; a 3.2 kg line is one booking with one ID.
- [ ] After a create, the Booking created dialog lists every new ID
      (single-ID and multi-ID layouts) with the customer and pickup.
- [ ] The dialog stays open until Done or Back: no timer, and a backdrop
      tap doesn't close it.
- [ ] Totals and submit label update while typing, without losing focus.

**Dashboard**

- [ ] KPIs leave out fully cancelled orders.
- [ ] Breakdown shows only with a date.
- [ ] Kitchen load shows only for today or later and handles late pending
      items.
- [ ] Kitchen load always spans 12 AM – 11:59 PM with a label under every
      hour; it scrolls sideways with the y-axis labels fixed; today opens
      on the dashed now line, other dates near the first busy bar.
- [ ] Changes made in the detail sheet update the Dashboard and list.

**General**

- [ ] Back closes PIN prompt → dialog (including Booking created) →
      bottom sheet → full sheet.
- [ ] Toasts sit above the sheet footer and hide behind bottom sheets.
