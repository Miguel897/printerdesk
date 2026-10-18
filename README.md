# PrinterDesk

Windows desktop app for your network printer: scan from scanners that support **eSCL** (AirScan /
Mopria) and print over **IPP** (IPP Everywhere / AirPrint / Mopria), on all-in-ones such as the
HP ENVY 6400 series. No HP software, scanner driver or printer driver needed.

- Flatbed and document feeder (all pages of a feeder batch)
- Live PDF preview, page thumbnails, reorder (drag & drop), rotate, delete
- Manual two-sided scanning for feeders without duplex
- Save as PDF (JPEGs embedded without recompression) or JPEG files
- **Open** PDF, JPEG and PNG files (Ctrl+O or drag & drop) and mix their pages with scans
- **Print** the document (Ctrl+P): color or black & white, two-sided, orientation, paper size, quality,
  copies and page range; printer status and ink levels
- Unsaved pages survive a crash or close and are offered on the next start

Formerly **ScanDesk**: settings and unsaved pages are moved over automatically on first start.

See [docs/PRD.md](docs/PRD.md) (scanning) and [docs/PRD-print.md](docs/PRD-print.md) (printing) for
requirements and design.

## Download

Get `PrinterDesk.exe` from the [latest release](https://github.com/Miguel897/printerdesk/releases/latest).
It is a single self-contained file: no installation and no .NET needed. Windows SmartScreen may warn
because the exe is not code-signed: click **More info → Run anyway**.

## Requirements

- Windows 10/11 x64
- .NET 10 SDK to build (not needed to run the published exe)
- Microsoft Edge WebView2 Runtime for the PDF preview (preinstalled on Windows 10/11)

## Build, test, run

```powershell
dotnet build PrinterDesk.slnx
dotnet test PrinterDesk.slnx
dotnet run --project src/PrinterDesk.App
```

## Publish a single exe

```powershell
dotnet publish src/PrinterDesk.App -c Release -r win-x64 -o publish
```

Produces `publish/PrinterDesk.exe` (~64 MB, self-contained). Copy it to any Windows PC on the same network as the scanner.

## Versions and releases

The version comes from the latest `vX.Y.Z` git tag ([MinVer](https://github.com/adamralph/minver));
there is no version number in the project files. Builds between tags are pre-releases such as
`0.3.1-alpha.0.3`.

Every user-visible change adds a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), in the
same PR. To release:

1. In a PR, rename `## [Unreleased]` to `## [0.3.0] - YYYY-MM-DD` and add a new empty `## [Unreleased]`
   above it. Merge it.
2. Tag the merge commit on `main` and push the tag:

   ```powershell
   git switch main; git pull
   git tag -a v0.3.0 -m "PrinterDesk 0.3.0"
   git push origin v0.3.0
   ```

The [Release workflow](.github/workflows/release.yml) tests, builds and attaches `PrinterDesk.exe`, using
that version's CHANGELOG section as the release notes. It fails if the section is missing or the exe
version does not match the tag.

## First run on a new PC

- Windows Firewall asks whether PrinterDesk may use the network: allow it on **private** networks so the
  scanner can be found automatically (mDNS). Without it, type the scanner's IP address and click **Connect**.
- Scans are saved to `Documents\Scans` by default; the last used folder is remembered.

## Privacy

PrinterDesk has no telemetry and needs no account. It only talks to the scanner and printer on your
local network (mDNS discovery, eSCL and IPP over HTTP) and makes no internet connections of its own.
Your documents never leave your PC, except to the printer when you print them. The PDF preview uses
the Microsoft Edge WebView2 Runtime that comes with Windows, which Windows keeps up to date. The only files PrinterDesk writes besides your scans are listed below.

## Where data is stored

| What | Where |
|---|---|
| Settings | `%APPDATA%\PrinterDesk\settings.json` |
| Pages of the current document (scans and copies of opened files) | `%LOCALAPPDATA%\PrinterDesk\Session` |
| PDF preview browser profile | `%LOCALAPPDATA%\PrinterDesk\WebView2` |
| Log (7 days) | `%LOCALAPPDATA%\PrinterDesk\logs` |
| Print job being sent (deleted when done) | `%TEMP%\PrinterDesk` |

## Project layout

```
src/PrinterDesk.Core          eSCL and IPP clients, scan planning, discovery, document session, file import,
                              PDFium wrapper, PDF export, print layout and PWG raster, settings
src/PrinterDesk.App           WPF UI (MVVM, WebView2 preview)
tests/PrinterDesk.Core.Tests  xUnit tests (fixtures captured from an HP ENVY 6400)
```

## License

[MIT](LICENSE)

PrinterDesk.exe bundles PDFium, PDFsharp, Zeroconf, System.Reactive, CommunityToolkit.Mvvm, the WebView2
SDK and the .NET runtime. Their licenses are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), also shown in
the app under **ⓘ › Third-party notices**.
