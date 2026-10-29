"""Inspect invoice packaging with independent readers and reject deliberate corruptions."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
from xml.etree import ElementTree as ET

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import DecodedStreamObject, NameObject
from install_mustang import JAR_SHA256, sha256

FX = "{urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#}"
checks = 0


def require(condition, message):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(message)


def run_mustang(path, report_path):
    jar = Path(os.environ["FOSSPDF_MUSTANG_JAR"])
    require(sha256(jar) == JAR_SHA256, "pinned Mustang CLI")
    process = subprocess.run(["java", "-Xmx512m", "-Dfile.encoding=UTF-8", "-jar", str(jar),
                              "--disable-file-logging", "--action", "validate", "--source", str(path.resolve())],
                             cwd=report_path.parent, capture_output=True, timeout=120)
    report_path.write_bytes(process.stdout)
    require(process.returncode in (0, 255, -1, 4294967295), "Mustang process completion")
    return ET.fromstring(process.stdout)


def inspect(directory):
    payload = (directory / "invoice.xml").read_bytes()
    with pymupdf.open(directory / "reference.pdf") as reference:
        expected_pixels = reference[0].get_pixmap(alpha=False).samples
    outputs = sorted(p for p in directory.glob("*.pdf") if p.name not in ("reference.pdf", "combined-ua1.pdf"))
    require(len(outputs) == 12, "two named releases, all three archival levels and four optimization modes on 3b")
    for path in outputs:
        stem = path.stem
        reader = PdfReader(path)
        root = reader.trailer["/Root"]
        associated = root["/AF"]
        require(len(associated) == 1, stem + " one associated invoice")
        file = associated[0].get_object()
        require(file["/AFRelationship"] == "/Alternative", stem + " Alternative relationship")
        require(file["/UF"] == file["/F"] == "factur-x.xml", stem + " uniform filename")
        embedded = file["/EF"]["/F"]
        require(embedded.get_data() == payload, stem + " byte-exact payload")
        require(file["/EF"].raw_get("/F") == file["/EF"].raw_get("/UF"), stem + " shared embedded stream")
        require(embedded["/Subtype"] == "/text/xml" and "/ModDate" in embedded["/Params"], stem + " MIME and date")
        names = root["/Names"]["/EmbeddedFiles"]["/Names"]
        require(names[0] == "factur-x.xml" and names[1] == associated[0], stem + " shared name-tree/AF file specification")
        xml = ET.fromstring(root["/Metadata"].get_data())
        for key, value in {"DocumentFileName": "factur-x.xml", "DocumentType": "INVOICE", "Version": "1.0", "ConformanceLevel": "EN 16931"}.items():
            require(xml.find(".//" + FX + key).text == value, stem + " XMP " + key)
        require(xml.find(".//{http://www.aiim.org/pdfa/ns/schema#}namespaceURI").text == FX[1:-1], stem + " declared extension schema")
        archival = json.loads((directory / (stem + "-verapdf.json")).read_text(encoding="utf-8"))["report"]["jobs"][0]["validationResult"][0]
        level = stem.split("-")[1][4:].lower()
        require(archival["compliant"] and archival["profileName"].lower() == "pdf/a-" + level + " validation profile", stem + " exact archival profile")
        for suffix in ("-xml.xml", "-package.xml"):
            report = ET.parse(directory / (stem + suffix)).getroot()
            require(report.find("summary").get("status") == "valid", stem + suffix + " overall status")
            require(report.find("xml/summary").get("status") == "valid", stem + suffix + " XML status")
            require(report.find("xml/info/validator").get("version") == "2.26.0", stem + " engine version")
            if suffix == "-package.xml":
                require(report.find("pdf/summary").get("status") == "valid", stem + " PDF package status")
        if stem.endswith("linearized"):
            require(subprocess.run([shutil.which("qpdf"), "--check-linearization", str(path)], capture_output=True).returncode == 0, stem + " hint tables")
        with pymupdf.open(path) as rendered:
            require(rendered.embfile_get("factur-x.xml") == payload, stem + " second-reader extraction")
            pixels = rendered[0].get_pixmap(alpha=False).samples
            require(len(pixels) == len(expected_pixels) and max(abs(a-b) for a, b in zip(pixels, expected_pixels)) <= 2, stem + " unchanged painting")
            require("FP-2026-001" in rendered[0].get_text() and "14.28" in rendered[0].get_text(), stem + " visible invoice")
    with pymupdf.open(directory / "combined-ua1.pdf") as combined:
        require(combined.embfile_get("factur-x.xml") == payload, "combined PDF/UA invoice extraction")
        require(combined.embfile_get("support.csv") == b"item,net\nreport,12.00\n", "additional associated file retained")
        require("Example accounting system" in combined.get_xml_metadata(), "custom metadata retained")
    ua = json.loads((directory / "combined-ua1.json").read_text(encoding="utf-8"))["report"]["jobs"][0]["validationResult"][0]
    require(ua["compliant"], "combined PDF/UA-1 machine checks")


def negative_fixtures(directory):
    negative = directory / "expected-failures"
    negative.mkdir(exist_ok=True)
    source = directory / "FacturX1092En16931-PdfA3B-default.pdf"
    for scenario in ("relationship", "filename", "xmp-profile"):
        writer = PdfWriter(clone_from=source)
        root = writer.root_object
        if scenario == "relationship":
            root["/AF"][0].get_object()[NameObject("/AFRelationship")] = NameObject("/Unspecified")
        else:
            packet = root["/Metadata"].get_data()
            packet = packet.replace(b"factur-x.xml", b"wrong-name.xml") if scenario == "filename" else packet.replace(b"EN 16931", b"BASIC")
            metadata = DecodedStreamObject()
            metadata.set_data(packet)
            metadata[NameObject("/Type")] = NameObject("/Metadata")
            metadata[NameObject("/Subtype")] = NameObject("/XML")
            root[NameObject("/Metadata")] = writer._add_object(metadata)
        path = negative / (scenario + ".pdf")
        writer.write(path)
        report = run_mustang(path, negative / (scenario + ".xml"))
        strict = PdfReader(path).trailer["/Root"]
        properties = ET.fromstring(strict["/Metadata"].get_data())
        strict_valid = strict["/AF"][0].get_object()["/AFRelationship"] == "/Alternative" and all(
            properties.find(".//" + FX + key).text == value for key, value in
            {"DocumentFileName": "factur-x.xml", "ConformanceLevel": "EN 16931"}.items())
        require(not strict_valid, scenario + " rejected by strict independent packaging checks")
        # Preserve the pinned validator's actual result, including its two known blind spots.
        expected_mustang = "invalid" if scenario == "filename" else "valid"
        require(report.find("pdf/summary").get("status") == expected_mustang, scenario + " pinned Mustang coverage")
    payload = (directory / "invoice.xml").read_bytes()
    for scenario, invalid in (("schema", payload.replace(b">EUR</ram:InvoiceCurrencyCode>", b">INVALID</ram:InvoiceCurrencyCode>")),
                              ("totals", payload.replace(b"<ram:GrandTotalAmount>14.28", b"<ram:GrandTotalAmount>99.99"))):
        path = negative / (scenario + ".xml")
        path.write_bytes(invalid)
        report = run_mustang(path, negative / (scenario + "-report.xml"))
        require(report.find("xml/summary").get("status") == "invalid", scenario + " invoice XML rejected")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    inspect(directory)
    negative_fixtures(directory)
    (directory / "independent-inspection.json").write_text(json.dumps({"passed_checks": checks,
        "invoice_releases": 2, "archival_levels": 3, "positive_packages": 13, "expected_failures": 5,
        "mustang_rejections": 3, "additional_structural_rejections": 2}, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {checks} independent invoice checks; three Mustang failures and two structural failures identified.")
