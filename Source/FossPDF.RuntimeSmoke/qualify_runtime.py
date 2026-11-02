"""Publish and execute real .NET 9 consumers; preserve every failure and tool log."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

SOURCE = Path(__file__).resolve().parents[1]
PROJECT = SOURCE / "FossPDF.RuntimeSmoke" / "FossPDF.RuntimeSmoke.csproj"
FEATURES = ("Core", "Fonts", "Svg", "Pdf")
MODES = ("jit", "trimmed", "aot")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def native_rid() -> str:
    system = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}.get(platform.system())
    arch = {"amd64": "x64", "x86_64": "x64", "arm64": "arm64", "aarch64": "arm64"}.get(platform.machine().lower())
    require(system is not None and arch is not None, "The host OS/architecture is not covered by this runner.")
    return f"{system}-{arch}"


def verify(result: Path, publish: Path, feature: str, mode: str, rid: str, baseline: Path | None) -> dict:
    import pymupdf as fitz
    from pypdf import PdfReader

    require(__import__("pypdf").__version__ == "6.21.2" and fitz.VersionBind == "1.26.7",
            "Install the pinned versions in requirements-validation.txt before qualifying output.")

    report = json.loads((result / "runtime-report.json").read_text(encoding="utf-8-sig"))
    require(report["SchemaVersion"] == 1 and report["Feature"] == feature and report["BuildMode"] == mode,
            "The executable does not match the requested feature/mode.")
    require(report["Rid"] == rid, f"Executed RID {report['Rid']} differs from requested RID {rid}.")
    expected_arch = {"x64": "X64", "arm64": "Arm64"}[rid.rsplit("-", 1)[1]]
    require(report["Architecture"] == expected_arch and report["OsArchitecture"] == expected_arch,
            "The consumer must execute on a native OS/architecture matching its requested RID.")
    require(report["DynamicCodeSupported"] == (mode != "aot"), "The runtime dynamic-code capability does not match the publish mode.")
    expected = {"TXT-001", "IMG-001", "layout-error"}
    expected |= {"Fonts": {"fonts"}, "Svg": {"IMG-201"}, "Pdf": {"PDF-302"}}.get(feature, set())
    require({item["Id"] for item in report["Results"]} == expected, "The executable did not run the full fixture set.")
    require(all(item["Passed"] for item in report["Results"]), "The published consumer reported a runtime failure.")
    require(report["NativeFiles"], "Native asset provenance is missing.")
    for item in report["NativeFiles"]:
        asset = publish / item["Path"].replace(chr(92), "/")
        require(asset.is_file(), f"Native asset is missing: {asset}")
        require(asset.stat().st_size == item["Bytes"] and hashlib.sha256(asset.read_bytes()).hexdigest().upper() == item["Sha256"],
                f"Native asset hash mismatch: {asset}")

    checks = []
    def check(condition: bool, message: str) -> None:
        require(condition, message)
        checks.append(message)

    text = PdfReader(result / "TXT-001.pdf")
    check(len(text.pages) == 1, "Text fixture has one page.")
    extracted = "".join(text.pages[0].extract_text().split())
    check("FossPDFruntimeRevenueEUR12.00" in extracted, "Text and span order survive publication.")
    check(str(text.metadata.title) == "FossPDF runtime qualification", "Document metadata survives publication.")
    fonts = text.pages[0]["/Resources"]["/Font"].get_object()
    check(bool(fonts) and all(font.get_object().get("/ToUnicode") is not None for font in fonts.values()),
          "Published text retains Unicode font mappings.")

    def color(path: Path, x: int, y: int, expected_rgb: tuple[int, int, int]) -> None:
        image = fitz.Pixmap(str(path))
        actual = image.pixel(x, y)[:3]
        check(all(abs(a - b) <= 2 for a, b in zip(actual, expected_rgb)), f"{path.stem}: pixel ({x},{y}) preserves its declared color.")

    for x, y, rgb in ((30, 30, (211, 47, 47)), (70, 30, (25, 118, 210)),
                      (30, 70, (56, 142, 60)), (70, 70, (251, 192, 45))):
        color(result / "IMG-001.png", x, y, rgb)
    trace = (result / "layout-error.txt").read_text(encoding="utf-8-sig")
    check("Constrained" in trace and "10000" in trace, "Layout diagnostics retain element identity and size constraints.")

    if feature == "Fonts":
        font_pdf = PdfReader(result / "fonts.pdf")
        check("SubsetLatoRevenueEUR12.00" in "".join(font_pdf.pages[0].extract_text().split()),
              "Registered/subset fonts retain readable Unicode text.")
    if feature == "Svg":
        color(result / "IMG-201.png", 30, 50, (211, 47, 47))
        color(result / "IMG-201.png", 70, 50, (25, 118, 210))
        vector = PdfReader(result / "IMG-201.pdf")
        check(not vector.pages[0]["/Resources"].get("/XObject"), "SVG replay remains vector output.")
    if feature == "Pdf":
        merged = PdfReader(result / "PDF-302.pdf")
        check(len(merged.pages) == 3, "Page selection and merge retain three pages.")
        check(["".join(page.extract_text().split()) for page in merged.pages] == ["FIRST", "SECOND", "FIRST"],
              "Imported page text/order is preserved.")
        check([int(page.mediabox.width) for page in merged.pages] == [200, 240, 200], "Imported page boxes are preserved.")
        encrypted = fitz.open(result / "PDF-305-encrypted.pdf")
        check(encrypted.needs_pass and encrypted.authenticate("runtime-reader") > 0, "AES-256 output opens with the configured reader password.")
        check(encrypted.metadata["encryption"] == "Standard V5 R6 256-bit AES", "Encryption retains the declared AES-256 revision and strength.")
        check("RUN-503" in encrypted.get_xml_metadata(), "Encrypted XMP survives the pinned backend bridge.")
        check(encrypted.embfile_get("runtime-data.xml") == b"<runtime>RUN-503</runtime>", "Encrypted attachment bytes are preserved.")
        encrypted.close()
        decrypted = fitz.open(result / "PDF-305-decrypted.pdf")
        check(not decrypted.needs_pass and "RUN-503" in decrypted.get_xml_metadata(), "Decryption preserves metadata.")
        decrypted.close()

    comparisons = []
    if baseline is not None:
        for path in sorted(result.glob("*.png")):
            reference = fitz.Pixmap(str(baseline / path.name))
            actual = fitz.Pixmap(str(path))
            require((reference.width, reference.height, reference.n) == (actual.width, actual.height, actual.n), "Published raster dimensions changed.")
            pixels = actual.width * actual.height
            left, right = reference.samples, actual.samples
            changed = sum(any(abs(left[offset + channel] - right[offset + channel]) > 2 for channel in range(actual.n))
                          for offset in range(0, len(left), actual.n))
            fraction = changed / pixels
            check(fraction <= 0.002, f"{path.name}: changed-pixel fraction {fraction:.6f} is within 0.002 at 2/255 channel tolerance.")
            comparisons.append({"file": path.name, "changed_pixel_fraction": fraction})
    evidence = {"passed": True, "reader": f"pypdf {__import__('pypdf').__version__}; PyMuPDF {fitz.VersionBind}",
                "checks": checks, "raster_comparisons": comparisons}
    (result / "independent-inspection.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    return evidence


def run(args: list[str], log: Path, cwd: Path, timeout: int) -> tuple[int, float]:
    started = time.monotonic()
    with log.open("wb") as stream:
        try:
            process = subprocess.run(args, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout, check=False)
            return process.returncode, time.monotonic() - started
        except (OSError, subprocess.TimeoutExpired) as error:
            stream.write(str(error).encode("utf-8"))
            return -1, time.monotonic() - started


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"), default=native_rid())
    parser.add_argument("--features", nargs="+", choices=FEATURES, default=list(FEATURES))
    parser.add_argument("--modes", nargs="+", choices=MODES, default=list(MODES))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--verify-existing", action="store_true", help="Recheck existing outputs with the pinned readers without republishing.")
    parser.add_argument("--package-source", type=Path, help="Consume FossPDF packages exclusively from this feed in an isolated restore cache.")
    parser.add_argument("--package-version", default="2026.5.1")
    args = parser.parse_args()
    require(args.rid.split("-", 1)[0] == native_rid().split("-", 1)[0], "Cross-OS publishing is not execution evidence: use a native runner matching --rid.")
    root = args.output.resolve()
    if args.verify_existing:
        matrix = json.loads((root / "qualification.json").read_text(encoding="utf-8"))
        require(matrix["rid"] == args.rid, "Existing results target another RID.")
        baselines = {}
        for entry in matrix["profiles"]:
            if entry.get("publish_exit_code") != 0 or entry.get("run_exit_code") != 0:
                continue
            directory = root / entry["relative_directory"]
            try:
                evidence = verify(directory / "result", directory / "publish", entry["feature"], entry["mode"], args.rid,
                                  baselines.get(entry["feature"]))
                entry.update(status="passed", independent_checks=len(evidence["checks"]))
                entry.pop("error", None)
                if entry["mode"] == "jit":
                    baselines[entry["feature"]] = directory / "result"
            except Exception as error:
                entry.update(status="verification-failed", error=str(error))
            print(f"VERIFY {entry['feature']} {entry['mode']}: {entry['status']}", flush=True)
        (root / "qualification.json").write_text(json.dumps(matrix, indent=2) + "\n", encoding="utf-8")
        return 0 if all(item["status"] == "passed" for item in matrix["profiles"]) else 1
    require(not root.exists() or not any(root.iterdir()), "Choose a fresh output directory so stale reports cannot pass a failed run.")
    root.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    os.environ.update(env)
    sdk = subprocess.check_output(["dotnet", "--version"], cwd=SOURCE, text=True).strip()
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=SOURCE, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=SOURCE, text=True).strip())
    matrix = {"schema_version": 1, "started_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
              "rid": args.rid, "host": platform.platform(), "sdk": sdk, "source_commit": commit,
              "dirty_worktree": dirty, "profiles": []}
    package_properties = []
    if args.package_source:
        feed = args.package_source.resolve()
        require(feed.is_dir() and any(feed.glob("*.nupkg")), "The package feed is missing or empty.")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="fosspdf-local", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="fosspdf-local"), "package", pattern="FossPDF*")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget.org"), "package", pattern="*")
        config_path = root / "NuGet.Config"
        ET.ElementTree(config).write(config_path, encoding="utf-8", xml_declaration=True)
        package_properties = ["-p:UseFossPdfPackages=true", f"-p:SmokePackageVersion={args.package_version}",
                              f"-p:RestoreConfigFile={config_path}", f"-p:RestorePackagesPath={root / 'nuget-packages'}"]
        matrix["package_feed"] = str(feed)
        matrix["packages"] = [{"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                              for path in sorted(feed.glob("*.nupkg"))]
    for feature in args.features:
        baseline = None
        for mode in MODES:
            if mode not in args.modes:
                continue
            directory = root / feature.lower() / mode
            publish = directory / "publish"
            result = directory / "result"
            directory.mkdir(parents=True)
            command = ["dotnet", "publish", str(PROJECT), "-c", "Release", "-r", args.rid,
                       "--self-contained", "true", f"-p:RuntimeFeature={feature}", f"-p:QualificationMode={mode}",
                       "-p:GeneratePackageOnBuild=false", "-p:NuGetAudit=false",
                       "-p:TreatWarningsAsErrors=true", "-p:ILLinkTreatWarningsAsErrors=true",
                       "-p:IlcTreatWarningsAsErrors=true", "--output", str(publish)]
            command += package_properties
            entry = {"feature": feature, "mode": mode, "command": command, "status": "publish-failed",
                     "relative_directory": directory.relative_to(root).as_posix()}
            matrix["profiles"].append(entry)
            print(f"PUBLISH {args.rid} {feature} {mode}", flush=True)
            exit_code, duration = run(command, directory / "publish.log", SOURCE, args.timeout)
            entry.update(publish_exit_code=exit_code, publish_seconds=round(duration, 3))
            if exit_code == 0:
                if args.package_source:
                    assets = json.loads((PROJECT.parent / "obj" / "project.assets.json").read_text(encoding="utf-8"))
                    selected = {key: value for key, value in assets["libraries"].items() if key.startswith("FossPDF")}
                    require(selected and all(value["type"] == "package" for value in selected.values()),
                            "Qualification must use packages, not FossPDF project references.")
                    entry["foss_pdf_packages"] = sorted(selected)
                    entry["dependencies"] = sorted(assets["libraries"])
                executable = publish / ("FossPDF.RuntimeSmoke.exe" if os.name == "nt" else "FossPDF.RuntimeSmoke")
                exit_code, duration = run([str(executable), "--output", str(result)], directory / "run.log", directory, args.timeout)
                entry.update(status="run-failed", run_exit_code=exit_code, run_seconds=round(duration, 3))
                if exit_code == 0:
                    try:
                        evidence = verify(result, publish, feature, mode, args.rid, baseline)
                        entry.update(status="passed", independent_checks=len(evidence["checks"]))
                        if mode == "jit":
                            baseline = result
                    except Exception as error:
                        entry.update(status="verification-failed", error=str(error))
            print(f"RESULT {feature} {mode}: {entry['status']}", flush=True)
            (root / "qualification.json").write_text(json.dumps(matrix, indent=2) + "\n", encoding="utf-8")
    return 0 if all(item["status"] == "passed" for item in matrix["profiles"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
