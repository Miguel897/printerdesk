# PrinterDesk

Windows desktop app for your network printer: scan documents from network scanners that support
**eSCL** (AirScan / Mopria), such as the HP ENVY 6400 series. No HP software or scanner driver needed.

- Flatbed and document feeder (all pages of a feeder batch)
- Live PDF preview, page thumbnails, reorder (drag & drop), rotate, delete
- Manual two-sided scanning for feeders without duplex
- Save as PDF (JPEGs embedded without recompression) or JPEG files
- Unsaved pages survive a crash or close and are offered on the next start

Printing (color, two-sided, rotation) is planned: see [docs/PRD-print.md](docs/PRD-print.md).
Formerly **ScanDesk**: settings and unsaved pages are moved over automatically on first start.

See [docs/PRD.md](docs/PRD.md) for requirements and design.

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
`1.0.1-alpha.0.3`.

Every user-visible change adds a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), in the
same PR. To release:

1. In a PR, rename `## [Unreleased]` to `## [1.1.0] - YYYY-MM-DD` and add a new empty `## [Unreleased]`
   above it. Merge it.
2. Tag the merge commit on `main` and push the tag:

   ```powershell
   git switch main; git pull
   git tag -a v1.1.0 -m "PrinterDesk 1.1.0"
   git push origin v1.1.0
   ```

The [Release workflow](.github/workflows/release.yml) tests, builds and attaches `PrinterDesk.exe`, using
that version's CHANGELOG section as the release notes. It fails if the section is missing or the exe
version does not match the tag.

## First run on a new PC

- Windows Firewall asks whether PrinterDesk may use the network: allow it on **private** networks so the
  scanner can be found automatically (mDNS). Without it, type the scanner's IP address and click **Connect**.
- Scans are saved to `Documents\Scans` by default; the last used folder is remembered.

## Privacy

PrinterDesk has no telemetry and needs no account. It only talks to scanners on your local network
(mDNS discovery and eSCL over HTTP) and makes no internet connections of its own. Your scans never
leave your PC. The PDF preview uses the Microsoft Edge WebView2 Runtime that comes with Windows, which
Windows keeps up to date. The only files PrinterDesk writes besides your scans are listed below.

## Where data is stored

| What | Where |
|---|---|
| Settings | `%APPDATA%\PrinterDesk\settings.json` |
| Pages of the current document | `%LOCALAPPDATA%\PrinterDesk\Session` |
| PDF preview browser profile | `%LOCALAPPDATA%\PrinterDesk\WebView2` |
| Log (7 days) | `%LOCALAPPDATA%\PrinterDesk\logs` |

## Project layout

```
src/PrinterDesk.Core          eSCL client, scan planning, discovery, document session, PDF export, settings
src/PrinterDesk.App           WPF UI (MVVM, WebView2 preview)
tests/PrinterDesk.Core.Tests  xUnit tests (fixtures captured from an HP ENVY 6400)
```

## License

[MIT](LICENSE)

PrinterDesk.exe bundles PDFsharp, Zeroconf, System.Reactive, CommunityToolkit.Mvvm, the WebView2 SDK and the
.NET runtime. Their licenses are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), also shown in
the app under **ⓘ › Third-party notices**.
