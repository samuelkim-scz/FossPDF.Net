"""Independently inspect FP-304 artifacts without printing or recording passwords."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path

import pypdf
from pypdf import PdfReader


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--catalog", type=Path, default=Path(__file__).resolve().parents[3] /
                        "docs/feature-parity/acceptance-fixtures.json")
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    fixture = next(item for item in catalog["fixtures"] if item["id"] == "PDF-304")["input"]
    user, owner = fixture["user_password"], fixture["owner_password"]
    checks = []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    def page_facts(document):
        return [(list(page.mediabox), page.get_contents().get_data()) for page in document.pages]

    directory = args.directory
    source = PdfReader(directory / "source.pdf", strict=True)
    expected = page_facts(source)
    require(not source.is_encrypted and len(expected) == 2, "Source has two unencrypted pages")
    modes = [("encrypted-40.pdf", 1, 2, 40, None, -24),
             ("encrypted-128.pdf", 4, 4, 128, "/V2", -2072),
             ("encrypted.pdf", 5, 6, 256, "/AESV3", -2072)]

    for name, version, revision, length, method, permissions in modes:
        path = directory / name
        reader = PdfReader(path, strict=True)
        require(reader.is_encrypted, f"{length}-bit output is encrypted")
        encryption = reader.trailer["/Encrypt"]
        require(encryption["/V"] == version and encryption["/R"] == revision,
                f"{length}-bit output uses the expected security handler")
        require(encryption.get("/Length", 40) == length and encryption["/P"] == permissions,
                f"{length}-bit output has the expected key length and permissions")
        if method:
            filters = encryption["/CF"].get_object()
            require(any(item.get_object().get("/CFM") == method for item in filters.values()),
                    f"{length}-bit output has the expected crypt filter")
        for candidate, role in (("", "empty"), ("wrong-password", "incorrect")):
            require(PdfReader(path, strict=True).decrypt(candidate) == 0,
                    f"{length}-bit output rejects an {role} password")
        for password, expected_role, role in ((user, 1, "user"), (owner, 2, "owner")):
            authenticated = PdfReader(path, strict=True)
            require(authenticated.decrypt(password) == expected_role,
                    f"{length}-bit output authenticates the {role} role")
            require(page_facts(authenticated) == expected,
                    f"{length}-bit {role} decryption preserves page boxes and decoded painting")
        data = path.read_bytes()
        require(all(password.encode("utf-8") not in data for password in (user, owner)),
                f"{length}-bit output contains neither plaintext password")

    decrypted = PdfReader(directory / "decrypted.pdf", strict=True)
    require(not decrypted.is_encrypted and "/Encrypt" not in decrypted.trailer,
            "Decrypted output has no security handler")
    require(page_facts(decrypted) == expected, "Decrypted output preserves source pages exactly")
    names = ["source.pdf", "decrypted.pdf"] + [mode[0] for mode in modes]
    report = {"pypdf": pypdf.__version__, "cryptography": importlib.metadata.version("cryptography"),
              "checks": checks,
              "sha256": {name: hashlib.sha256((directory / name).read_bytes()).hexdigest() for name in names}}
    serialized = json.dumps(report, indent=2)
    require(all(password not in serialized for password in (user, owner)), "Inspection report contains no passwords")
    (directory / "independent-validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"FP-304: {len(checks)} independent security checks passed (pypdf {pypdf.__version__}).")


if __name__ == "__main__":
    main()
