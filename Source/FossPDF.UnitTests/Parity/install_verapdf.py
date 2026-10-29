"""Install the reviewed, hash-pinned veraPDF CLI for CI/development, never at library runtime."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import urllib.request
import zipfile
from xml.etree import ElementTree as ET

VERSION = "1.30.3"
ARCHIVE_SHA256 = "b512bf24945503a33fc10738ecffc1f2dccaaf8593a448e94ee9ac86a9559a59"
JAR_SHA256 = "19f6edd5c92841a38d9b3b018a555b7ba173b13e4e0d29edecc232e5ba6c1d70"
PROFILES = {
    "2A": "22ef9e98557d1ff714e252d5cf020840b0ec05fddd437c47058eeaa225dc6af5",
    "2B": "bae015161f0c9b4296481f11936c97728dfd12dffb2df48ea744f885794da382",
    "2U": "52bddd46bc6c8bfcfb00cf4589935a7328668f709d92928d2e8841446c05a21d",
    "3A": "4eac441409c0523ded5d29d77e567f63ae3ac285d82505f673d2992286da674a",
    "3B": "fc6f5c9cccc8c52af8584f98505fe62979522e66acd525a67ce391c36d8dbc17",
    "3U": "2ac7b14a789681c4dc9d53ec66e9a28f6efa2cd61a4633487b4c15c111281bf2",
    "UA1": "794c9f6bf53c8cb89e532807e74fea58331a0d9a935c52cb054b63853bdd8b2a",
}


def sha256(path):
    with open(path, "rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def install(directory):
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / "runtime"
    cli = target / "bin" / f"cli-{VERSION}.jar"
    if not cli.is_file():
        archive = directory / f"verapdf-greenfield-{VERSION}-installer.zip"
        if not archive.is_file():
            url = f"https://software.verapdf.org/releases/1.30/{archive.name}"
            with urllib.request.urlopen(url, timeout=60) as response, archive.open("wb") as output:
                while chunk := response.read(1024 * 1024):
                    output.write(chunk)
        if sha256(archive) != ARCHIVE_SHA256:
            raise RuntimeError("veraPDF installer archive SHA-256 mismatch")
        if target.exists() and any(target.iterdir()):
            raise RuntimeError("Refusing to install into an existing nonempty runtime directory")
        installer = directory / f"verapdf-izpack-installer-{VERSION}.jar"
        with zipfile.ZipFile(archive) as package:
            entry = f"verapdf-greenfield-{VERSION}/{installer.name}"
            installer.write_bytes(package.read(entry))
        root = ET.Element("AutomatedInstallation", langpack="eng")
        ET.SubElement(root, "com.izforge.izpack.panels.htmlhello.HTMLHelloPanel", id="welcome")
        panel = ET.SubElement(root, "com.izforge.izpack.panels.target.TargetPanel", id="install_dir")
        ET.SubElement(panel, "installpath").text = str(target)
        packs = ET.SubElement(root, "com.izforge.izpack.panels.packs.PacksPanel", id="sdk_pack_select")
        for index, name in enumerate(("veraPDF GUI", "veraPDF CLI", "veraPDF Documentation", "veraPDF Sample Plugins")):
            ET.SubElement(packs, "pack", index=str(index), name=name, selected=str(index == 1).lower())
        ET.SubElement(root, "com.izforge.izpack.panels.install.InstallPanel", id="install")
        ET.SubElement(root, "com.izforge.izpack.panels.finish.FinishPanel", id="finish")
        config = directory / "auto-install.xml"
        ET.ElementTree(root).write(config, encoding="utf-8", xml_declaration=True)
        subprocess.run(["java", "-Djava.awt.headless=true", "-jar", str(installer), str(config)], check=True, timeout=120)
    if sha256(cli) != JAR_SHA256:
        raise RuntimeError("veraPDF CLI SHA-256 mismatch")
    with zipfile.ZipFile(cli) as package:
        for profile, expected in PROFILES.items():
            filename = "PDFUA-1.xml" if profile == "UA1" else f"PDFA-{profile}.xml"
            data = package.read(f"org/verapdf/pdfa/validation/{filename}")
            if hashlib.sha256(data).hexdigest() != expected:
                raise RuntimeError(f"veraPDF {profile} profile SHA-256 mismatch")
    manifest = {"version": VERSION, "archive_sha256": ARCHIVE_SHA256, "cli_sha256": JAR_SHA256, "profiles_sha256": PROFILES}
    (directory / "qualification-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return cli


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--github-env", type=Path)
    args = parser.parse_args()
    cli = install(args.output.resolve())
    if args.github_env:
        with args.github_env.open("a", encoding="utf-8") as environment:
            environment.write(f"FOSSPDF_VERAPDF_JAR={cli}\n")
    print(f"FOSSPDF_VERAPDF_JAR={cli}")
