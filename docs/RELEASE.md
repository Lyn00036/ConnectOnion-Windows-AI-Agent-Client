# Releasing

Pushing a `v*` tag is the entire release process. Everything below explains what that does, what
it refuses to do, and the two things a person still has to check by hand.

| | |
|---|---|
| Workflow | `.github/workflows/release.yml` |
| Scripts | `scripts/Set-PackageIdentity.ps1`, `scripts/Test-PackageContents.ps1`, `scripts/Test-ReleaseUpgrade.ps1` |
| Headless upgrade coverage | `tests/ConnectOnion.IntegrationTests/Database/ReleaseUpgradeTests.cs` |
| Architectures | **x64 only** for now — see "Adding ARM64" |

```powershell
git tag v1.2.3
git push origin v1.2.3
```

To rehearse without publishing, run the workflow manually with a version — every gate runs, no
release is created.

## Package identity — decide once, never again

```
Name       ConnectOnion.Desktop
Publisher  <the signing certificate's subject>       (secret: SIGNING_PUBLISHER_DN)
Version    <from the tag>                            (Major.Minor.Patch.0)
```

**`Name` + `Publisher` together are the upgrade identity, and neither can ever change.** Windows
keys everything off that pair. A package whose pair differs is a different application: it
installs *beside* the old one instead of upgrading it, and it gets a different
`%LOCALAPPDATA%\Packages\<Name>_<publisher hash>\` folder. The previous install's conversations
and its DPAPI-protected agent identity are still on disk, and the new install cannot see any of
it. Nothing warns anyone. There is no rename path afterwards — the only remedy is to publish
another package under the *original* identity, which means the wrong one is loose forever.

So: changing the signing certificate is fine, but **the new certificate's subject must be
identical to the old one**. That constraint is the real reason to think about the DN before the
first release rather than after.

The values that shipped before this pipeline existed (`CN=ROG`, a GUID name, `1.0.0.0`) were
development leftovers. They were never released, so replacing them cost nothing — but a local
Visual Studio deploy from before the change is now a *different* package, and its data will look
missing. Uninstall the old one and start fresh; nothing of value is in it.

The committed manifest carries `Version="0.0.0.0"` deliberately: it is not a valid release
version and the stamping script refuses it, so a package that reaches anyone still carrying it
proves the pipeline was bypassed. The workflow re-reads the manifest back out of the *signed
package* and fails if the version, name or publisher is not what it expects — checking the
working tree would not catch a stamping step that silently no-opped.

## Versioning

`Directory.Build.props` holds `VersionPrefix`, the single source. It flows into
`AssemblyVersion`/`FileVersion`/`InformationalVersion`, which is what `AppVersionService` reports
when the app runs **unpackaged**. The packaged build additionally stamps the manifest, which is
what `AppVersionService` reports when it runs **packaged**. Both come from the tag in a release
build, so the two can never disagree.

MSIX versions are four integers. The fourth is left at `0` because the Store reserves it and
rejects packages that set it. There is nowhere to put a prerelease suffix, so `v1.2.3-rc.1` and
`v1.2.3-rc.2` are *the same version* to Windows — one will not upgrade the other. Prerelease tags
build and publish as GitHub prereleases for internal testing; do not hand them to users expecting
an upgrade path.

## Signing

Three repository secrets:

| Secret | What |
|---|---|
| `SIGNING_CERTIFICATE_BASE64` | The `.pfx`, base64-encoded |
| `SIGNING_CERTIFICATE_PASSWORD` | Its password |
| `SIGNING_PUBLISHER_DN` | The certificate subject, e.g. `CN=ConnectOnion, O=…, C=AU` |

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('signing.pfx')) | Set-Clipboard
# Read the subject exactly as the certificate states it — this is the value that must match:
(Get-PfxCertificate -FilePath signing.pfx).Subject
```

The workflow writes the `.pfx` to `RUNNER_TEMP`, signs, and deletes it in a `finally` so the key
does not survive a failed step. Signing uses SHA-256 with an RFC3161 timestamp
(`/tr` + `/td SHA256`). **The timestamp is not optional**: without a countersignature every
installer ever published stops installing the day the certificate expires. With one, signatures
keep verifying for the life of the timestamp authority's own certificate.

Verification uses `signtool verify /pa` — the Authenticode policy a client actually applies.
Verifying under a weaker policy would pass in CI and fail on a user's machine.

### Rotating the certificate

1. Obtain the new certificate **with the same subject DN as the current one**. A different DN is
   a different application (see above) — if the DN must change, that is a new product identity
   and a migration problem, not a rotation.
2. Update `SIGNING_CERTIFICATE_BASE64` and `SIGNING_CERTIFICATE_PASSWORD`. Leave
   `SIGNING_PUBLISHER_DN` alone if the subject is unchanged; if it changed, stop and read step 1
   again.
3. Cut a patch release and run the full validation matrix. Previously published installers keep
   working — that is what the timestamp bought.

Neither key nor password is ever written to a log, and no marketplace action touches the signed
artifact: publishing uses the runner's preinstalled `gh`.

## What the release contains

| Asset | |
|---|---|
| `ConnectOnion.Desktop-v<version>-x64.msix` | The signed, self-contained installer |
| `ConnectOnion.Desktop-v<version>-x64-portable.zip` | Self-contained unpackaged build; extract the whole archive and run `ConnectOnion.WinUIClient.exe` |
| `ConnectOnion.Desktop-v<version>-x64.appxsym` | Symbols, **separately** — never inside the package |
| `SHA256SUMS.txt` | Checksums for every published asset |

Both release forms are self-contained. The MSIX embeds the .NET and Windows App SDK runtimes and
must not declare an external `PackageDependency`; this makes the single signed file installable
on a clean supported Windows machine. The portable ZIP includes the .NET runtime and Windows App
SDK bootstrapper, so it does not need
a separately installed .NET runtime. It is deliberately a multi-file archive, not a single EXE:
extract it before running and keep its files together. It has no package identity, does not
register an installer entry, and does not participate in MSIX upgrades. Its unpackaged data stays
under `%AppData%\ConnectOnion`, while the installed MSIX uses package LocalState.

`scripts/Test-PackageContents.ps1` audits both the MSIX and portable ZIP and fails the build if
either contains PDBs, xunit or VSTest assemblies, any
`ConnectOnion.*Tests`/`TrimSmoke`/`Conformance`/`LiveTest` assembly, FlaUI or ArchUnit, log files,
`.db` files, coverage output, or source. It also opens the MSIX manifest and rejects every
external `PackageDependency`, preventing a framework-dependent installer from being published as
a standalone asset. The payload is assembled from a publish directory rather than an allow-list,
so the way a test assembly reaches a user is not a decision anyone makes — it is one nobody
notices.

Symbols come from MSBuild's `.appxsym`, which needs `mspdbcmf.exe`. If the runner image lacks it
the release ships without symbols rather than failing; the log warns.

## Package size

Baselines (unsigned x64, untrimmed, ReadyToRun):

| Artifact | Compressed | Unpacked | Budget |
|---|---:|---:|---:|
| Self-contained MSIX, ratified 2026-07-27 | 121.43 MB | 310.07 MB | +10% |
| Portable ZIP, ratified 2026-07-25 | 96.65 MB | 246.84 MB | +10% |

The MSIX increase is intentional: it now embeds the Windows App SDK runtime instead of silently
depending on a separately installed framework package.

The gate catches accidental payload — a second copy of the runtime, an unpruned resource set —
not a few megabytes of real features. A breach is a prompt to look at what grew, and if the
growth is legitimate, to re-ratify the numbers here deliberately.

**Do not enable trimming to get back under budget.** Trimming is gated separately and for
unrelated reasons by [TRIMMING.md](./TRIMMING.md); the release build passes
`PublishTrimmed=false` explicitly, and package size is not an argument that bears on that gate.

## Validation

### Automated, every release

The workflow runs the full CI suite — protocol, headless client, database integration tests, the
conformance gate and the trimmed serialization smoke harness — **before** it builds a package,
and re-runs it here rather than trusting the commit's own CI run, because a tag can point at a
commit CI never saw.

`ReleaseUpgradeTests` covers the data half of the upgrade criterion headlessly, against real
SQLite and real DPAPI:

- an upgrade preserves the address, the private seed **and** the recovery phrase, and the stored
  ciphertext is untouched;
- upgrading from a pre-schema-v5 release keeps the identity, leaves the phrase absent (nothing
  can derive one for an existing seed, and regenerating would change the address every agent
  authorized), and falls back to offering the raw seed;
- the v4 → v5 migration leaves a *real* DPAPI blob byte-identical and still decryptable;
- conversations and messages survive.

### Manual, on a clean profile

`scripts/Test-ReleaseUpgrade.ps1` drives the rest and records the evidence to
`artifacts/release-validation/`. It refuses to run if the package is already installed, because
on a machine that has ever had the app deployed "the upgrade preserved my data" proves nothing.

```powershell
pwsh scripts/Test-ReleaseUpgrade.ps1 -OldPackage .\v1.0.0.msix -NewPackage .\v1.1.0.msix
```

It automates what it can — package registration, that the package family name is unchanged across
the upgrade, and that `connectonion.db` is byte-identical after install and before first launch —
and prompts for what only a person can see: first launch with no identity-reset toast, the same
`0x` address in Settings → Identity, conversations still present, toast activation opening the
right conversation, tray minimise and restore, and a clean uninstall.

Unverified checkpoints exit `2`, not `0`. An incomplete run is not a pass.

## Adding ARM64

Deliberately out of scope for the first release: it doubles build time and there is no ARM64
hardware to validate the install on, and an unvalidated architecture in a release is worse than
an absent one. When it is wanted:

1. Add `ARM64` / `win-arm64` to a build matrix in `release.yml`.
2. Produce a `.msixbundle` (`makeappx bundle`) instead of publishing the `.msix` directly, and
   sign the bundle.
3. Extend `Test-PackageContents.ps1` to walk each package inside the bundle.
4. Validate the install on real ARM64 hardware before publishing.

## If a release goes wrong

A failed gate leaves the tag pushed and **no release published** — recoverable in a way a
half-published release with a broken installer is not. Artifacts upload even on failure, so a bad
run is diagnosable without re-running the build.

```powershell
git push --delete origin v1.2.3
git tag -d v1.2.3
# fix, then re-tag
```

If a broken release is already published, delete the release (and its assets) but **do not reuse
the version number** — anyone who downloaded it has a package Windows now considers that version.
Ship the fix as the next patch.
