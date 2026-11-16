#!/usr/bin/env bash
#
# Rebuilds the code-signature fixtures CodeSignatureSpecTests checks. Run on macOS; needs clang, codesign, security,
# openssl, the .NET SDK and osslsigncode (brew install osslsigncode). The tests cannot sign at test time: starting a
# process (codesign, security, signtool) is banned in every assembly (DN0011), so the signed files are committed here.
#
# Each set is signed with a fresh throwaway certificate that is self-signed and untrusted — exactly like the
# certificates CI signs Denice with — so the tests prove a signature passes on its pin alone, without chaining to a
# trusted root. The private keys are deleted when the script ends. A rerun mints new certificates: copy the two
# fingerprints it prints into CodeSignatureSpecTests (MacOS signer and WindowsSigner).
#
#   macos/signed-macho    thin x86_64 Mach-O, codesign --options runtime with the fixture certificate
#   macos/unsigned-macho  the same Mach-O, unsigned
#   macos/adhoc-macho     the same Mach-O, ad-hoc signed (no certificate)
#   macos/Signed.app      a minimal app bundle, codesign --options runtime with the fixture certificate
#   windows/signed.dll    a minimal PE (a managed class library), Authenticode SHA-256 with the fixture certificate
#   windows/unsigned.dll  the same PE, unsigned
#
# Usage: make-fixtures.sh   (writes next to this script)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
KC="$WORK/fixture.keychain-db"
OLD_KEYCHAINS="$(security list-keychains -d user | sed 's/"//g')"
cleanup() {
  # shellcheck disable=SC2086
  security list-keychains -d user -s $OLD_KEYCHAINS
  security delete-keychain "$KC" 2>/dev/null || true
  rm -rf "$WORK"
}
trap cleanup EXIT

# A self-signed code-signing certificate and key, in PEM and PKCS#12.
mint_certificate() {
  local name="$1" cn="$2"
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=$cn/O=denice-aios" \
    -addext "keyUsage=critical,digitalSignature" -addext "extendedKeyUsage=critical,codeSigning" \
    -addext "basicConstraints=critical,CA:false" \
    -keyout "$WORK/$name.key.pem" -out "$WORK/$name.cert.pem" 2>/dev/null
  echo "$name signer: $(openssl x509 -in "$WORK/$name.cert.pem" -noout -fingerprint -sha256 | cut -d= -f2)"
}

# --- macOS -------------------------------------------------------------------------------------------------------
mint_certificate macos "Denice fixture macOS signing (untrusted)"
openssl pkcs12 -export -legacy -inkey "$WORK/macos.key.pem" -in "$WORK/macos.cert.pem" \
  -out "$WORK/macos.p12" -passout pass:fixture
security create-keychain -p fixture "$KC"
security unlock-keychain -p fixture "$KC"
security import "$WORK/macos.p12" -k "$KC" -P fixture -T /usr/bin/codesign >/dev/null
# shellcheck disable=SC2086
security list-keychains -d user -s "$KC" $OLD_KEYCHAINS
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k fixture "$KC" >/dev/null
IDENTITY="$(security find-identity -p codesigning "$KC" | grep -oE '[0-9A-F]{40}' | head -1)"

printf 'int main(void) { return 0; }\n' > "$WORK/fixture.c"
clang -arch x86_64 -Os -o "$WORK/fixture" "$WORK/fixture.c"

MAC="$HERE/macos"
rm -rf "$MAC" && mkdir -p "$MAC"
cp "$WORK/fixture" "$MAC/unsigned-macho"
cp "$WORK/fixture" "$MAC/adhoc-macho"
codesign --force --sign - "$MAC/adhoc-macho"
cp "$WORK/fixture" "$MAC/signed-macho"
codesign --force --options runtime --keychain "$KC" --sign "$IDENTITY" "$MAC/signed-macho"

APP="$MAC/Signed.app"
mkdir -p "$APP/Contents/MacOS"
cp "$WORK/fixture" "$APP/Contents/MacOS/Signed"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Signed</string>
<key>CFBundleIdentifier</key><string>ai.denice.test.signed</string>
<key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
PLIST
# Binary plist, so a Windows checkout's line-ending conversion can never touch the sealed file.
plutil -convert binary1 "$APP/Contents/Info.plist"
codesign --force --options runtime --keychain "$KC" --sign "$IDENTITY" "$APP"
codesign --verify --strict "$MAC/signed-macho"
codesign --verify --strict --deep "$APP"

# --- Windows -----------------------------------------------------------------------------------------------------
mint_certificate windows "Denice fixture Windows signing (untrusted)"
mkdir -p "$WORK/pe"
cat > "$WORK/pe/Fixture.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Deterministic>true</Deterministic>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
CSPROJ
printf 'namespace Fixture { public static class Marker { } }\n' > "$WORK/pe/Marker.cs"
# Built outside the repository, so the repository's Directory.Build.props and analyzers do not apply.
(cd "$WORK/pe" && dotnet build -c Release -o "$WORK/pe/out" >/dev/null)

WIN="$HERE/windows"
rm -rf "$WIN" && mkdir -p "$WIN"
cp "$WORK/pe/out/Fixture.dll" "$WIN/unsigned.dll"
osslsigncode sign -h sha256 -certs "$WORK/windows.cert.pem" -key "$WORK/windows.key.pem" \
  -in "$WIN/unsigned.dll" -out "$WIN/signed.dll" >/dev/null
osslsigncode verify -CAfile "$WORK/windows.cert.pem" -in "$WIN/signed.dll" >/dev/null
