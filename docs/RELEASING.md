# Releasing Coreloom

Releases are built entirely by GitHub Actions from a version tag. Nothing is uploaded by hand.

## One-time setup

1. Create a GitHub repository, for example `<you>/Coreloom`. Public repositories get unlimited Actions minutes
   on hosted runners, and update checks need no token.
2. Push the code:

   ```powershell
   git remote add origin https://github.com/<you>/Coreloom.git
   git push -u origin main
   ```

3. Under *Settings → Actions → General*, make sure Actions may run. The release job only needs the default
   `GITHUB_TOKEN` with `contents: write`, which the workflow requests itself.
4. Watch the **CI** workflow on that first push. It builds and tests on Windows and Linux, publishes the
   self-contained app and runs CoreloomDiag on the runner. Fix anything red before tagging.

## Cutting a release

1. Add a section for the version in `CHANGELOG.md`, for example `## [1.0.0]`, and commit it. The release
   notes are taken from that section, and Coreloom builds its in-app *What's new* summary from them (see
   [Writing release notes](#writing-release-notes-for-the-whats-new-summary) below).
2. Tag and push:

   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```

   A tag with a suffix, such as `v1.1.0-beta.1`, becomes a **pre-release**. Users only see it when they
   enabled *Include pre-release versions*.
3. The **Release** workflow:
   - reads the version from the tag;
   - installs Inno Setup;
   - runs `build/build.ps1 -Package`, which restores, builds, tests, publishes self-contained `win-x64`
     (`Coreloom.exe` + `CoreloomDiag.exe`), builds the installer, zips the portable build and writes
     `SHA256SUMS.txt`;
   - creates the GitHub release with these assets:
     - `Coreloom-<version>-win-x64-Setup.exe`
     - `Coreloom-<version>-win-x64-Portable.zip`
     - `SHA256SUMS.txt`
4. The build embeds `GITHUB_REPOSITORY` as the update source. Copies of earlier versions find the
   new release at their next check (20 seconds after they start, then as often as *Settings → Updates* says;
   daily by default) or right away with *Check for updates*. With *Update automatically* on (the default), they
   download and verify it in the background, show a badge and a notification, and install it the next time
   Coreloom starts, while the PC is idle, or when the user clicks *Install update*. Installed copies update with
   `-Setup.exe`, portable copies with `-Portable.zip`, so every release needs both (plus `SHA256SUMS.txt`). Copies
   of 1.1.0 or earlier that are portable still link to the release page: they predate in-place updates.

If you create the release yourself on GitHub's website (for example to write its text there), the workflow adds
the files to that release and keeps your title and text; it only fills in the text when you left it empty. If a
release ever ends up without its files, open *Actions → Release → Run workflow* and enter the tag: it builds that
tag again and uploads the files.

Keep the asset names unchanged: the updater looks for `*-win-x64-Setup.exe` and `SHA256SUMS.txt`. Never replace
the assets of a published release: copies that already downloaded it would see a different checksum. Publish
a new version instead.

## Writing release notes for the "What's new" summary

Coreloom shows a short summary of each release in *Settings → Updates*, in the update notification and after
updating. It's built from the release notes like this:

- The **first sentence** before any heading becomes the intro, so start with one plain sentence that says what
  the release is about.
- Up to **five bullets** are shown. If there's a `### Highlights` (or `### What's new` / `### Summary`) section,
  its bullets are used. Otherwise the Keep a Changelog sections are used, in this order: `### Security`,
  `### Added` (tagged *New*), `### Changed` (*Improved*), `### Fixed` (*Fixed*), `### Removed`, `### Deprecated`.
  Anything beyond five becomes "…and N more changes in the full release notes."
- Each bullet is cut to its **bold lead** and **first sentence**: `- **Faster charts.** Scrolling now keeps up
  with 240 Hz displays. More details…` shows *Faster charts* with "Scrolling now keeps up with 240 Hz displays."
  Put the most important words first; keep the first sentence under about 140 characters.
- Sub-bullets, code blocks, tables and everything after a `---` line (the download footer the workflow adds) are
  left out.

Example:

```markdown
## [1.1.0] - 2026-11-03

Coreloom now keeps itself up to date.

### Added
- **Automatic updates.** New versions download in the background and install when Coreloom restarts.
- **What's new.** A short summary of every release, right in Settings.

### Fixed
- **Tray icon.** It no longer disappears after Explorer restarts.
```

## Verifying a release

- Download the assets and check them:

  ```powershell
  Get-FileHash .\Coreloom-1.0.0-win-x64-Setup.exe -Algorithm SHA256
  ```

  The hash must match the line in `SHA256SUMS.txt`.
- Install the previous version (and unzip its portable build), then use *Check for updates* in each. *Install
  update* should download, verify, install and relaunch. The full checklist, including the automatic paths, is in
  [TESTING-ON-YOUR-PC.md](TESTING-ON-YOUR-PC.md#6-automatic-updates-after-publishing-two-releases).

## Code signing (recommended before wide distribution)

Releases aren't code-signed yet, so Windows SmartScreen will warn on first run until the download builds
reputation. To sign:

1. Get a code-signing certificate (or use Azure Trusted Signing).
2. Add the certificate as repository secrets.
3. Add a `signtool sign` step to `release.yml` after the build step and before the checksums are written.
   Sign `Coreloom.exe` and `CoreloomDiag.exe` in `artifacts/publish/win-x64`, and the setup executable (Inno
   Setup's `SignTool` directive can do the last one).
4. Regenerate `SHA256SUMS.txt` after signing. The simplest way is to sign inside `build.ps1` before its
   checksum step.

## Local packaging

```powershell
./build/build.ps1 -Version 1.0.0-local -Repository <you>/Coreloom -Package
```

The output goes to `artifacts/release/`. Without Inno Setup, only the portable zip and checksums are
produced.
