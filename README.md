# Filio

Automatic file organizing for Windows — Filio watches your Downloads folder and files every document away by itself.

## What it does

Filio is a small background tool that sits in your Windows system tray and watches your Downloads folder (or any other folder you choose). Every time a new file lands there — an invoice, a contract, a report, or even a scanned photo of a receipt — Filio automatically figures out what kind of document it is and which client it belongs to, then moves it into a clean `Client \ Year \ Document Type` folder structure. Recognition uses local content rules plus OCR for images and scanned photos, so handwritten or scanned invoices get sorted just like PDFs. Every file is checked against Windows Defender before it's filed, duplicate files are detected by content (not just name) and routed to a separate folder for review, and every single move is logged so it can be undone with one click. Filio never deletes a file — it only organizes what's already there.

## Download & install

**[⬇️ Download the latest version](https://github.com/ofirshudari1-ship-it/filio/releases/latest)**

1. Go to the [latest release](https://github.com/ofirshudari1-ship-it/filio/releases/latest) and download the `Filio-Setup-<version>.exe` installer.
2. Run the installer and choose your language (English or Hebrew).
3. Follow the installation wizard — pick an install location, review the license, and choose your shortcuts.
4. On first launch, a short setup wizard asks which folder to watch and where filed documents should go.
5. Done — Filio now runs quietly in the system tray and files new downloads automatically.

**System requirements:** Windows 10 (version 1809 or later) or Windows 11, 64-bit, ~150 MB free disk space. Windows Defender should be active for the pre-filing security scan (recommended, not required). Administrator rights may be needed only if you choose to install into `Program Files`.

## Key features

- Automatic filing by client, year, and document type — for PDFs, Word documents, and images
- Local OCR (Tesseract) for scanned images and photos, in English and Hebrew, entirely on your machine
- Content-based duplicate detection (SHA-256), routed to a review folder instead of being overwritten
- Pre-filing Windows Defender scan on every file
- One-click undo for the last filed file
- Optional "ask before filing" mode for files that don't match any known client
- "Fix / File…" correction tool that also teaches Filio new client aliases for next time
- Optional sorting of installer files (`.exe`/`.msi`) into their own `_Installers\Year` folder
- Full Hebrew and English interface with RTL support, switchable instantly without restarting
- Self-healing folder watcher that recovers automatically if Windows interrupts it (sleep, restart, updates)
- Support for watching multiple folders at once

## Automatic updates

Filio checks GitHub for a newer release once per session, a few seconds after startup. This check is notify-only — it never downloads or installs anything on its own. If a newer version is available, you'll get a tray notification with a link to grab it yourself. See all releases at [github.com/ofirshudari1-ship-it/filio/releases](https://github.com/ofirshudari1-ship-it/filio/releases).

## Privacy

Filio is fully local-first. Document text extraction, OCR, and client/document-type classification all run entirely on your own computer — no file content, filename, or document is ever sent to an external server. The only network activity is the once-per-session check against the public GitHub Releases API to see if a newer version exists, which sends no personal or file data.
