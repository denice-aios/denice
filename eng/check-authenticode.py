#!/usr/bin/env python3
# Copyright (c) Denice.ai. All rights reserved.
"""Checks the Authenticode signature of Windows PE files on any OS, without signtool or osslsigncode.

For each file it proves, from the file's own bytes:
  1. the PE security directory holds a PKCS#7 Authenticode signature (WIN_CERT_TYPE_PKCS_SIGNED_DATA);
  2. the SHA-256 image hash Authenticode defines (the whole file except the checksum field, the security directory
     entry and the signature itself) equals the digest the signature carries (SpcIndirectDataContent), so no byte of
     the program changed after signing;
  3. the PKCS#7 signature over that digest verifies (`openssl smime -verify`, chain trust not asked for: Denice's
     certificates are self-signed and are trusted by pin, exactly as Denice Runtime trusts them);
  4. the signer's certificate is one of the pinned Denice certificates (SHA-256 fingerprints).
Prints one line per file ("authenticode PASS|FAIL: <file>: <detail>") and exits 1 when any file fails.

Usage: check-authenticode.py <pinned-fingerprint>[,<pinned-fingerprint>...] <pe-file> [<pe-file> ...]
Requires: python3 and OpenSSL 3 (LibreSSL, macOS's /usr/bin/openssl, cannot verify this detached PKCS#7 form): the first
of $OPENSSL, `openssl` on PATH and Homebrew's openssl@3 that is OpenSSL. Run with `python3 -I` (the files are downloads).
"""

import hashlib
import os
import subprocess
import sys
import tempfile

SECURITY_DIRECTORY = 4
WIN_CERT_TYPE_PKCS_SIGNED_DATA = 0x0002
OID_SPC_INDIRECT_DATA = bytes.fromhex("060a2b060104018237020104")  # 1.3.6.1.4.1.311.2.1.4
OID_SHA256 = bytes.fromhex("0609608648016503040201")  # 2.16.840.1.101.3.4.2.1


def find_openssl():
    candidates = [os.environ.get("OPENSSL", ""), "openssl",
                  "/opt/homebrew/opt/openssl@3/bin/openssl", "/usr/local/opt/openssl@3/bin/openssl"]
    for candidate in filter(None, candidates):
        try:
            version = subprocess.run([candidate, "version"], capture_output=True, text=True, check=False).stdout
        except OSError:
            continue
        if version.startswith("OpenSSL "):
            return candidate
    sys.exit("check-authenticode: OpenSSL 3 is needed (LibreSSL cannot verify Authenticode's detached PKCS#7)")


OPENSSL = None


class Refused(Exception):
    """The file is not validly signed by a pinned Denice certificate."""


def der(data, at):
    """Reads one DER element at `at`: returns (tag, header_length, content_start, end)."""
    tag = data[at]
    length = data[at + 1]
    start = at + 2
    if length & 0x80:
        count = length & 0x7F
        length = int.from_bytes(data[start:start + count], "big")
        start += count
    return tag, start - at, start, start + length


def children(data, start, end):
    """Yields (tag, element_start, content_start, element_end) for each element in data[start:end]."""
    at = start
    while at < end:
        tag, _, content, element_end = der(data, at)
        yield tag, at, content, element_end
        at = element_end


def pe_layout(image):
    """Returns (checksum_offset, security_entry_offset, signature_offset, signature_size)."""
    if image[:2] != b"MZ":
        raise Refused("not a PE file (no MZ header)")
    pe = int.from_bytes(image[0x3C:0x40], "little")
    if image[pe:pe + 4] != b"PE\0\0":
        raise Refused("not a PE file (no PE signature)")
    optional = pe + 24
    magic = int.from_bytes(image[optional:optional + 2], "little")
    directories = optional + {0x10B: 96, 0x20B: 112}.get(magic, -1 - optional)
    if directories < 0:
        raise Refused(f"unknown optional header magic 0x{magic:x}")
    entry = directories + 8 * SECURITY_DIRECTORY
    offset = int.from_bytes(image[entry:entry + 4], "little")
    size = int.from_bytes(image[entry + 4:entry + 8], "little")
    if offset == 0 or size == 0:
        raise Refused("no Authenticode signature (empty security directory)")
    if offset + size > len(image):
        raise Refused("the security directory points past the end of the file")
    return optional + 64, entry, offset, size


def image_hash(image, checksum, entry, signature, size):
    digest = hashlib.sha256()
    digest.update(image[:checksum])
    digest.update(image[checksum + 4:entry])
    digest.update(image[entry + 8:signature])
    digest.update(image[signature + size:])
    return digest.digest()


def pkcs7_of(image, signature, size):
    length = int.from_bytes(image[signature:signature + 4], "little")
    kind = int.from_bytes(image[signature + 6:signature + 8], "little")
    if kind != WIN_CERT_TYPE_PKCS_SIGNED_DATA or length > size or length <= 8:
        raise Refused(f"the certificate table holds no PKCS#7 signature (type 0x{kind:x})")
    return image[signature + 8:signature + length]


def indirect_data(pkcs7):
    """Returns (content_body, sha256_digest) of the SpcIndirectDataContent the signature carries."""
    _, _, content_info, content_end = der(pkcs7, 0)
    parts = list(children(pkcs7, content_info, content_end))
    _, _, explicit, _ = parts[1][:4]
    _, _, signed_data, signed_end = der(pkcs7, explicit)
    encapsulated = list(children(pkcs7, signed_data, signed_end))[2]
    _, _, encap_content, encap_end = encapsulated
    oid, wrapper = list(children(pkcs7, encap_content, encap_end))[:2]
    if pkcs7[oid[1]:oid[3]] != OID_SPC_INDIRECT_DATA:
        raise Refused("the signature does not carry SpcIndirectDataContent")
    _, _, body, body_end = der(pkcs7, wrapper[2])  # SpcIndirectDataContent; Authenticode signs its body only
    _, digest_info = list(children(pkcs7, body, body_end))[:2]
    algorithm, value = list(children(pkcs7, digest_info[2], digest_info[3]))[:2]
    algorithm_oid = list(children(pkcs7, algorithm[2], algorithm[3]))[0]
    if pkcs7[algorithm_oid[1]:algorithm_oid[3]] != OID_SHA256:
        raise Refused("the signature's image digest is not SHA-256")
    return pkcs7[body:body_end], pkcs7[value[2]:value[3]]


def signer_fingerprint(pkcs7, content):
    """Verifies the PKCS#7 signature over the indirect data with openssl; returns the signer's SHA-256 fingerprint."""
    with tempfile.TemporaryDirectory() as work:
        paths = {name: os.path.join(work, name) for name in ("sig.der", "content.bin", "signer.pem")}
        with open(paths["sig.der"], "wb") as handle:
            handle.write(pkcs7)
        with open(paths["content.bin"], "wb") as handle:
            handle.write(content)
        verify = subprocess.run(
            [OPENSSL, "smime", "-verify", "-inform", "DER", "-in", paths["sig.der"], "-content", paths["content.bin"],
             "-binary", "-noverify", "-signer", paths["signer.pem"], "-out", os.devnull],
            capture_output=True, text=True, check=False)
        if verify.returncode != 0:
            raise Refused("the PKCS#7 signature does not verify: " + (verify.stderr.strip().splitlines() or ["?"])[0])
        printed = subprocess.run(
            [OPENSSL, "x509", "-in", paths["signer.pem"], "-noout", "-fingerprint", "-sha256", "-subject"],
            capture_output=True, text=True, check=True).stdout
    lines = printed.strip().splitlines()
    return lines[0].split("=", 1)[1].strip().upper(), lines[1].split("=", 1)[1].strip() if len(lines) > 1 else ""


def check(path, pinned):
    with open(path, "rb") as handle:
        image = handle.read()
    checksum, entry, signature, size = pe_layout(image)
    pkcs7 = pkcs7_of(image, signature, size)
    content, carried = indirect_data(pkcs7)
    actual = image_hash(image, checksum, entry, signature, size)
    if actual != carried:
        raise Refused(f"the image hash {actual.hex()} differs from the signed digest {carried.hex()}")
    fingerprint, subject = signer_fingerprint(pkcs7, content)
    if fingerprint not in pinned:
        raise Refused(f"signed by an unpinned certificate {fingerprint} ({subject})")
    return f"image hash {actual.hex()} signed by pinned {fingerprint} ({subject})"


def main(argv):
    if len(argv) < 3:
        print(next(line for line in __doc__.splitlines() if line.startswith("Usage:")), file=sys.stderr)
        return 2
    global OPENSSL
    OPENSSL = find_openssl()
    pinned = {item.strip().upper() for item in argv[1].split(",") if item.strip()}
    ok = True
    for path in argv[2:]:
        try:
            print(f"authenticode PASS: {path}: {check(path, pinned)}")
        except (Refused, IndexError, ValueError) as error:
            ok = False
            print(f"authenticode FAIL: {path}: {error}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
