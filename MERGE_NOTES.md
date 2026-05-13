# GuestGate merge notes

Base project: `GuestGate-master`.
Source checked for improvements: `GuestGateoo-master`.

Merged into the base project:

- Config-driven kiosk screensaver endpoint: `GET /api/kiosk/screensaver`.
- Hotel screensaver images from `GuestGateoo-master/GuestGate/wwwroot/img/hotel`.
- Tablet screensaver config loading, clock display, and Wake Lock request.
- Mobile page responsive Bootstrap layout, country-code mobile input behavior, copy-data action, safer loading/saving status messages, and dynamic template compatibility.
- Diagnostic endpoints: `/diag/health`, `/diag/test-log`, `/diag/test-seq`.
- Serilog self-log/startup diagnostics and enriched request logging.
- Desktop cancel/end flow fixed to call the existing `DELETE /api/sessions/active` endpoint, with fallback to `POST /api/sessions/cancel`.
- `appsettings.json` updated with candidate-style screensaver settings, `Seq` diagnostics section, `SqlDependency.Enabled=false`, and HTTPS CORS origin.

Not merged directly:

- `GuestGateoo-master` direct-SQL kiosk queue controllers/watchers and JWT guest-token workflow. They require a different `dbo.Guests` schema (`FullName`, `KioskStatus`, `ForKiosk`, etc.), while the base project uses JSON guests plus `KioskSessions`. Copying those files directly would conflict with the current data model and API flow.

## Hotfix - tablet form appears without manual refresh

- Normalized kiosk IDs (`kid`) for SignalR groups and session API calls so `K1` and `k1` do not split realtime notifications into different groups.
- `/api/sessions/start` now broadcasts `sessionStarted` even when an active session already exists, which lets the tablet recover if the first realtime message was missed.
- Tablet `index.html` now has a 1-second active-session polling fallback in addition to SignalR, so the data-entry form appears automatically even if the SignalR event is missed or reconnecting.
- Added handling for `sessionEnded` so desktop cancellation can return the tablet to the screensaver.

## Smart phone input merge
- Mobile and tablet dynamic forms automatically render a smart phone picker for fields detected by data type, key, or label, including Arabic labels such as `موبايل`, `جوال`, `هاتف`, `تليفون`, and `تلفون`.
- Phone values are saved in international E.164 style when possible, e.g. `+9665xxxxxxx`.

## Offline smart phone input update

- Replaced the online intl-tel-input phone component with a local/offline GuestGate phone component based on `smart_phone_input_all_countries.html`.
- The component now includes the all-countries list, flags, dial codes, country search, local digit normalization, automatic country detection from `+` and `00` prefixes, and E.164 value saving in `/wwwroot/lib/guestgate-phone-input.js`.
- Removed the phone-input CDN CSS/JS from tablet and mobile pages.
