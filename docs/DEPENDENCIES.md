# Dependencies and licenses

Milestones 1–8 use pinned NuGet packages in `Directory.Packages.props` and resolved graphs in each
`packages.lock.json`. [dependency-inventory.csv](dependency-inventory.csv) lists all restored
direct/transitive NuGet packages, versions, license metadata, and upstream URLs. It was generated
from the restored package manifests. No vendor branding or copied controller artwork is included.
Milestone 2 adds no NuGet/native dependencies.
Milestone 3 adds the Windows MIDI SDK and C#/WinRT projection tooling.

| Dependency | Version | License | Purpose |
| --- | --- | --- | --- |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.3 | MIT | Desktop UI and theme |
| ppy.SDL3-CS | 2026.1002.1 | MIT | C# SDL3 binding and bundled platform native binaries |
| SDL3 native library | 3.5.0 development snapshot, bundled with pinned binding | zlib | Gamepad discovery and input |
| xunit.v3 | 4.0.1 | Apache-2.0 | Core unit tests |
| xunit.runner.visualstudio | 4.0.0 | Apache-2.0 | Visual Studio-compatible test adapter |
| Microsoft.NET.Test.Sdk | 18.10.1 | MIT | Test integration |
| Windows.Devices.Midi2 | 0.99.88-preview.10 | MIT package metadata; additional preview redistribution terms | Windows MIDI Services WinRT API and native DLL/PRI |
| Microsoft.Windows.CsWinRT | 2.2.0 | MIT | Generates SDK C# projection; generated interop requires unsafe compilation |
| Microsoft.Windows.SDK.NET.Ref | 10.0.26100.57 | [Windows SDK license](https://aka.ms/WinSDKLicenseURL), as specified by package metadata | Windows metadata/reference projections supplied by the Windows target framework |
| .NET / System.Text.Json | 10.0.12 packaged runtime | MIT | Runtime and profile JSON serialization |

The official MIDI SDK is downloaded unmodified from Microsoft's GitHub release because it is not
published on nuget.org. Provenance and SHA-256 are recorded in [vendor notes](../vendor/nuget/README.md).
Run `tools/Restore-MidiSdk.ps1` before the first restore. The downloaded package is ignored by
Git and omitted from source archives; it remains a local NuGet feed for reproducible builds.
The [Preview 10 release](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10) permits
app-local preview API distribution subject to its go-live conditions, updated October 5, 2026:
supported Windows 11 25H2+ only, preview disclosure, continued updates, readiness for metadata/API
changes before rollout, removal of bundled binaries after in-box availability, and notifying users
about the subsequent rebuilt version. See the linked agreement for the full conditions.
The former 90-day clause is in the superseded agreement, not the current one.
Tools and transport installers are not redistributable under those terms and are not
included here. Publishing the Windows backend as a reusable MIDI library requires separate review
with Microsoft. Milestone 8 produces a preview package with these terms disclosed; review migration/update obligations before public distribution.

The license folder also includes Microsoft's MIDI repository MIT license and C#/WinRT package license.
The inventory includes packages downloaded implicitly for Windows framework references.
Regenerate the complete dependency inventory after restore with:
`powershell -NoProfile -ExecutionPolicy Bypass -File tools/Update-DependencyInventory.ps1`.

The pinned ppy package's Windows DLL reports version 3.5.0, an SDL development snapshot.
An inspected earlier ppy package (2026.429.0) bundles the same development version, so merely
downgrading the binding does not select a stable SDL branch. Milestone 1 retains the pinned,
tested binding/native pair.
The native virtual-controller and application startup checks passed with this exact binary.
Before production packaging, review the binding's supported stable-native options or a vetted
stable SDL build; do not treat this milestone as a production dependency stability guarantee.

Avalonia's principal rendering dependencies include SkiaSharp (MIT wrapper, native Skia notices),
HarfBuzzSharp (MIT wrapper, native HarfBuzz notices), MicroCom.Runtime (MIT), and ANGLE
(BSD-style bundled license). Test dependencies include xUnit components/analyzers (Apache-2.0)
and Microsoft Testing Platform/TestPlatform components (MIT). Other platform assets may be restored
by Avalonia.Desktop but are not all shipped in a Windows-x64 build.

The [licenses folder](licenses/) preserves the ANGLE license and the Windows native SkiaSharp/
HarfBuzzSharp licenses and third-party notices supplied with the restored packages. Native component
notices include their own dependency terms; the NuGet wrapper license alone is not a complete
redistribution notice. Keep the relevant upstream license/copyright notices when assembling the
Milestone 8 installer, including the binding's MIT license and SDL's zlib license. The preview package preserves these notices. NSIS 3.11 is build-only; the installer uses permissively licensed zlib compression. The project's own terms are in [LICENSE.txt](../LICENSE.txt); they do not replace third-party terms.

Sources: [Avalonia](https://github.com/AvaloniaUI/Avalonia),
[ppy.SDL3-CS](https://github.com/ppy/SDL3-CS),
[SDL license](https://github.com/libsdl-org/SDL/blob/main/LICENSE.txt),
[xUnit](https://github.com/xunit/xunit),
[Windows MIDI Services](https://github.com/microsoft/MIDI).
