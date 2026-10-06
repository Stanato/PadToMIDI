# Preparing a GitHub preview release

The source and packages are prepared for a **preview** release. This is not a signed stable
release. The application targets Windows 11 25H2+ x64 and uses preview Windows MIDI APIs.

## Upload the repository

The prepared source archive contains the solution, code, lockfiles, original assets,
documentation, notices, VS Code configuration, and `.github` workflow/templates.
Extract it before uploading source files. Do not commit `.tools`, `artifacts`, `bin`,
`obj`, test results, downloaded NuGet packages, signing keys, or personal profiles.

With Git installed, open the extracted `PadToMIDI` folder and run:

```powershell
git init -b main
git add .
git status --short
git commit -m "Initial PadToMIDI preview"
git remote add origin <YOUR_GITHUB_REPOSITORY_URL>
git push -u origin main
```

Create the empty repository on GitHub first. Replace the URL placeholder with that
repository's HTTPS or SSH URL. If it already has commits, clone it and copy the source
into that checkout instead of replacing its history. Review the staged file list before
committing. Keep the ignored files ignored; do not use `git add -f` for the SDK package.

The Build and test workflow runs on pushes to main/master and pull requests. It downloads
the checksum-pinned Microsoft SDK, performs locked restore, builds, runs all three unit
test suites, and compiles the diagnostic tools. It does not require a gamepad, DAW, MIDI
service, or desktop session and does not publish anything. Its first remote run still
needs to pass after upload; local validation does not claim a GitHub-hosted run.

## Attach the downloadable release

Use **Releases > Draft a new release** after uploading the source. Set the tag to
`v0.1.0-preview.3`, use the release notes from `release/RELEASE-NOTES.md`, and select
**Set as a pre-release**. Attach these files from the prepared GitHub-release folder:

- `PadToMIDI-0.1.0-preview.3-win-x64-setup.exe`
- `PadToMIDI-0.1.0-preview.3-win-x64.zip`
- `PadToMIDI-0.1.0-preview.3-source.zip`
- `SHA256SUMS.txt`

Upload installers and ZIPs as release assets, not repository source files. Do not upload
`build.json`, work folders, test fixtures, or the downloaded Microsoft SDK package.
The SHA-256 file covers all three archives/executables in the prepared upload folder.

## Rebuild the files

Install .NET SDK 10.0.401 x64 and NSIS 3.11. See [packaging](MILESTONE-8.md) for details.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Release.ps1 -NsisCompiler 'C:\Program Files (x86)\NSIS\makensis.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Prepare-GitHubRelease.ps1
```

The last command checks package hashes, extracts and verifies the portable payload,
creates a source archive from an explicit file list, and prepares the upload folder.
Build/test the source archive before uploading if source or build configuration changed.

## License and preview maintenance

PadToMIDI is licensed under [MIT](../LICENSE.txt). Dependency licenses and Microsoft's
preview redistribution conditions remain independently applicable; do not remove notices.

Microsoft updated its [go-live agreement](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10)
on October 5, 2026. Distributing the preview API entails the current maintenance and update
conditions, including preparing for API/metadata changes, keeping preview versions current,
and replacing app-local binaries after the in-box API becomes available. The owner must
follow these conditions for binary distribution. See [dependencies](DEPENDENCIES.md).

Stable-release readiness also requires physical controller/DAW testing, clean-machine
prerequisite checks, normal Start Menu/Installed Apps verification, and a signing plan.
