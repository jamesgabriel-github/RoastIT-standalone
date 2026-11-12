# Changelog

One line per release. `ApplicationVersion` goes up by 1 each time (plan §14).

## 1.1.0 (build 2) — 2026-11-12
- **Lock (D37):** switching to another app no longer locks RoastIT, so you can take a call
  or use the camera and come back without re-entering the PIN. The app now locks when it is
  **closed** and after **30 minutes** without use (it was 10). Time spent in another app
  counts towards the 30 minutes.
- **Late to start (D38):** a **Pending** booking that should already be cooking
  (pickup time minus the product's cooking time) is flagged in the Booking list. Detailed
  card: red stripe, a "Late to start · was due 3:00 PM (20 min ago)" line and a highlighted
  "Start cooking" button. Compact row: red stripe and a "Late" tag. After the pickup time it
  reads "Not started". The detail sheet has a new "Start cooking by" row for Pending
  bookings. The flag appears on the minute and updates by itself; "Start cooking" clears it.
- No database change: the update keeps all existing data.

## 1.0.0 (build 1) — 2026-11-08
First release for the shop's phone: PIN lock, Menu, Bookings (weights in kilos, pickup
changes, payments, discounts, cancel), Dashboard, Settings, export/import backups,
"Save to a folder…", and the automatic 48-hour CSV backup.
