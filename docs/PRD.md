# PrinterDesk — Product Requirements Document (v1: scanning)

| | |
|---|---|
| **Status** | v1 implemented (see §12) |
| **Date** | 2026-10-12 |
| **Owner** | miguel |
| **Platform** | Windows 10/11 (x64), .NET 10 LTS, WPF |

## 1. Summary

PrinterDesk is a small Windows desktop app for scanning documents from network scanners
that support **eSCL** (AirScan / Mopria). The first target device is an
**HP ENVY 6432e** (flatbed + automatic document feeder). The user scans pages,
checks them in a built-in preview, fixes order or removes bad pages, and saves a PDF.

It installs on any of the owner's Windows PCs **without any HP software or scanner
driver**. The PC only needs to be on the same network as the printer.

## 2. Background and findings

Tested on 2026-10-12 against the ENVY 6432e (`192.168.1.130`, reports as "ENVY 6400 series"):

| Approach | Flatbed | Feeder (multi-page) | Output | Driver needed |
|---|---|---|---|---|
| WIA Automation (`wiaaut.dll`) | OK | **Only page 1 returned**; remaining pages are scanned but lost | BMP only (~26 MB/page @300 dpi) | Yes (Windows WSD driver) |
| **eSCL 2.63** (HTTP) | OK | **All pages returned** (`NextDocument` until 404) | JPEG or PDF | **No** |

Measured eSCL feeder speed: ~20–26 s/page at 300 dpi color, 600–850 KB/page JPEG.
The device has **no duplex** (one side only).

**Decision:** the scan layer is built on eSCL. WIA is out of scope (see §9).

## 3. Goals

1. Scan from the **flatbed** or the **feeder** with a single click.
2. **Preview** the result as a PDF and check every page before saving.
3. Simple page editing: **reorder, delete, rotate, rescan/append** pages.
4. Save a **multi-page PDF** (default) or JPEG files.
5. Run on other Windows PCs with **no driver installation**.

### Non-goals (v1)

- Printing, copying, fax, printer management.
- OCR / searchable PDF (candidate for v2).
- macOS / Linux.
- Scanners without eSCL (USB-only, TWAIN-only).
- Cloud upload, e-mail integration.

## 4. Users and scenarios

Single user (the owner), occasional home/office scanning.

| # | Scenario |
|---|---|
| S1 | Scan a stack of 1–35 loose one-sided pages through the feeder into one PDF. |
| S2 | Scan receipts / a book page / an ID card from the flatbed. |
| S3 | Scan several flatbed pages one after another into the same PDF. |
| S4 | Mixed: feeder batch, then add a page from the flatbed into the same document. |
| S5 | Manual duplex: scan fronts, flip the stack, scan backs; the app interleaves them. |
| S6 | Notice a skewed or blank page in the preview, delete it and rescan it. |

## 5. Functional requirements

Priority: **P0** = v1 must have, **P1** = v1 should have, **P2** = later.

### 5.1 Scanner connection

| ID | Requirement | Pri |
|---|---|---|
| F1 | Auto-discover eSCL scanners on the LAN via mDNS (`_uscan._tcp`, `_uscans._tcp`). | P0 |
| F2 | Manual entry of IP / hostname as fallback; remember last used scanner. | P0 |
| F3 | Read `ScannerCapabilities` and only offer options the device supports (sources, resolutions, color modes, formats). | P0 |
| F4 | Show live status from `ScannerStatus`: idle / busy / feeder loaded / feeder empty. | P0 |

### 5.2 Scanning

| ID | Requirement | Pri |
|---|---|---|
| F5 | Source: **Feeder** / **Flatbed** / **Auto** (feeder if paper is loaded, else flatbed). | P0 |
| F6 | Resolution: device list; **default 150 dpi**. | P0 |
| F7 | Color mode: Color / Grayscale / Black & white; default Color. | P0 |
| F8 | Paper size: A4 (default) / Letter / Full bed; applies to flatbed scan region. | P1 |
| F9 | Feeder scan retrieves **every** page (`NextDocument` loop, retry on HTTP 503, stop on 404). | P0 |
| F10 | Progress: "Scanning page N…", elapsed time, pages received so far appear in the preview live. | P0 |
| F11 | Cancel a running scan (`DELETE` the eSCL job); pages already received are kept. | P0 |
| F12 | Scan transfer format: JPEG per page (see §7 open question Q1). | P0 |
| F13 | Manual duplex helper (scan fronts → prompt to flip → scan backs → interleave reversed backs). | P1 |

### 5.3 Document and preview

| ID | Requirement | Pri |
|---|---|---|
| F14 | A document holds an ordered list of pages; new scans **append** to the current document. | P0 |
| F15 | **Thumbnail strip** of all pages with page numbers. | P0 |
| F16 | **PDF preview pane** showing the document as it will be saved (zoom, scroll, page navigation). | P0 |
| F17 | Delete selected page(s). | P0 |
| F18 | Reorder pages (drag & drop and/or move up/down buttons). | P0 |
| F19 | Rotate page 90° / 180°. | P1 |
| F20 | "New document" clears the current pages (with confirmation if unsaved). | P0 |
| F21 | Auto-crop to content for small items (receipts) on flatbed. | P2 |
| F22 | Blank-page detection and removal. | P2 |

### 5.4 Saving

| ID | Requirement | Pri |
|---|---|---|
| F23 | Save as **PDF** (one multi-page file) — default. | P0 |
| F24 | Save as JPEG (one file per page). | P1 |
| F25 | Default folder `Documents\Scans`, file name `Scan_yyyy-MM-dd_HHmmss.pdf`, editable before saving. | P0 |
| F26 | After save: "Open file" / "Open folder" actions. | P1 |
| F27 | Unsaved pages are kept in a temp working folder so a crash does not lose a long feeder batch. | P1 |

### 5.5 Settings

| ID | Requirement | Pri |
|---|---|---|
| F28 | Persist: scanner address, default source, dpi, color, paper size, output folder. Stored in `%APPDATA%\PrinterDesk\settings.json`. | P0 |

## 6. UI

Single main window, WPF.

```
┌───────────────────────────────────────────────────────────────────────────┐
│ Scanner: [HP ENVY 6400 (192.168.1.130) ▼] (↻)   ● Idle · Feeder loaded    │
├──────────────────┬───────────────────┬────────────────────────────────────┤
│ SCAN             │ PAGES             │ PREVIEW                            │
│ Source  [Feeder▼]│ ┌─────┐           │ ┌────────────────────────────────┐ │
│ DPI     [150   ▼]│ │  1  │           │ │                                │ │
│ Color   [Color ▼]│ └─────┘           │ │      PDF viewer (WebView2)     │ │
│ Paper   [A4    ▼]│ ┌─────┐           │ │   zoom · page nav · scroll     │ │
│                  │ │  2  │ ◄selected │ │                                │ │
│ [  ▶ Scan      ] │ └─────┘           │ │                                │ │
│ [  ■ Cancel    ] │ ┌─────┐           │ │                                │ │
│                  │ │  3  │           │ │                                │ │
│ Scanning page 2… │ └─────┘           │ └────────────────────────────────┘ │
│ ▓▓▓▓▓▓░░░  0:41  │ [↑][↓][⟳][🗑]     │                                    │
├──────────────────┴───────────────────┴────────────────────────────────────┤
│ [New]                                   3 pages   [Save PDF ▼]            │
└───────────────────────────────────────────────────────────────────────────┘
```

- **PDF preview** uses **WebView2** (the Edge PDF viewer, already installed on Windows 10/11),
  so zoom, page navigation and printing come for free. It reloads after each page change.
- **Thumbnails** are rendered from the page JPEGs (fast, no PDF rendering library needed).
- Light/dark follows the Windows theme (P2).

## 7. Technical design

### 7.1 Stack

| Concern | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 (LTS, supported to Nov 2028) | .NET 8 support ends Nov 2026 |
| UI | WPF + MVVM (`CommunityToolkit.Mvvm`) | Mature, native Windows desktop |
| PDF preview | `Microsoft.Web.WebView2` | Uses runtime preinstalled on Win 10/11 |
| PDF writing | `PDFsharp` 6.x (MIT) | JPEG pages → PDF without re-encoding |
| mDNS discovery | `Zeroconf` (MIT) | Fallback: manual IP |
| eSCL client | Own code over `HttpClient` + `System.Xml.Linq` | ~300 lines |
| Tests | xUnit + fake `HttpMessageHandler` | Core library only |

### 7.2 Solution layout

```
PrinterDesk.sln
├─ src/PrinterDesk.Core        class library: eSCL client, discovery, document model, PDF export
├─ src/PrinterDesk.App         WPF app: views, view models, settings
└─ tests/PrinterDesk.Core.Tests
```

### 7.3 Page model

The **source of truth is one JPEG per page** (as received from the scanner) plus
per-page rotation. The PDF is generated from those JPEGs:

- on every change → temp preview PDF shown in WebView2;
- on save → final PDF.

Embedding the JPEGs directly in the PDF avoids recompression, so quality and size are
the same as the scanner's output. This keeps reorder/delete/rotate trivial.

### 7.4 eSCL flow

```
GET  /eSCL/ScannerCapabilities          once per connection
GET  /eSCL/ScannerStatus                polled every ~3 s while idle
POST /eSCL/ScanJobs  (ScanSettings XML) → 201 + Location: /eSCL/ScanJobs/{id}
loop GET {job}/NextDocument
       200 → page bytes → append page
       503 → wait 1 s, retry
       404 → job finished
DELETE {job}                            on cancel
```

All network calls are async; the UI never blocks during the ~20–30 s per page.

### 7.5 Distribution

- `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` → one `PrinterDesk.exe`
  (~70–100 MB, no .NET install needed) that can be copied to other PCs.
- Optional later: MSIX or Inno Setup installer with Start-menu shortcut.
- First run on a new PC: Windows Firewall may ask to allow mDNS; manual IP always works.

## 8. Non-functional requirements

| ID | Requirement |
|---|---|
| N1 | UI stays responsive during scans (all I/O async, cancellable). |
| N2 | No admin rights needed to run. |
| N3 | Works offline (LAN only); no telemetry, no internet calls. |
| N4 | Scanned data never leaves the PC except to the chosen save folder. |
| N5 | Clear error messages: scanner not found, busy (used by another PC), feeder empty, paper jam. |
| N6 | Startup < 2 s on the owner's laptop. |

## 9. Out of scope / alternatives considered

- **WIA**: returns only the first feeder page from this printer's WSD driver; BMP only. Rejected.
- **TWAIN**: requires the full HP software package on every PC. Rejected.
- **WinUI 3**: more modern look but heavier packaging and tooling; WPF is simpler for a single-window tool.

## 10. Open questions

| # | Question | Proposed answer |
|---|---|---|
| Q1 | Use the printer's native **PDF** output instead of JPEG? | **Tested:** works, but the printer returns **one** PDF only after the whole batch (3 pages, 713 KB @150 dpi, 71.7 s), so no live per-page preview and no early cancel with partial pages. **Proposal:** JPEG per page as the default pipeline; native PDF not used in v1. |
| Q2 | Default color mode for documents: Color or Grayscale? | Color (owner can change; setting persists). |
| Q3 | Need OCR (searchable PDFs)? | v2 — Windows built-in OCR (`Windows.Media.Ocr`) or Tesseract. |
| Q4 | Installer or just a copied `.exe`? | Single `.exe` for v1. |
| Q5 | App name. | "ScanDesk" (working name); renamed to "PrinterDesk" in 2026-10 (see [PRD-print.md](PRD-print.md) Q1). |

## 11. Milestones

| # | Milestone | Content |
|---|---|---|
| M1 | Core | eSCL client, capabilities/status, feeder + flatbed scan, PDF export, unit tests, console smoke test |
| M2 | UI MVP | Main window, scan panel, thumbnails, WebView2 preview, save PDF |
| M3 | Editing | Reorder, delete, rotate, cancel, settings persistence, discovery |
| M4 | Polish & ship | Manual duplex, error handling, single-file publish, test on a second PC |

## 12. Implementation status (2026-10-12)

| Area | Requirements | Status |
|---|---|---|
| Connection | F1–F4 | Done. Manual IP verified on device; mDNS discovery implemented, needs the firewall prompt to be allowed. |
| Scanning | F5–F7, F9–F12 | Done. Feeder (3 pages, Auto source, 150 dpi) verified end to end in the app. |
| Paper size | F8 | Done (flatbed scan region). Not yet verified on device. |
| Manual duplex | F13 | Done, unit-tested interleaving. Not yet verified on device. |
| Document / preview | F14–F20 | Done. Rotate, reorder, preview verified in the app. |
| Auto-crop, blank-page removal | F21–F22 | Not started (P2). |
| Saving | F23–F27 | Done. PDF save verified via the app; JPEG export and crash-restore implemented, not yet verified manually. |
| Settings | F28 | Done. |
| Distribution | §7.5 | `dotnet publish` → single 64 MB `PrinterDesk.exe`, verified to start and reconnect. |
