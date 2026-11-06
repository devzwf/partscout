# Changelog

All notable changes to PartScout. Versions follow the `VERSION` file and the image tags of `devzwf/partscout`.

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
