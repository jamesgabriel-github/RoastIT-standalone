# Roastline — Lock Screen Spec (`signin-prototype.html`)

Status: Detailed spec of the Lock screen: first-run PIN setup, unlock,
forgot PIN and change PIN.

| | |
|---|---|
| Route | `/lock` (change mode: `/lock?mode=change`, opened from Settings → Update PIN) |
| Layout | `LockLayout` (no nav, no FAB) |
| Services | `IAppLock`: `IsSetUpAsync`, `SetUpAsync`, `UnlockAsync`, `ChangeAsync`, `ResetWithRecoveryCodeAsync` |
| Rules | Plan §9.1–§9.4, decisions D4–D6 |
| Shared UI | `shared-ui-spec.md` (tokens, theme, toast, keypad look) |
| Build phase | Phase 1 |

---

## 1. When the screen shows

| Situation | Starting view |
|---|---|
| No `app_lock` row (first run, or after reinstall) | **Enter (setup)** |
| PIN set up and the app is locked (launch after the app was closed, 30 min idle) | **Unlock** |
| Settings → Update PIN | **Verify (change)** |

**Rules**

- While the app is locked, every other route redirects to `/lock`.
- Unlocking, finishing setup or finishing a reset opens **Bookings**.
- Finishing a change returns to **Settings** (the app stays unlocked
  throughout the change flow).

---

## 2. Layout

Column, max width 420 px, centered, min height `100dvh`. Padding: 16 px +
safe-top above, 24 px sides, 20 px + safe-bottom below. Gap 18.

**Top row** (min height 44): brand on the left, optional link on the
right.

- **Brand:** 34 × 34 accent tile (radius 9, white flame 19 px), then
  **Roastline** (16 px bold) over *Litson Haus* (11.5 px muted).
- **Right link** (accent, 14 px / 600):

  | View | Link | Goes to |
  |---|---|---|
  | Verify / Enter / Confirm in the change flow | **Cancel** | Settings, nothing changed |
  | Forgot | **Back** | Unlock |
  | Anything else | none | |

**Main** (fills the remaining height, content vertically centered, 20 px
gap):

1. Heading block, centered: title 22 px bold (−0.01em), subtitle 14 px
   muted, 6 px gap.
2. Dots row (§4).
3. Message line (§6).
4. Keypad (§4) or the view's form.
5. View extras (links, notes).

---

## 3. Views

| View | Title | Subtitle | Dots hint | Confirm key | Extras |
|---|---|---|---|---|---|
| **Enter · setup** | Set up your PIN | 4 to 6 digits. Roastline asks for it every time the app opens. | Enter 4–6 digits | Next | — |
| **Enter · reset** | Choose a new PIN | Your recovery code checked out. 4 to 6 digits. | Enter 4–6 digits | Next | — |
| **Enter · change** | Choose a new PIN | 4 to 6 digits. | Enter 4–6 digits | Next | Cancel link |
| **Confirm** (all flows) | Confirm your PIN | Enter the same PIN again. | Re-enter your PIN | Save | Cancel link in change flow |
| **Unlock** | Welcome back | Enter your PIN to open Roastline. | Enter your PIN | Unlock | **Forgot PIN?** link under the keypad |
| **Verify · change** | Enter your current PIN | Confirm it's you before changing it. | Enter your current PIN | Next | Cancel link |
| **Forgot** | Use your recovery code | It's the 12-character code you saved when you set up your PIN. | — | — | Code form (§7), note, Back link |
| **Recovery code** | Save your recovery code | If you forget your PIN, this code is the only way back in. | — | — | Code card (§8) |
| **Done** | per flow (§5) | per flow | — | — | Success icon + button |

---

## 4. Dots and keypad

**Dots row**

- Height 24, 14 px gap.
- One 14 px `--fg` dot per digit entered, or the muted hint (13 px) when
  empty.
- `aria-live="polite"`, label "N digits entered".
- Shakes (0.35 s, ±8 px) on a wrong PIN or a mismatch.

**Keypad**

- 3 × 4 grid, max width 300, 12 px gap.
- Keys 64 px tall, radius 16, border, `--surface`, mono 24 px / 600;
  pressed `--surface-2`.
- Rows 1–3: digits 1–9.
- Row 4:
  - **Delete**: transparent, muted, backspace icon 22 px.
  - **0**.
  - Confirm key: accent, 15 px bold sans, label from the view.

| Rule | Behavior |
|---|---|
| Digits | Max 6; extra taps ignored. Typing clears the message line |
| Delete | Removes the last digit |
| Confirm key | Disabled until 4 digits |
| Lockout | Every key disabled (40% opacity) |

**Physical keyboard** (Unlock, Verify, Enter, Confirm views; ignored
while typing in a text field):

| Key | Action |
|---|---|
| 0–9 | Enter digit |
| Backspace | Delete |
| Enter | Confirm |

- Enter with fewer than 4 digits shows *Use 4 to 6 digits.* and shakes.
  The on-screen confirm key can't trigger this because it's disabled.

---

## 5. Flows

### 5.1 First run

1. **Enter (setup)** → Next stores the first entry, clears the dots and
   goes to Confirm.
2. **Confirm** → Save:
   - Match: save the PIN, generate a recovery code, go to **Recovery
     code**.
   - No match: back to **Enter (setup)**, message *PINs didn't match.
     Choose your PIN again.*, shake.
3. **Recovery code** → tick the checkbox → **Continue**.
4. **Done**: *You're all set* · *Opening Bookings…* · button **Open
   Bookings**. Goes to Bookings automatically after ~1.4 s.

Service: `SetUpAsync(pin)` returns the plain recovery code once; only
hashes are stored.

### 5.2 Unlock

1. **Unlock** → Unlock:
   - Correct: failure counter resets, then **Done**: *Unlocked* ·
     *Opening Bookings…* → Bookings after ~1.4 s.
   - Wrong: §6.
2. **Forgot PIN?** opens **Forgot**.

Service: `UnlockAsync(pin)`.

### 5.3 Forgot PIN

1. **Forgot** → enter the code → **Verify code**:
   - Correct: counter resets, go to **Enter (reset)**.
   - Wrong / empty: §6.
2. **Enter (reset)** → **Confirm**. A mismatch goes back to Enter
   (reset).
3. **Recovery code** with a **new** code, plus the notice *Your old
   recovery code no longer works.*
4. **Done**: *PIN reset* · *Opening Bookings…* → Bookings after ~1.4 s.

**Service**

- Verify the code without changing anything, then
  `ResetWithRecoveryCodeAsync(code, newPin)` sets the new PIN hash and a
  new recovery-code hash in one step.
- Audit: `pin.reset_with_recovery`.

### 5.4 Change PIN (from Settings)

1. **Verify** → enter the current PIN → Next:
   - Correct: go to **Enter (change)**.
   - Wrong: §6.
2. **Enter (change)** → **Confirm**. A mismatch goes back to Enter
   (change).
3. **Done**:
   - Title *PIN updated*.
   - Text *Use your new PIN the next time Roastline locks. Your recovery
     code stays the same.*
   - Button **Back to Settings**; no automatic redirect.

**Service**

- `ChangeAsync(currentPin, newPin)`; the recovery code is unchanged.
- Audit: `pin.changed`.
- **Cancel** on any step returns to Settings with nothing changed.

---

## 6. Messages and lockout

The message line sits under the dots: min height 20, centered, 13 px /
600, cancelled color, `role="alert"`.

| Event | Message |
|---|---|
| Fewer than 4 digits submitted (keyboard) | Use 4 to 6 digits. |
| Confirm doesn't match | PINs didn't match. Choose your PIN again. |
| Wrong PIN (Unlock, Verify) | Incorrect PIN. 3 tries left. (*1 try left.* when one) |
| Wrong recovery code | That recovery code isn't right. 3 tries left. |
| Empty recovery code | Enter your recovery code. |
| Locked out | Too many tries. Try again in 27s. (counts down every second) |

**Lockout**

- 5 wrong tries (PIN or recovery code, any mix) disables input for
  **30 s**: keypad keys, or the code field and Verify button.
- When the 30 s run out, the message clears, input re-enables and the
  counter starts again from 0.

**Real app (plan §9.3)**

- `failed_attempts` and `locked_until` are stored in `app_lock`, so a
  restart doesn't reset them.
- The counter is shared with the in-app PIN prompts.
- Messages never reveal anything about the stored PIN.

---

## 7. Forgot view

Form: column, 14 px gap.

| Element | Spec |
|---|---|
| Field label | Recovery code |
| Input | 50 px, radius 12, mono, 0.08em letter spacing, uppercase, centered. Placeholder `XXXX-XXXX-XXXX`. Autocomplete, autocorrect and spellcheck off; capitalization = characters |
| Message line | As §6 |
| Button | **Verify code** (primary, 50 px, full width). Disabled during lockout |
| Note | *Lost the code too? The only option left is reinstalling the app, which erases all bookings on this device unless you have a backup.* (12.5 px muted, centered) |

- Typing updates state only; no re-render while typing.
- Submitting the form (Enter key) is the same as tapping Verify.
- **Matching:** uppercase both sides and drop everything that isn't A–Z
  or 0–9, so `rstl 7q4m-k2pd` matches `RSTL-7Q4M-K2PD`.

---

## 8. Recovery code view

1. **Code card:** `--surface`, border, radius 14, padding 18, centered,
   12 px gap.
   - The code in mono 22 px / 600, 0.06em spacing, shown as
     `XXXX-XXXX-XXXX`.
   - **Copy** button: 38 px, `--surface-2`, copy icon. Toast *Recovery
     code copied*, or *Copy not available — write it down* when the
     clipboard can't be used.
2. **Reset flow only:** notice *Your old recovery code no longer works.*
3. Notice: *Without this code, a forgotten PIN means reinstalling the
   app — which erases all bookings on this device.*
4. Checkbox (20 px, accent): *I've written this code down somewhere
   safe.*
5. **Continue** (primary, full width): disabled until the box is ticked.

Notices: accent at 8% background, 35% accent border, radius 12, padding
10 × 12, 13 px.

**Code generation**

- 12 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no 0, O, 1, I,
  L), using a secure random source, grouped 4-4-4.
- The real app should avoid modulo bias: use
  `RandomNumberGenerator.GetInt32(alphabet.Length)` per character.
- Shown only on this screen, never again. Only its hash is stored.

---

## 9. Done view

- Success icon: 60 px circle, cooked color at 14% fill, cooked border,
  26 px check.
- Title and text as in §5.
- Full-width primary button (a link): **Open Bookings** or **Back to
  Settings**.
- Setup, unlock and reset redirect to Bookings after ~1.4 s; the button
  covers impatient users. Change doesn't redirect.

---

## 10. State model (prototype → app)

| Field | Meaning |
|---|---|
| `view` | enter · confirm · recovery · unlock · verify · forgot · done |
| `flow` | setup · reset · change (decides the Enter copy and where Done goes) |
| `entry` | Current keypad digits |
| `firstEntry` | PIN typed on Enter, compared on Confirm. Never persisted |
| `codeInput` | Recovery code field |
| `message`, `shake` | Message line and shake animation |
| `failed`, `lockedUntil` | Lockout state. In the app these come from `app_lock` |
| `savedAck` | Recovery checkbox |
| `done` | `{ title, text, cta, href, redirect }` for the Done view |

Leaving a view clears `entry`, `firstEntry`, `codeInput` and `message`.

---

## 11. Prototype-only

- The **Prototype controls** panel at the bottom (First run / Unlock flow
  / Change PIN flow, showing the current PIN and recovery code): remove.
- The PIN is stored in plain text in browser storage
  (`roastline.pin`, default `1234`) so the other prototypes can check it.
  The seed recovery code is `RSTL-7Q4M-K2PD`. The app stores hashes only.
- Mode comes from `?mode=unlock` / `?mode=change`. The app decides from
  `IsSetUpAsync()` and the route.
- The source comment mentions
  `standalone/web-to-blazor-hybrid-transition.md §4`; the current source
  of truth is `implementation-plan.md` §9.

---

## 12. Open points

1. **Unlock after auto-lock always opens Bookings.** If the app locked
   while the owner was on Menu or Settings, or had a sheet or form open,
   that context is lost. *Decide: return to the previous route (sheets
   and unsaved forms can't be restored), or always open Bookings.*
2. **Android Back on the lock screen.**
   - *Suggest:* on Unlock or first-run setup, Back sends the app to the
     background (never reveals pages).
   - *Suggest:* in the change flow, Back = Cancel.
   - *Suggest:* on Forgot, Back = Back link.
   - *Suggest:* on the Recovery code view, Back does nothing, so the code
     isn't skipped.
3. **Leaving setup half-done.** If the app is killed on the Recovery code
   view, the PIN is already saved but the owner may not have the code.
   *Decide: save the PIN and code hash only when Continue is tapped, or
   accept the risk.*
4. **Brand subtitle "Litson Haus"** is hard-coded. *Confirm it's the
   business name, or move it to a constant/setting.*
5. **Done delay (~1.4 s)** before opening Bookings. *Keep for feedback, or
   go straight to Bookings after unlock.*

---

## 13. Acceptance checklist

- [ ] Fresh install starts on **Set up your PIN**; no other route is
      reachable.
- [ ] Confirm mismatch returns to Enter with the mismatch message.
- [ ] Recovery code shows once, copies, and Continue stays disabled until
      the box is ticked.
- [ ] Unlock with the right PIN opens Bookings; a wrong PIN shows tries
      left.
- [ ] 5 wrong tries lock input for 30 s with a countdown, and the lockout
      survives killing the app.
- [ ] Wrong tries in an in-app PIN prompt count toward the same lockout.
- [ ] A recovery code with lower case and spaces works; after a reset the
      old code fails and a new code is shown.
- [ ] Change PIN keeps the recovery code, returns to Settings, and Cancel
      changes nothing.
- [ ] Physical keyboard works on the keypad views.
- [ ] Light and dark themes both render correctly.
- [ ] The PIN, its hash and the recovery code never appear in logs or
      Preferences.
