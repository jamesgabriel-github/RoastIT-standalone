# Roastline — Menu Screen Spec (`menu-prototype.html`)

Status: Detailed spec of the Menu screen: the product catalog with
search, filters, sort, add, edit and activate/deactivate.

| | |
|---|---|
| Route | `/menu` |
| Layout | `MainLayout`, nav item **Menu** active |
| Services | `ProductService`, `IChangeNotifier`, `PinPromptService` |
| Rules | Plan §7.7, §9.5; decisions D7, D10 |
| Shared UI | `shared-ui-spec.md` |
| Older behavior notes | `menu-page-spec.md` (ignore its desktop layout) |
| Build phase | Phase 2 |

---

## 1. Page structure

**Top bar:** title **Menu**, subtitle *Products, pricing, and
availability*.

**Content, top to bottom:**

1. Search
2. Status chips
3. Meta row (count + Sort)
4. Product grid or empty state

**Overlays:**

| Overlay | Type |
|---|---|
| Add/Edit product (§6) | Full sheet |
| Sort (§4) | Bottom sheet, z 50/51 |
| PIN prompt | Shared |

**FAB:** **+ Add product**, hidden while the full sheet is open.

---

## 2. State

| State | Default | Notes |
|---|---|---|
| Search | '' | Page state; resets on app restart |
| Status filter | `all` | `all` / `active` / `inactive` |
| Sort field / direction | `name` / `asc` | |
| Drawer | none | `add` or `edit {id}` |
| Form | see §6 | Draft values as typed (strings) |
| Form error | '' | One message at a time |

**Refreshing**

- Subscribe to `CatalogChanged` and re-query.
- The New booking sheet on Bookings also listens, so newly added or
  re-activated products appear there.

---

## 3. List

### 3.1 Search

- Placeholder *Search products*.
- Matches product **name or description**, case-insensitive substring.
- Updates the grid and count on each keystroke without re-rendering the
  field.

### 3.2 Status chips

- **All · 5**, **Active · 5**, **Inactive · 0**.
- Counts are **global**: they ignore the search.

### 3.3 Meta row

- **Left:** count *5 products* / *1 product* (shown results).
- **Right:** Sort meta button: ↑/↓ icon + *{Field} · {direction label}*,
  e.g. *Name · A → Z* (ellipsis if it doesn't fit).

### 3.4 Grid

- CSS grid `repeat(auto-fill, minmax(260px, 1fr))`, 12 px gap.
- One column on a phone; two or more on tablets.

### 3.5 Product card

Card radius 14, padding 14, 10 px gap.

| Part | Spec |
|---|---|
| Top row | Name (16 px bold, line height 1.3, wraps) · status pill on the right: **Active** (cooked color) or **Inactive** (pending color) |
| Price row | Price mono 21 px / 600 (*₱2,200*) · muted *· 2 hr cook* |
| Description | 13 px muted, line height 1.5, **clamped to 2 lines**. Empty → *No description yet.* |
| Actions | Divider, then two small buttons sharing the width: **Edit** · **Deactivate** / **Activate** |
| Inactive card | Whole card at 72% opacity |

The card itself isn't tappable; only its buttons are.

### 3.6 Empty states

| Situation | Text |
|---|---|
| No products at all | No products in the menu yet. Add your first one to get started. |
| Search has no match | No products match "{search text}". (curly quotes, trimmed text) |
| Filter has no match (no search) | No products match this filter. |

---

## 4. Sort sheet ("Sort products")

| Field | Asc label | Desc label |
|---|---|---|
| Name *(default, asc)* | A → Z | Z → A |
| Price | Low to high | High to low |
| Cooking time | Shortest first | Longest first |

**Sheet contents**

1. Field rows; the selected row is accent with a check.
2. Direction Segmented with the field's labels.
3. **Done**.

Picking a field or direction applies immediately; Done closes. Changing
the field keeps the direction.

---

## 5. Activate / Deactivate (from a card)

1. Tap **Deactivate** or **Activate**.
2. PIN prompt (Menu scope, **every time**):

   | Action | Detail | Key |
   |---|---|---|
   | Deactivate | Hide {name} from new bookings. | Deactivate |
   | Activate | Make {name} available for booking again. | Activate |

3. `ProductService.SetActiveAsync(id, active, confirmation)`. Audit
   `product.active_changed`; raises `CatalogChanged`.
4. Toast *{name} hidden from new bookings* / *{name} is available again*.

**Effects**

- Inactive products disappear from the New booking product picker, and
  `CreateAsync` rejects them.
- Existing bookings keep them.
- There is **no delete** anywhere.

---

## 6. Add / Edit product (full sheet)

**Header:** close (X, *Close*) · *Add product* or *Edit product*.

**Body:** a form (Enter in a single-line field submits).

| Field | Control | Add default | Edit default | Notes |
|---|---|---|---|---|
| Product name | Text, `enterkeyhint="next"` | blank | current | Placeholder *e.g. Lechon Belly*. Trimmed |
| Price | ₱ field, decimal keyboard, min 0 | blank | current | Left half of a 2-column row. Placeholder *0* |
| Cooking time (min) | Numeric, min 1 | blank | current | Right half. Placeholder *30* |
| Description *(optional)* | Textarea, 3 rows | blank | current | Placeholder *What makes this product worth ordering*. Trimmed |
| Available for booking | Toggle card with a switch | **on** | current | Caption *Inactive products are hidden from the new-booking form.* |
| Error line | `role="alert"`, 13 px / 600, cancelled color | hidden | | Under the fields |

**Footer:** **Cancel** + primary **Add product** / **Save changes**.

### 6.1 Validation

Checked in this order on submit. Only the **first** failing message
shows; the sheet stays open.

| # | Rule | Message |
|---|---|---|
| 1 | Name not blank after trimming | Give the product a name. |
| 2 | Price is a number ≥ 0 | Price must be a number of 0 or more. |
| 3 | Cooking time is a number ≥ 1 | Cooking time must be at least 1 minute. |

The same rules live in the `Core` FluentValidation validator; the
service re-checks (plan §5).

### 6.2 Saving

1. Validation passes → the error clears → PIN prompt (Menu scope, every
   time):
   - Add: *Add {name} to the menu.* → **Add**
   - Edit: *Save changes to {name}.* → **Save**
2. **Add:** `ProductService.CreateAsync(values, confirmation)`. Audit
   `product.created`.
3. **Edit:** `ProductService.UpdateAsync(id, values, confirmation)`.
   Audit `product.updated`. If the switch changed, the same save also
   changes `active` (one PIN, not two).
4. `CatalogChanged`. The sheet closes, the form resets, toast *Added
   {name}* / *Saved {name}*.
5. Cancelling the PIN prompt leaves the sheet open with the typed values.

**Price changes affect new bookings only.** `orders.total_amount` is
fixed at creation (plan §6.2).

**Closing the sheet** (X, Cancel, backdrop or Back) discards the draft
without asking.

---

## 7. Data

| UI value | Stored as | Conversion |
|---|---|---|
| Price (₱) | `products.price` `long` centavos | Parse the input as pesos → × 100, rounded. Display with the shared money format |
| Cooking time | `cooking_time_min` integer | See §10 #1 |
| Active | `active` bool | |
| Description | `description` null when blank | |

**Seed products** (prototype)

| Name | Price | Cooking time | Description |
|---|---|---|---|
| Lechon Head | ₱1,200 | 180 min | Whole roasted pig head — a budget-friendly centerpiece for smaller gatherings. |
| Lechon Turkey | ₱4,500 | 150 min | Turkey roasted lechon-style, stuffed with the same herb blend as the whole pig. |
| Lechon Chicken | ₱750 | 30 min | Our fastest order — a whole chicken roasted to order, great for walk-ins. |
| Lechon Belly | ₱2,200 | 120 min | Boneless rolled pork belly with crisp skin — the most ordered cut. |
| Liempo | ₱650 | 45 min | Grilled pork belly strips, marinated overnight. |

All five start Active.

---

## 8. UI action → service

| UI action | Service call | PIN | Audit | Event |
|---|---|---|---|---|
| List, counts | Product query | — | — | — |
| Add | `ProductService.CreateAsync` | Menu, every time (single-use confirmation, ~60 s) | `product.created` | `CatalogChanged` |
| Save edits | `ProductService.UpdateAsync` | Menu, every time | `product.updated` | `CatalogChanged` |
| Activate / Deactivate | `ProductService.SetActiveAsync` | Menu, every time | `product.active_changed` | `CatalogChanged` |

- Every write starts with `EnsureUnlocked()` and
  `EnsureConfirmed(confirmation, PinScope.Menu)`.
- Menu actions never start or use the 3-minute Bookings window.

---

## 9. Toasts

| Event | Text |
|---|---|
| Added | Added {name} |
| Saved | Saved {name} |
| Deactivated | {name} hidden from new bookings |
| Activated | {name} is available again |

---

## 10. Open points

1. **Cooking time decimals.** The prototype accepts `1.5` (any number
   ≥ 1). The column is an integer. *Suggest: whole minutes only; the
   validator rejects decimals with the same message, or rounds.*
2. **Price centavos.** The price field steps by 1 (whole pesos) in the
   prototype. Money is stored in centavos. *Decide: allow centavos
   (step 0.01), or whole pesos only.*
3. **Duplicate names** are allowed. *Decide: allow, warn, or block
   (case-insensitive, trimmed).*
4. **Field limits.** No maximum length for name or description, no
   maximum price or cooking time. *Suggest: name ≤ 60, description ≤ 300,
   cooking time ≤ 1,440.*
5. **Unsaved changes.** Closing the sheet discards edits silently.
   *Keep, or ask "Discard changes?" when the form is dirty.*
6. **Error placement.** The prototype shows one message at the bottom
   of the form. The plan says "inline" on the first failing field.
   *Suggest: show it under that field and focus it.*
7. **Sort ties** (same price or cooking time) have no defined order.
   *Suggest name as the secondary key.*

---

## 11. Acceptance checklist

- [ ] Chips show global counts; search matches name and description.
- [ ] All three empty-state texts appear in the right situations.
- [ ] Sort fields and labels match §4; the default is Name A → Z.
- [ ] Cards show price, cooking time, a 2-line description (or *No
      description yet.*), and dim when inactive.
- [ ] Validation messages appear in order, one at a time, without a PIN
      prompt.
- [ ] Add, Save and Activate/Deactivate ask for the PIN **every time**,
      even within 3 minutes of a Bookings PIN.
- [ ] Cancelling the PIN keeps the sheet and values.
- [ ] An inactive product is hidden from New booking but still shows on
      its existing bookings.
- [ ] Editing a price doesn't change existing order totals.
- [ ] No delete action exists.
- [ ] Back closes the PIN prompt, then the sort sheet or product sheet.
