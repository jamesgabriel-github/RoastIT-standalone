# RoastIT release guide (Android)

Step-by-step for building a signed APK and getting it onto the shop phone.
The rules behind it are in plan §14; this is the hands-on version. Run
everything in PowerShell, from the repo root unless a step says otherwise.

> **Never change:** `ApplicationId` (`com.roastit.app`) and the signing
> keystore. Android refuses to update an installed app signed with a
> different key, and the only fix is uninstalling, which **deletes all data**.

## Quick path (every release after the key exists)

```powershell
# 1. Bump the version (see "Before you build")
# 2. Build and sign
.\scripts\build-release.ps1 -KeyStore D:\Keys\roastit.keystore
# 3. Install over the old version
adb install -r artifacts\release\com.roastit.app-Signed.apk
```

The script asks for the keystore password, never stores it, and removes it
from the environment when it finishes. Output:
`artifacts\release\com.roastit.app-Signed.apk`.

---

## One-time setup: the signing key

Do this once, ever. If `D:\Keys\roastit.keystore` already exists, skip to
"Before you build".

### 1. Find `keytool`

`keytool` isn't on PATH. It ships with the JDK that the Android workload
installs. The folder name includes the JDK version, so if the path below
fails, look for the newest `jdk-*` folder:

```powershell
$keytool = "C:\Program Files\Android\openjdk\jdk-21.0.8\bin\keytool.exe"
Test-Path $keytool          # must print True
Get-ChildItem "C:\Program Files\Android\openjdk" -Directory     # if it didn't
```

### 2. Create the key (outside the repo)

```powershell
New-Item -ItemType Directory -Force D:\Keys | Out-Null
cd D:\Keys
& $keytool -genkeypair -v `
    -keystore roastit.keystore -alias roastit `
    -keyalg RSA -keysize 2048 -validity 10000
```

It asks for a keystore password and some name fields. Use a strong password
and press Enter at the "key password" prompt to reuse it (the build passes
one password for both). The alias must stay `roastit`.

### 3. Verify it

```powershell
& $keytool -list -v -keystore D:\Keys\roastit.keystore -alias roastit
```

### 4. Back it up now

Copy `roastit.keystore` to a second place (USB stick, private cloud drive)
and store the password in a password manager. Losing either one means the
installed app can never be updated.

- Never commit the keystore. `.gitignore` blocks `*.keystore` and `*.jks`,
  but keep it outside the repo anyway.
- Never put the password in a file, a script or chat.

---

## Before you build

1. **Bump the version** in `src\RoastIT.App\RoastIT.App.csproj`:
   - `ApplicationVersion`: whole number, **+1 every release** (Android
     rejects an update that doesn't go up). Build 1 is the first release.
   - `ApplicationDisplayVersion`: `MAJOR.MINOR.PATCH`, what people see.
2. **Add a line to `CHANGELOG.md`** for the new version.
3. **Run the tests** and make sure they pass:
   `dotnet test tests\RoastIT.Tests`
4. **Work from a clean tree**: `git status` shows nothing you didn't mean
   to ship.
5. If this release adds a migration, remember that migrations that have
   shipped are never edited (CLAUDE.md rule 8).

## Build the signed APK

### Recommended: the script

```powershell
.\scripts\build-release.ps1 -KeyStore D:\Keys\roastit.keystore
```

Use `-Alias <name>` only if the alias isn't `roastit`.

### Manual equivalent

Use this only if the script can't be used. It does the same thing.

```powershell
cd <repo root>

# Ask for the password without echoing it, keep it only in this session
$secure = Read-Host "Keystore password" -AsSecureString
$env:ROASTIT_KEY_PASS = [System.Net.NetworkCredential]::new("", $secure).Password

dotnet publish src\RoastIT.App -f net10.0-android -c Release `
  -p:AndroidPackageFormats=apk `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=D:\Keys\roastit.keystore `
  -p:AndroidSigningKeyAlias=roastit `
  -p:AndroidSigningKeyPass=env:ROASTIT_KEY_PASS `
  -p:AndroidSigningStorePass=env:ROASTIT_KEY_PASS

# Always clear it afterwards
Remove-Item Env:\ROASTIT_KEY_PASS
```

- `env:ROASTIT_KEY_PASS` (no `$`) is correct: it tells MSBuild to read that
  environment variable.
- Always pass `-f net10.0-android`. Building all targets tries iOS and fails.
- Where the APK lands:

  | How you built | Output |
  |---|---|
  | `scripts\build-release.ps1` | `artifacts\release\com.roastit.app-Signed.apk` |
  | Manual (no `-o`) | `src\RoastIT.App\bin\Release\net10.0-android\publish\com.roastit.app-Signed.apk` |

### Confirm it is signed with your key

```powershell
& $keytool -printcert -jarfile "artifacts\release\com.roastit.app-Signed.apk"
```

Check that the owner and SHA-256 fingerprint match the keystore
(`keytool -list -v` above). It must **not** say "Android Debug". Keep the
fingerprint in your notes; every release must show the same one.

---

## Install on the phone

1. Connect the phone (USB debugging on) and check it is seen:
   `adb devices`
2. Install, keeping the app's data:
   `adb install -r artifacts\release\com.roastit.app-Signed.apk`

   Or copy the APK to the phone (USB, Drive) and open it. Allow "Install
   unknown apps" for the app you opened it with.
3. First install only: open the app and complete the PIN setup. **Write
   down the recovery code**; it is shown once.
4. Keep **Android System WebView** up to date (Play Store).

Common install errors:

| Message | Meaning |
|---|---|
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | Installed app was signed with a different key (often a debug build). Export a backup, uninstall, install the release. Never happens between release builds |
| `INSTALL_FAILED_VERSION_DOWNGRADE` | `ApplicationVersion` is not higher than the installed one. Bump it and rebuild |

## Test before handing it over

The release build is trimmed, which can break things a debug build doesn't.
Test on a device, with **real data**, not a fresh install only.

**First release (trimming check):** Bookings, Menu, Dashboard, Export,
Import, "Save to a folder…", and the CSV backup.

**Every update (upgrade test):**
1. On a phone running the **previous** version with real data, take an
   Export first.
2. Install the new APK over it (`adb install -r`).
3. Open it: the PIN screen appears, migrations run after the automatic
   database copy, and nothing is missing (bookings, menu, settings).
4. Create a booking, record a payment, run an Export and an Import.

If any step fails, don't hand the APK over.

## Release it

Once the tests pass:

1. Set the date on the `CHANGELOG.md` entry (replace "not released yet").
2. Commit the version bump and changelog.
3. Tag the release: `git tag v1.0.0` (use the display version).
4. Keep a copy of the exact APK you shipped (for example next to the
   keystore backup), so you can reinstall it if needed.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `Keystore not found` | Wrong `-KeyStore` path. Check with `Test-Path` |
| `keystore password was incorrect` / `Cannot recover key` | Wrong password, or key password differs from the store password. The build uses one password for both |
| Build output is unsigned or "Android Debug" | Not published with the signing properties. Use the script |
| `keytool` not recognised | Not on PATH. Call it by full path (see setup step 1) |
| iOS errors during build | You left out `-f net10.0-android` |
| App crashes only in the release build | Likely trimming (EF Core, or the raw-SQLite export/import). Get the log with `adb logcat` and tell Claude |

## Never do

- Change `ApplicationId` or switch to a different keystore.
- Commit the keystore or password, or type the password into a command line
  (use the prompt).
- Ship without testing the upgrade path on real data.
- Uninstall the app on the shop phone without exporting a backup first.
