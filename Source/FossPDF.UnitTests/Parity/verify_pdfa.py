"""Independently inspect every archival fixture, render it, and validate deliberate corruptions."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import unicodedata
from xml.etree import ElementTree as ET

import pymupdf
from pypdf import PdfReader, PdfWriter

from install_verapdf import JAR_SHA256, sha256

checks = 0


def require(condition, message):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(message)


def fonts(resources, seen=None):
    seen = set() if seen is None else seen
    for font in resources.get("/Font", {}).get_object().values() if "/Font" in resources else ():
        yield font.get_object()
    for value in resources.get("/XObject", {}).get_object().values() if "/XObject" in resources else ():
        form = value.get_object()
        identity = id(form)
        if identity not in seen and "/Resources" in form:
            seen.add(identity)
            yield from fonts(form["/Resources"], seen)


def inspect(directory):
    qpdf = shutil.which("qpdf")
    require(qpdf is not None, "qpdf is required")
    reference = pymupdf.open(directory / "reference.pdf")
    reference_pixels = reference[0].get_pixmap(alpha=False).samples
    profiles = ("2A", "2B", "2U", "3A", "3B", "3U")
    for profile in profiles:
        for mode in ("default", "compressed", "linearized", "uncompressed"):
            stem = f"PdfA{profile}-{mode}"
            path = directory / (stem + ".pdf")
            report = json.loads((directory / (stem + ".json")).read_text(encoding="utf-8"))
            validation = report["report"]["jobs"][0]["validationResult"][0]
            require(validation["compliant"] and validation["details"]["failedChecks"] == 0, stem + " compliance")
            require(validation["profileName"].lower() == f"pdf/a-{profile.lower()} validation profile", stem + " exact profile")
            checked = subprocess.run([qpdf, "--check", str(path)], capture_output=True, text=True)
            require(checked.returncode == 0, stem + " qpdf: " + checked.stdout + checked.stderr)
            if mode == "linearized":
                require(subprocess.run([qpdf, "--check-linearization", str(path)], capture_output=True).returncode == 0, stem + " hint tables")
            reader = PdfReader(path)
            root = reader.trailer["/Root"]
            require(not reader.is_encrypted and len(reader.pages) == 1, stem + " pages and encryption")
            packet = root["/Metadata"].get_data()
            xml = ET.fromstring(packet)
            namespace = "{http://www.aiim.org/pdfa/ns/id/}"
            require(xml.find(".//" + namespace + "part").text == profile[0], stem + " part")
            require(xml.find(".//" + namespace + "conformance").text == profile[1], stem + " level")
            require(b"xmlns:pdfaid=" in packet, stem + " identification namespace prefix")
            intent = root["/OutputIntents"][0].get_object()
            icc = intent["/DestOutputProfile"]
            require(intent["/S"] == "/GTS_PDFA1" and icc["/N"] == 3 and icc.get_data()[36:40] == b"acsp", stem + " RGB ICC intent")
            # Embedded font maps can represent shaped fi ligatures as the equivalent U+FB01.
            text = unicodedata.normalize("NFKC", reader.pages[0].extract_text())
            require("caf\u00e9" in text and "r\u00e9sum\u00e9" in text and "office" in text, stem + " Unicode extraction")
            for font in fonts(reader.pages[0]["/Resources"]):
                require("/ToUnicode" in font, stem + " ToUnicode")
                descendant = font["/DescendantFonts"][0].get_object() if "/DescendantFonts" in font else font
                descriptor = descendant["/FontDescriptor"]
                require(any(key in descriptor for key in ("/FontFile", "/FontFile2", "/FontFile3")), stem + " embedded font")
            if profile.endswith("A"):
                require("/StructTreeRoot" in root and root["/MarkInfo"]["/Marked"] and root["/Lang"] == "en-US", stem + " tags and language")
                require("/ParentTree" in root["/StructTreeRoot"], stem + " parent tree")
            associated = root.get("/AF", [])
            require(len(associated) == (5 if profile.startswith("3") else 0), stem + " attachment count")
            for item in associated:
                specification = item.get_object()
                relationship = str(specification["/AFRelationship"])[1:]
                require(specification["/UF"] == relationship + "-caf\u00e9.xml", stem + " Unicode attachment name")
                embedded = specification["/EF"]["/F"]
                require(embedded.get_data() == ("<data>" + relationship + "</data>").encode(), stem + " exact payload")
                require(embedded["/Subtype"] == "/application/xml" and "/ModDate" in embedded["/Params"], stem + " associated file metadata")
            with pymupdf.open(path) as rendered:
                pixels = rendered[0].get_pixmap(alpha=False).samples
                require(len(pixels) == len(reference_pixels), stem + " rendered geometry")
                require(max(abs(a - b) for a, b in zip(pixels, reference_pixels)) <= 2, stem + " rendering within 2/255 AA tolerance")
    reference.close()
    for profile in ("2A", "2U"):
        for mode in ("continuous", "original"):
            path = directory / f"merged-PdfA{profile}-{mode}.pdf"
            reader = PdfReader(path)
            require(len(reader.pages) == 2, str(path) + " merged pages")
            for index, page in enumerate(reader.pages):
                expected = f"Page {index + 1}" if mode == "continuous" else "Page 1"
                require(expected in page.extract_text(), str(path) + " page numbering " + expected)
            validation = json.loads(path.with_suffix(".json").read_text())["report"]["jobs"][0]["validationResult"][0]
            require(validation["compliant"], str(path) + " merged compliance")


def negative_fixtures(directory):
    jar = Path(os.environ["FOSSPDF_VERAPDF_JAR"])
    require(sha256(jar) == JAR_SHA256, "pinned validator CLI")
    negative = directory / "expected-failures"
    negative.mkdir(exist_ok=True)
    from pypdf.generic import NameObject
    for scenario, profile in (("output-intent", "2B"), ("font", "2B"), ("unicode", "2U"), ("tags", "3A"), ("encryption", "2B"), ("identification", "2B"), ("associated-file", "3U")):
        writer = PdfWriter(clone_from=directory / f"PdfA{profile}-default.pdf")
        root = writer.root_object
        if scenario == "output-intent":
            del root["/OutputIntents"]
        elif scenario in ("font", "unicode"):
            for font in fonts(writer.pages[0]["/Resources"]):
                if scenario == "unicode":
                    font.pop(NameObject("/ToUnicode"), None)
                else:
                    descendant = font["/DescendantFonts"][0].get_object() if "/DescendantFonts" in font else font
                    descriptor = descendant["/FontDescriptor"]
                    for key in ("/FontFile", "/FontFile2", "/FontFile3"):
                        descriptor.pop(NameObject(key), None)
        elif scenario == "tags":
            del root["/StructTreeRoot"]
        elif scenario == "encryption":
            writer.encrypt("fixture-password", algorithm="AES-256")
        elif scenario == "associated-file":
            del root["/AF"][0].get_object()["/AFRelationship"]
        else:
            del root["/Metadata"]
        path = negative / (scenario + ".pdf")
        writer.write(path)
        process = subprocess.run(["java", "-Xmx512m", "-jar", str(jar), "--format", "json", "--maxfailuresdisplayed", "5", "--flavour", profile.lower(), str(path)], capture_output=True, text=True, timeout=120)
        (negative / (scenario + ".json")).write_text(process.stdout, encoding="utf-8")
        report = json.loads(process.stdout)["report"]
        results = report["jobs"][0].get("validationResult", [])
        require((bool(results) and not results[0]["compliant"]) or report["batchSummary"]["failedEncryptedJobs"] > 0, scenario + " must fail conformance")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    inspect(args.directory)
    negative_fixtures(args.directory)
    (args.directory / "independent-inspection.json").write_text(json.dumps({"passed_checks": checks, "positive_profiles": 6, "optimization_modes": 4, "expected_failures": 7}, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {checks} independent PDF/A checks across six profiles and four optimization modes; seven corrupted files rejected.")
