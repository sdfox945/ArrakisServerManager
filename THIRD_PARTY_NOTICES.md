# Third-party notices and asset provenance

The project's MIT license covers its original contributions. It does not grant rights to Microsoft runtime components, Funcom software, trademarks or other third-party material. Keep the notices below with redistributed Windows packages.

## Bundled runtime

The self-contained v1.3.0 release uses the same runtime dependencies as accepted v1.1.2: Microsoft.NETCore.App.Runtime.win-x64 **10.0.13** and Microsoft.WindowsDesktop.App.Runtime.win-x64 **10.0.13**. These are the two runtime packs named in `ArrakisServerManager.deps.json`. The source projects contain no third-party application NuGet PackageReference entries; the update and custom-configuration systems use framework HTTP/JSON/cryptography/WPF APIs. Framework-pack package metadata points to dotnet/dotnet revision `4099f83977f4d29604a9aeb971fb0a7bc1d5f699`.

| Material | Terms/notices included in the public package |
| --- | --- |
| .NET runtime and Windows Desktop open-source portions | `licenses/DOTNET-MIT.txt`, `licenses/WINDOWSDESKTOP-MIT.txt` |
| Third-party components/resources identified by the .NET runtime pack | Full, unabridged `licenses/DOTNET-THIRD-PARTY-NOTICES.txt`, copied from the exact 10.0.13 pack |
| `coreclr.dll`, `Microsoft.DiaSymReader.Native.amd64.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll` | Microsoft .NET Library terms, `licenses/Microsoft.NET-Library.html` |
| `D3DCompiler_47_cor3.dll` | Windows SDK terms, `licenses/Microsoft.Windows-SDK.md` |

Microsoft documents the Windows-specific exceptions in [License information for .NET on Windows](https://github.com/dotnet/core/blob/main/license-information-windows.md). The [WPF redistribution explanation](https://github.com/dotnet/wpf/blob/main/Documentation/redistributables.md) describes its renamed native dependencies. The MIT package expression alone does not replace those exceptions. Review [BUNDLED-RUNTIME-TERMS.md](BUNDLED-RUNTIME-TERMS.md), the actual included license texts, and Microsoft's distribution requirements before using or redistributing the binaries. These components are provided with the application, not as a standalone Microsoft runtime distribution.

The .NET Library HTML is an unmodified copy of Microsoft's official terms page. The Windows SDK Markdown contains the official Microsoft Learn article body (site metadata removed), retrieved 2026-10-23. Authoritative URLs are included in the bundled terms document. Package-license copies retain their original copyright notices and text. The runtime's third-party notice file remains complete rather than attempting to remove notices for optional or platform-specific portions.

## External tools, not bundled

Windows, PowerShell, Hyper-V, Windows OpenSSH, Steam, Funcom's CLI, Kubernetes tools and the official Dune: Awakening server must already be installed or obtained separately. They remain governed by their own terms. Docker and the pinned BusyBox image are development/test prerequisites only; neither Docker nor BusyBox binaries or images are included in the source repository or application ZIP.

## Project assets and fixtures

- `src/Arrakis.Desktop/Assets/arrakis.ico` is an original programmatically drawn geometric mark. The original 2026-10-22 creation command used System.Drawing to draw a triangle and crossbar on a plain background; it did not import a downloaded image, game asset, logo or font file. The exact icon blob is unchanged across the stable tags.
- `docs/images/*.png` are native WPF screenshots from isolated simulated adapters or mocked HTTP fixtures. No production screenshot, account identity, private IP address or credential is included. v1.1.2 added corrected water-guidance evidence; v1.2.0 adds Demo application-update dialog/download evidence. Simulated future versions are examples, not claims that those versions are publicly released.
- Textual references to Dune: Awakening, Funcom and Windows identify supported integrations. No affiliation, endorsement or trademark license is implied. No proprietary Funcom server program, container, game data, official artwork or font file is included.
- `tests/Arrakis.Tests/Fixtures/completed-database-utility.json` is a sanitized metadata fixture with synthetic names, IDs and timestamps.
- `tests/Arrakis.Tests/Fixtures/UserServerCustomSettings.representative.ini` is a 174-line, credential-screened example derived read-only from the user's existing server configuration during v1.0.2 development. It retains third-party-origin configuration identifiers, ranges, choices and short template-derived functional comments; it is not wholly synthetic or wholly original MIT-licensed prose. The targeted review found no substantial copied expressive material beyond that functional content and grouping and recommends retaining the fixture and history. No affirmative Funcom license, ownership of third-party material or guaranteed legal clearance is asserted. The fixture contains no credential/player/machine data and is excluded from the runnable ZIP. See [the detailed provenance, passage review and license boundary](docs/FIXTURE-PROVENANCE.md).

The v1.1.2 fixture changes only its water-direction comment to explicitly attributed user-reported gameplay guidance; all original setting values remain identical. The MIT notice does not relicense inherited third-party material. Residual questions concerning third-party wording/arrangement and applicable terms remain distinct from the finding that no substantial expressive passage was identified. The app preserves existing server comments rather than automatically rewriting them.

The public-source audit covers all historical versions reachable from the intended branches and stable tags. It does not authorize publication of raw development traces or other ignored files. Actual repository creation, pushes and release uploads require the maintainer's explicit publication approval.
