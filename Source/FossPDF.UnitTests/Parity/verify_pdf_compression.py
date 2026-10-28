"""Independently verify FP-306 compression and linearization artifacts with pypdf and qpdf."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import pypdf
from pypdf import PdfReader


def page_facts(document):
    return [(list(page.mediabox), page.get_contents().get_data()) for page in document.pages]


def run_qpdf(executable, *arguments):
    result = subprocess.run(
        [executable, *arguments],
        capture_output=True,
        text=True,
        check=False,
    )

    return result.returncode, (result.stdout + result.stderr).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--qpdf", default="qpdf")
    parser.add_argument("--benchmarks", type=Path)
    parser.add_argument("--catalog", type=Path, default=Path(__file__).resolve().parents[3] /
                        "docs/feature-parity/acceptance-fixtures.json")
    args = parser.parse_args()

    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    fixture = next(item for item in catalog["fixtures"] if item["id"] == "PDF-306")["input"]
    options = fixture["options"]
    checks = []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    directory = args.directory
    source_path = directory / "source.pdf"
    generated_path = directory / "generated.pdf"
    optimized_path = directory / "optimized.pdf"

    source = PdfReader(source_path, strict=True)
    expected_pages = page_facts(source)
    require(len(expected_pages) >= 1, "Source has at least one page")
    require(not source.is_encrypted, "Source is not encrypted")

    for label, path in (("generated", generated_path), ("optimized", optimized_path)):
        reader = PdfReader(path, strict=True)
        require(len(reader.pages) == len(expected_pages),
                f"{label} document keeps every page")
        require(page_facts(reader) == expected_pages,
                f"{label} document preserves every page box and decoded content stream")
        require(reader.metadata is not None and reader.metadata.title == source.metadata.title,
                f"{label} document keeps the document title")
        require(b"/Linearized" in path.read_bytes(),
                f"{label} document carries the linearization parameter dictionary")

        structure_code, structure_output = run_qpdf(args.qpdf, "--check", str(path))
        require(structure_code in (0, 3),
                f"qpdf --check reads the {label} document completely: {structure_output}")

        linearization_code, linearization_output = run_qpdf(
            args.qpdf, "--check-linearization", str(path))
        require(linearization_code == 0,
                f"qpdf --check-linearization accepts the {label} document: {linearization_output}")

        if options.get("additional_compression"):
            require(b"/ObjStm" in path.read_bytes(),
                    f"{label} document uses object streams for additional compression")

    version_code, version_output = run_qpdf(args.qpdf, "--version")
    require(version_code == 0, "qpdf is available for independent verification")

    sizes = {
        name: (directory / name).stat().st_size
        for name in ("source.pdf", "generated.pdf", "optimized.pdf")
    }
    measurements = None
    if args.benchmarks:
        measurements = json.loads((args.benchmarks / "measurements.json").read_text())
        require({item["name"] for item in measurements} == {"text", "images", "attachments"},
                "Processing measurements cover text, images and attachment-heavy input")
        for item in measurements:
            name = item["name"]
            before = args.benchmarks / (name + "-source.pdf")
            after = args.benchmarks / (name + "-optimized.pdf")
            original, optimized = PdfReader(before, strict=True), PdfReader(after, strict=True)
            require(page_facts(original) == page_facts(optimized), name + ": benchmark pages and painting are unchanged")
            require(dict(original.metadata) == dict(optimized.metadata), name + ": benchmark document metadata is unchanged")
            attachments = lambda reader: {key: list(value) for key, value in reader.attachments.items()}
            require(attachments(original) == attachments(optimized), name + ": benchmark attachment bytes are unchanged")
            require(item["inputBytes"] == before.stat().st_size and item["outputBytes"] == after.stat().st_size,
                    name + ": recorded sizes match the independently inspected files")
            require(len(item["samples"]) == 5 and item["medianMilliseconds"] == sorted(item["samples"])[2],
                    name + ": five elapsed-time samples and their median are recorded")
            for path in (before, after):
                code, output = run_qpdf(args.qpdf, "--check", str(path))
                require(code == 0, name + ": qpdf reads " + path.name + " without warnings: " + output)
            code, output = run_qpdf(args.qpdf, "--check-linearization", str(after))
            require(code == 0, name + ": benchmark linearization is valid: " + output)
            if name == "attachments":
                extracted = attachments(optimized)
                require(set(extracted) == {"rows.csv", "binary.dat"} and len(extracted["binary.dat"][0]) == 512 * 1024,
                        "Attachment-heavy benchmark includes the declared 512 KiB binary payload")
                require(extracted["rows.csv"] == [b"2026-10-28,service,1200.00,EUR\n" * 40000],
                        "Attachment-heavy benchmark preserves all 40,000 CSV rows exactly")
    report = {
        "pypdf": pypdf.__version__,
        "qpdf": version_output.splitlines()[0] if version_output else "unknown",
        "checks": checks,
        "sizes": sizes,
        "processing_measurements": measurements,
        "sha256": {
            name: hashlib.sha256((directory / name).read_bytes()).hexdigest()
            for name in sizes
        },
    }

    (directory / "independent-validation.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8")

    print(f"FP-306: {len(checks)} independent compression checks passed "
          f"(pypdf {pypdf.__version__}, {report['qpdf']}).")


if __name__ == "__main__":
    main()
