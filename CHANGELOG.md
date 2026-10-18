# Changelog

All notable changes to PrinterDesk (formerly ScanDesk) are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/). Each release on GitHub shows its section of this file.
Versions stay below 1.0.0 while the app is still changing quickly; 0.1.0 and 0.2.0 were first
published as 1.0.0 and 1.1.0.

## [Unreleased]

## [0.3.0] - 2026-10-18

### Changed

- Renamed from ScanDesk to **PrinterDesk**, ahead of adding printing. The exe is now `PrinterDesk.exe`;
  settings and unsaved pages from ScanDesk are moved over on first start.

## [0.2.0] - 2026-10-17

### Added

- App icon for the exe, title bar and taskbar.
- About dialog (ⓘ button, top right): version, privacy statement, links and third-party notices.
- Log file in `%LOCALAPPDATA%\ScanDesk\logs` (kept 7 days) to help diagnose problems.
- The window remembers its size and position, and always opens fully on screen.

### Changed

- Settings are saved safely: a crash while saving no longer leaves a broken settings file.

### Security

- Updated the bundled `System.Security.Cryptography.Pkcs` library to 10.0.13
  (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).

## [0.1.0] - 2026-10-12

First release.

### Added

- Scan from eSCL (AirScan / Mopria) network scanners, found automatically or by IP address.
- Flatbed and document feeder; manual two-sided scanning for feeders without duplex.
- Live PDF preview, page thumbnails, reorder by drag and drop, rotate and delete pages.
- Save as PDF (scanner JPEGs embedded without recompression) or as JPEG files.
- Unsaved pages survive a crash or close and are offered on the next start.
