"""Audit the locked restored cohort and its redistributable native/notice files."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(assets_path, output):
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    folders = [Path(folder) for folder in assets["packageFolders"]]
    project = assets_path.parent.parent
    repository = project.parent.parent
    notices = repository / "docs/feature-parity/notices"
    packages = []
    native_count = 0
    for identity, library in sorted(assets["libraries"].items()):
        if library["type"] != "package":
            continue
        package, version = identity.split("/")
        location = next(folder / library["path"] for folder in folders if (folder / library["path"]).is_dir())
        metadata = ET.parse(next(location.glob("*.nuspec"))).getroot()
        values = {element.tag.split("}")[-1]: element.text for element in metadata.iter()}
        expected_license = "MS-PL" if package == "Svg.Custom" else "MIT"
        if values.get("license") != expected_license:
            raise ValueError(f"Unreviewed license: {identity} {values.get('license')}")
        entry = {"package": package, "version": version, "license": values["license"],
                 "copyright": values.get("copyright"), "nativeAssets": [], "notices": []}
        if ".NativeAssets." in package:
            cohort = "SkiaSharp" if package.startswith("SkiaSharp") else "HarfBuzzSharp"
            expected_version = "3.119.4" if cohort == "SkiaSharp" else "8.3.1.5"
            if version != expected_version:
                raise ValueError(f"Native cohort mismatch: {identity}")
            for name in ("LICENSE.txt", "THIRD-PARTY-NOTICES.txt"):
                path = location / name
                vendored = notices / ("SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt" if name == "THIRD-PARTY-NOTICES.txt"
                                      else f"{cohort}-{version}-LICENSE.txt")
                if not path.is_file() or sha256(path) != sha256(vendored):
                    raise ValueError(f"Missing/changed redistributable notice: {identity} {name}")
                entry["notices"].append({"file": name, "sha256": sha256(path)})
            for relative in library["files"]:
                if relative.startswith("runtimes/") and "/native/" in relative and relative.endswith((".dll", ".so", ".dylib")):
                    path = location / relative
                    entry["nativeAssets"].append({"path": relative, "rid": relative.split("/")[1],
                                                  "bytes": path.stat().st_size, "sha256": sha256(path)})
            if not entry["nativeAssets"]:
                raise ValueError(f"Missing native libraries: {identity}")
            native_count += 1
        packages.append(entry)
    if native_count != 6:
        raise ValueError(f"Expected six native asset packages, found {native_count}")
    lock = json.loads((project / "packages.lock.json").read_text(encoding="utf-8-sig"))
    locked = lock["dependencies"]["net9.0"]
    for package in packages:
        if locked[package["package"]]["resolved"] != package["version"]:
            raise ValueError(f"Lock/restore disagreement: {package['package']}")
    output.mkdir(parents=True, exist_ok=True)
    (output / "dependency-assets.json").write_text(json.dumps({"passed": True, "packages": packages}, indent=2), encoding="utf-8")
    print(f"Dependency audit passed: {len(packages)} packages, {native_count} native packages; assets, notices and lock verified.")


if __name__ == "__main__":
    audit(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
