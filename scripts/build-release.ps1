<#
Builds the signed release APK (plan §14).

  .\scripts\build-release.ps1 -KeyStore D:\Keys\roastit.keystore

Asks for the keystore password; it is never written to a file or the command history.
Output: artifacts\release\com.roastit.app-Signed.apk
#>
param(
    [Parameter(Mandatory)] [string] $KeyStore,
    [string] $Alias = "roastit"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $KeyStore)) { throw "Keystore not found: $KeyStore" }

$secure = Read-Host "Keystore password" -AsSecureString
$env:ROASTIT_KEY_PASS = [System.Net.NetworkCredential]::new("", $secure).Password
try {
    $out = Join-Path (Split-Path $PSScriptRoot) "artifacts\release"
    dotnet publish src\RoastIT.App -f net10.0-android -c Release -o $out `
        -p:AndroidPackageFormats=apk `
        -p:AndroidKeyStore=true `
        -p:AndroidSigningKeyStore=$KeyStore `
        -p:AndroidSigningKeyAlias=$Alias `
        -p:AndroidSigningKeyPass=env:ROASTIT_KEY_PASS `
        -p:AndroidSigningStorePass=env:ROASTIT_KEY_PASS
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    Write-Host "Done: $out\com.roastit.app-Signed.apk"
}
finally { Remove-Item Env:\ROASTIT_KEY_PASS -ErrorAction SilentlyContinue }
