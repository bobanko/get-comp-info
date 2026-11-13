# CHAGEAR Toolbox

Small Windows utilities for IT technicians who prepare office computers and hand them out to staff,
plus two standalone programs for everyday users. The windowed programs are plain C# / WinForms built with the
compiler that ships with Windows (.NET Framework 4.8). They need no installation and run from a folder or a USB flash drive.

| Program | What it does | Download |
|---|---|---|
| **CHAGEAR Toolbox** (`comp-info.exe` and the PowerShell console version) | Collects a computer's inventory (hardware, serial numbers, network adapters), sets up the network, computer name and domain membership, runs security checks, searches documents, shows the device database and synchronizes it with the team's cloud database | [chagear.top/toolbox](https://chagear.top/toolbox/) |
| **SUSpector** (`SUSpector.exe`) | Checks a user's computer before a security inspection: domain membership, antivirus, VPN, WinRAR, OneDrive sync, local administrators, expired passwords, and documents marked "for official use" that must not be stored on the computer. The user can then turn off OneDrive sync or delete the files that were found | [chagear.top/suspector](https://chagear.top/suspector/) |
| **youSBee** (`youSBee.exe`) | Shows the serial numbers of connected USB flash drives and disks and copies them with one click. Needs no administrator rights | [chagear.top/yousbee](https://chagear.top/yousbee/) |

The programs are available in Ukrainian, English and Russian.

## Building

```bat
tools\build.BAT                  rem all three programs, then the checks in app\tests
tools\build.BAT suspector        rem or: toolbox, suspector, yousbee
```

Nothing has to be installed: `build.BAT` uses `csc.exe` from .NET Framework 4.x, which is part of Windows.
The PowerShell tests are in `tests\*.tests.ps1`; the cloud part (`cloud\`, a Cloudflare Worker) has its own
`npm test`.

## Releases

Each program has its own version (`YYYY.MM.DD.N`). Pushing a tag builds and publishes a release with
[GitHub Actions](.github/workflows/release.yml): `v<version>` for the toolbox, `suspector-v<version>` for SUSpector.
The workflow runs the tests, builds the programs from this repository and uploads the package to the cloud,
which the programs' built-in updaters download from.

## Repository layout

| Path | Contents |
|---|---|
| `app/src` | C# source of `comp-info.exe`, `SUSpector.exe` and `youSBee.exe` (they share most of the code) |
| `*.ps1`, `*.BAT` | the console version of the toolbox |
| `cloud/` | the cloud database: Cloudflare Worker, D1 and the web interface |
| `tools/` | build and release scripts |
| `tests/`, `app/tests` | tests |

Detailed documentation (in Russian) lives in the Markdown files in the repository root, starting with
[PROJECT_CONTEXT.md](PROJECT_CONTEXT.md).

## License

[GNU General Public License v3.0 or later](LICENSE). Copyright (c) 2026 bobanko.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Only the programs built from this repository by its GitHub Actions workflow are signed.
Every release is approved manually before it is signed.

Team roles:

- Committers and reviewers: [bobanko](https://github.com/bobanko)
- Approvers: [bobanko](https://github.com/bobanko)

## Privacy policy

These programs will not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating them.

In detail:

- **youSBee** never uses the network.
- **SUSpector** runs all checks locally and never sends their results anywhere. It connects to
  `chagear.top` only when the user opens "Check for updates", to read the latest version and download it.
- **CHAGEAR Toolbox** keeps the collected inventory in its local `data` folder. It sends records to the cloud
  database (the address in `cloud-config.json`) only when the user runs a synchronization or an update check
  with their personal access key.
