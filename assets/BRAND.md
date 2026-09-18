# Filio — Brand Guidelines

## Palette

| Token | Value | Usage |
|---|---|---|
| `primary` | `#1E7F72` | Buttons, links, active state |
| `primary-dark` | `#0F5348` | Header gradient, hover |
| `accent` | `#DB8A1F` | CTAs, primary button fill |
| `background` | `#F7F9F8` | App background (light) |
| `surface` | `#FFFFFF` | Cards, panels (light) |
| `surface-alt` | `#F2F6F4` | Striped rows, secondary surface |
| `border` | `#DCE5E2` | Card borders |
| `text-muted` | `#5E6E69` | Subtitles, help text |
| `success` | `#2E8B57` | Status pill active |
| `warning` | `#C97B1C` | Status pill paused |
| `error` | `#C0392B` | Danger button, error state |

## Dark Mode Palette

| Token | Value |
|---|---|
| `background` | `#0D1A18` |
| `surface` | `#152420` |
| `surface-alt` | `#1C302B` |
| `border` | `#2A4040` |
| `text-muted` | `#8ABDB5` |

## Typography

- **Font family:** Segoe UI (Windows system font) — no web font needed
- **Hebrew:** Segoe UI supports Hebrew; fallback chain: `"Segoe UI", "Arial Hebrew", sans-serif`
- **Scale:** 11px caption / 12px body-sm / 13px body / 14px body-lg / 16px subtitle / 19px title / 26px hero

## Logo

Filio's mark: an auto-filing folder with a bold "filed" checkmark, topped by a small teal accent dot — the dot echoes the dot over the "i" in "Filio", tying the mark directly to the wordmark instead of an unrelated mascot. No animal, no clip-art.

- **Source of truth:** `design-source/filio_logo_master.svg` (512×512, full color) — the two-layer folder (back tab + front body), a thick white checkmark, and the teal dot accent.
- **Small-size variant:** app icons at 16/24/32px use a *further-simplified* rendering (single-layer folder silhouette, no back tab, bolder checkmark/dot) rather than a naive shrink of the master — the two-layer detail disappears at tray/taskbar size otherwise. Both variants are produced by the same generation script from one source design.
- **Raster exports:** `design-source/filio_logo_master_512.png` (full variant) and `app/Filio.App/Assets/filio-{16,24,32,48,64,128,256}.png`.
- **App icon (.ico):** `app/Filio.App/Assets/filio.ico` — multi-resolution (16/24/32/48/64/128/256), built from the same exports. Used as `ApplicationIcon` in the csproj, so it's the exe icon, taskbar icon, and (via `Icon.ExtractAssociatedIcon`) the tray icon — one source, no drift.
- **In-UI placements:** splash screen, main window header, and onboarding brand rail all render the same PNG export (`/Assets/filio-*.png` via WPF pack URI) — no emoji placeholder.
- **Installer:** `installer/wizard_large.bmp` (164×314) and `installer/wizard_small.bmp` (55×58) are generated from the same master logo composited on the brand gradient (`#0F5348` → `#1E7F72`), not the generic Inno Setup default banner. `SetupIconFile` in `setup.iss` also points at `Assets/filio.ico`.
- **Variations still open:** a hand-tuned monochrome/single-ink version and a dedicated dark-background variant (STANDARDS.md asks for both) — today the full-color mark is used everywhere, including on the dark gradient header/splash/installer banner, where it already reads cleanly since the folder is orange/teal against dark teal. A true 1-color silhouette export is a nice-to-have, not yet produced.

## Voice

- Friendly, practical, בעברית ובאנגלית
- Not overly technical — speak in terms of user outcomes ("מתייק לבד", not "triggers FileSystemWatcher event")
- Filio is the agent that does the filing, not the user
