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
   notes are taken from that section.
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
4. The build embeds `GITHUB_REPOSITORY` as the update source. Installed copies of earlier versions see the
   new release at their next check, at the frequency set in *Settings → Updates*, or right away with
   *Check now*.

Keep the asset names unchanged: the updater looks for `*-win-x64-Setup.exe` and `SHA256SUMS.txt`.

## Verifying a release

- Download the assets and check them:

  ```powershell
  Get-FileHash .\Coreloom-1.0.0-win-x64-Setup.exe -Algorithm SHA256
  ```

  The hash must match the line in `SHA256SUMS.txt`.
- Install the previous version, then use *Check now*. The update should download, verify, install silently
  and relaunch.

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
