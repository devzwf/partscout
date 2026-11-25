# Changelog

All notable changes to PartScout. Versions follow the `VERSION` file and the image tags of `devzwf/partscout`.

## 0.1.7 — 2026-11-25

- Sign-out button in the top bar (power icon, the user name shows on hover). Signing out from the settings still works.
- Dependency updates: Microsoft.Data.Sqlite 10.0.15, Caddy 2.11.9 image refreshed (new digest), Vite 8.3.8, @vitejs/plugin-react 6.3.0, test tooling.

## 0.1.6 — 2026-11-14

Security hardening, after an audit.
- **Behind HTTPS, set `PARTSCOUT_COOKIE_SECURE=always`** in `.env`: the session cookie is then never sent over plain HTTP. The default (`auto`) keeps plain HTTP working on a local network.
- Protection against cross-site requests: the API refuses any change that does not come from the PartScout interface. A browser tab left open on the previous version will ask you to reload the page.
- A sign-in lasts at most 90 days, even when used every day. **Everyone has to sign in again once after upgrading.**
- Request size limits (64 KB, 1 MB for BOMs).
- The app refuses to start if `PARTSCOUT_MASTER_KEY` or `PARTSCOUT_SETUP_TOKEN` still holds the example value `change-me`.
- Supplier images given in `http://` are loaded over `https://`.
- Clear message, and exit code 1, when the data folder is not writable (on a host folder: `chown -R 1654:1654`).
- Caddy image pinned by digest.

## 0.1.5 — 2026-11-08

- First public release. Same application as 0.1.4, with the new logo.

## 0.1.4 — 2026-11-06
- Movable columns in both views: drag a header onto another one, or use Alt+Shift+← / → on a header. The order is kept per view in the browser; **Default columns** puts it back.
- Dependency updates: Caddy 2.11.9, Node 26 for the interface build, Vite 8.3.6, Newtonsoft.Json 13.0.4, test tooling. .NET stays on the 10 LTS.
- Build stages run on the build machine's architecture, ready for multi-architecture images (only amd64 is published for now).

## 0.1.3 — 2026-11-05
- The interface language belongs to the account: FR/EN button in the top bar and on the sign-in screen. The browser's language only decides the very first visit.

## 0.1.2 — 2026-11-05
- Several users: the first account is the administrator, who creates the others. Each person has their own language, API keys, settings, BOMs and history.
- English interface.
- Optional HTTPS through Caddy, with your own certificate files.
- "By offer" view fixed.

## 0.1.1 — 2026-11-04
- Version shown next to the logo.

## 0.1.0 — 2026-11-04
- First version: search DigiKey, Mouser, element14 / Newark / Farnell and TME at once; group offers by part; best price for the quantity, MOQ and price breaks included; currency conversion (ECB rates); BOM with CSV export and import; key vault; demo mode; hourly database copies kept 14 days.
