"""Explicit development/CI installation of the reviewed Mustang CLI; never used at runtime."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile

VERSION = "2.26.0"
JAR_SHA256 = "42d7868cb68264874a7b8cab4c3587b03b23ccc7cd72373da917f66758bb9736"
URL = f"https://github.com/ZUGFeRD/mustangproject/releases/download/core-{VERSION}/Mustang-CLI-{VERSION}.jar"
RULES = {
    "schema/ZF_250/EN16931/FACTUR-X_EN16931.xsd": "34e51a9b26c95ef6e09297051b9921a05279a4024e659f236ba5d5e0f5a23bec",
    "schema/ZF_250/EN16931/FACTUR-X_EN16931_urn_un_unece_uncefact_data_standard_QualifiedDataType_100.xsd": "5a3ce756cfa8d4f2ff3165d68123cfb7fbf3c7b64664edb0de38cca64d5c413b",
    "schema/ZF_250/EN16931/FACTUR-X_EN16931_urn_un_unece_uncefact_data_standard_ReusableAggregateBusinessInformationEntity_100.xsd": "4a091d4339eddab30f6f5d29364ca2297607d72f03af7da2786637f08bdb3cf7",
    "schema/ZF_250/EN16931/FACTUR-X_EN16931_urn_un_unece_uncefact_data_standard_UnqualifiedDataType_100.xsd": "f87a1b78d2b7177955957002f8c2a7917e326038d38803f875266cc7579ea857",
    "xslt/ZF_250/FACTUR-X_EN16931.xslt": "d412c035cd790e0ea0de36b59f58a868f4d1a28bc2072fc2204f23dbeb2850d7",
    "xslt/ZF_250/FACTUR-X_EN16931_codedb.xml": "dd3856984b69e25d5ba6312ec7c8f68e4458064a5998bebcfe8ae66de1a21cad",
    "xslt/en16931schematron/EN16931-CII-validation.xslt": "b98ac4b99611b4fa332820e130f551805201a602d4f33173016f975ea904f192",
}


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def install(directory):
    directory.mkdir(parents=True, exist_ok=True)
    jar = directory / f"Mustang-CLI-{VERSION}.jar"
    if not jar.is_file():
        with urllib.request.urlopen(URL, timeout=60) as source, jar.open("wb") as output:
            while chunk := source.read(1024 * 1024):
                output.write(chunk)
    if sha256(jar) != JAR_SHA256:
        raise RuntimeError("Mustang CLI SHA-256 mismatch")
    with zipfile.ZipFile(jar) as package:
        for resource, expected in RULES.items():
            if hashlib.sha256(package.read(resource)).hexdigest() != expected:
                raise RuntimeError(f"Mustang validation resource mismatch: {resource}")
    manifest = {"version": VERSION, "url": URL, "jar_sha256": JAR_SHA256,
                "supported": ["Factur-X 1.09.2 EN16931", "ZUGFeRD 2.5.2 EN16931"], "rules_sha256": RULES}
    (directory / "qualification-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return jar


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--github-env", type=Path)
    args = parser.parse_args()
    jar = install(args.output.resolve())
    if args.github_env:
        with args.github_env.open("a", encoding="utf-8") as output:
            output.write(f"FOSSPDF_MUSTANG_JAR={jar}\n")
    print(f"FOSSPDF_MUSTANG_JAR={jar}")
