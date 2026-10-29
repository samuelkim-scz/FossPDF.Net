"""Inspect PDF/UA structure and rendering independently; automated checks do not replace human review."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
from types import SimpleNamespace
import unicodedata
from xml.etree import ElementTree as ET

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf._page import PageObject
from pypdf.generic import ArrayObject, DecodedStreamObject, NameObject

from install_verapdf import JAR_SHA256, sha256
from verify_pdfa import fonts
from verify_pdf_tagging import read_parent_tree, read_structure_tree, resolve

checks = 0


def require(condition, description):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(description)


def content_text(reader, reference):
    stream = reference["/Stm"]
    page = PageObject(reader)
    page[NameObject("/Contents")] = stream
    page[NameObject("/Resources")] = stream["/Resources"]
    return " ".join(unicodedata.normalize("NFKC", page.extract_text()).split())


def structure_transcript(reader, dictionary):
    result = {"role": str(dictionary["/S"])[1:], "alt": dictionary.get("/Alt"), "lang": dictionary.get("/Lang"), "content": []}
    kids = resolve(dictionary.get("/K"))
    for item in kids if isinstance(kids, ArrayObject) else ([] if kids is None else [kids]):
        item = resolve(item)
        if not hasattr(item, "get"):
            continue
        if "/S" in item:
            result["content"].append(structure_transcript(reader, item))
        elif item.get("/Type") == "/MCR":
            text = content_text(reader, item)
            if text:
                result["content"].append({"text": text})
    return result


def inspect(directory):
    qpdf = shutil.which("qpdf")
    require(qpdf is not None, "qpdf is required")
    review_directory = Path(__file__).resolve().parents[1] / "Resources" / "AccessibilityReview"
    review = json.loads((review_directory / "human-review.json").read_text(encoding="utf-8"))
    review_pdf = review_directory / review["pdf"]
    require(sha256(review_pdf) == review["pdf_sha256"], "immutable human-review fixture")
    review_reader = PdfReader(review_pdf)
    _, _, review_root, _ = read_structure_tree(review_reader)
    expected_transcript = structure_transcript(review_reader, review_root["dictionary"])
    with pymupdf.open(directory / "reference.pdf") as reference:
        reference_pixels = [page.get_pixmap(alpha=False).samples for page in reference]
    profiles = ("None", "PdfA2A", "PdfA2B", "PdfA2U", "PdfA3A", "PdfA3B", "PdfA3U")
    for profile in profiles:
        modes = ("default", "compressed", "linearized", "uncompressed") if profile == "None" else ("default", "optimized")
        for mode in modes:
            stem = f"{profile}-{mode}"
            path = directory / (stem + ".pdf")
            machine = json.loads((directory / (stem + "-ua1.json")).read_text(encoding="utf-8"))["report"]["jobs"][0]["validationResult"][0]
            require(machine["compliant"] and machine["details"]["failedChecks"] == 0, stem + " PDF/UA machine checks")
            require(machine["profileName"] == "PDF/UA-1 validation profile", stem + " exact profile")
            if profile != "None":
                archival = json.loads((directory / (stem + "-pdfa.json")).read_text(encoding="utf-8"))["report"]["jobs"][0]["validationResult"][0]
                require(archival["compliant"], stem + " combined archival checks")
                require(archival["profileName"].lower() == "pdf/a-" + profile[4:].lower() + " validation profile", stem + " exact archival profile")
            result = subprocess.run([qpdf, "--check", str(path)], capture_output=True, text=True)
            require(result.returncode == 0, stem + " strict qpdf parsing: " + result.stdout + result.stderr)
            if mode in ("optimized", "linearized"):
                require(subprocess.run([qpdf, "--check-linearization", str(path)], capture_output=True).returncode == 0, stem + " linearization hints")
            reader = PdfReader(path)
            catalog, structure, root, nodes = read_structure_tree(reader)
            require(len(reader.pages) == 2 and not reader.is_encrypted, stem + " page count and encryption")
            require(catalog["/Lang"] == "en-US" and catalog["/ViewerPreferences"]["/DisplayDocTitle"], stem + " language and displayed title")
            require(catalog["/MarkInfo"]["/Marked"], stem + " marked document")
            packet = ET.fromstring(catalog["/Metadata"].get_data())
            require(packet.find(".//{http://www.aiim.org/pdfua/ns/id/}part").text == "1", stem + " PDF/UA identification")
            title = packet.find(".//{http://purl.org/dc/elements/1.1/}title")
            require(title is not None and "accessibility review" in "".join(title.itertext()), stem + " XMP title")
            parent_tree = read_parent_tree(structure)
            for node in nodes:
                expected_parent = node["parent"]["dictionary"] if node["parent"] else structure
                require(node["dictionary"]["/P"] is expected_parent, stem + " complete structure parent chain")
                for marked in node["marked_content"]:
                    form = marked["/Stm"]
                    reverse = resolve(parent_tree[int(form["/StructParents"])])
                    require(resolve(reverse[int(marked["/MCID"])]) is node["dictionary"], stem + " reverse marked-content association")
            headings = [node["role"] for node in nodes if node["role"].startswith("H") and node["role"][1:].isdigit()]
            require(headings.count("H1") == 1 and headings.count("H2") == 2, stem + " heading hierarchy")
            lists = [node for node in nodes if node["role"] == "L"]
            require(len(lists) == 1 and len(lists[0]["children"]) == 2, stem + " two-item list")
            for item in lists[0]["children"]:
                require(item["role"] == "LI" and [child["role"] for child in item["children"]] == ["Lbl", "LBody"], stem + " label and body semantics")
            headers = [node for node in nodes if node["role"] == "TH"]
            require(len(headers) == 2, stem + " table headers")
            for header in headers:
                require(header["dictionary"]["/A"]["/Scope"] == "/Column", stem + " table header scope")
            figures = [node for node in nodes if node["role"] == "Figure"]
            require(len(figures) == 1 and figures[0]["alt"] == "Bar chart: twelve requests completed and four requests awaiting a response.", stem + " chart alternative")
            require(any(node["lang"] == "fr-FR" for node in nodes), stem + " French language override")
            links = [node for node in nodes if node["role"] == "Link"]
            require(len(links) == 3 and all(node["alt"] for node in links), stem + " link descriptions")
            for page in reader.pages:
                require(page["/Tabs"] == "/S", stem + " structure tab order")
                for annotation in page.get("/Annots", []):
                    annotation = annotation.get_object()
                    require(bool(annotation.get("/Contents")), stem + " annotation description")
                    require(resolve(parent_tree[int(annotation["/StructParent"])])["/S"] == "/Link", stem + " annotation parent association")
                for font in fonts(page["/Resources"]):
                    require("/ToUnicode" in font, stem + " Unicode mapping")
            associated = catalog.get("/AF", [])
            require(len(associated) == (1 if profile.startswith("PdfA3") else 0), stem + " associated files")
            for attachment in associated:
                spec = attachment.get_object()
                require(spec["/AFRelationship"] == "/Data" and spec["/EF"]["/F"].get_data() == b"<requests completed='12' awaiting='4'/>", stem + " exact XML payload and relationship")
            transcript = structure_transcript(reader, root["dictionary"])
            require(transcript == expected_transcript, stem + " semantic content matches the exact review fixture")
            serialized = json.dumps(transcript, ensure_ascii=False)
            require("Quarterly service report - repeated header" not in serialized and "Accessibility review fixture - repeated footer" not in serialized, stem + " decorative content excluded")
            require(serialized.index("Quarterly service report") < serialized.index("Key results") < serialized.index("Request status") < serialized.index("Bar chart") < serialized.index("Details and language changes"), stem + " logical reading sequence")
            (directory / (stem + "-structure.json")).write_text(json.dumps(transcript, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            with pymupdf.open(path) as rendered:
                require(len(rendered) == len(reference_pixels), stem + " rendered page count")
                for index, page in enumerate(rendered):
                    pixels = page.get_pixmap(alpha=False).samples
                    require(len(pixels) == len(reference_pixels[index]), stem + " rendered geometry")
                    require(max(abs(a - b) for a, b in zip(pixels, reference_pixels[index])) <= 2, stem + " rendering within 2/255")


def negative_fixtures(directory, source=None):
    jar = Path(os.environ["FOSSPDF_VERAPDF_JAR"])
    require(sha256(jar) == JAR_SHA256, "pinned validator")
    output = directory / "expected-failures"
    output.mkdir(parents=True, exist_ok=True)
    namespaces = {"rdf": "http://www.w3.org/1999/02/22-rdf-syntax-ns#", "dc": "http://purl.org/dc/elements/1.1/", "pdfuaid": "http://www.aiim.org/pdfua/ns/id/",
                  "pdfaid": "http://www.aiim.org/pdfa/ns/id/", "pdfaExtension": "http://www.aiim.org/pdfa/ns/extension/", "pdfaSchema": "http://www.aiim.org/pdfa/ns/schema#", "pdfaProperty": "http://www.aiim.org/pdfa/ns/property#"}
    for prefix, uri in namespaces.items(): ET.register_namespace(prefix, uri)
    scenarios = ("root-parent", "title", "language", "figure-alt", "link-contents", "tab-order", "identification", "unicode", "header-scope")
    for scenario in scenarios:
        writer = PdfWriter(clone_from=source or directory / "None-default.pdf")
        root = writer.root_object
        _, _, document, nodes = read_structure_tree(SimpleNamespace(trailer={"/Root": root}))
        if scenario == "root-parent":
            del document["dictionary"]["/P"]
        elif scenario in ("title", "identification"):
            packet = ET.fromstring(root["/Metadata"].get_data())
            if scenario == "title":
                for description in packet.findall(".//{" + namespaces["rdf"] + "}Description"):
                    title = description.find("{" + namespaces["dc"] + "}title")
                    if title is not None: description.remove(title)
            else:
                packet.find(".//{" + namespaces["pdfuaid"] + "}part").text = "2"
            stream = DecodedStreamObject(); stream.set_data(ET.tostring(packet, encoding="utf-8"))
            stream[NameObject("/Type")] = NameObject("/Metadata"); stream[NameObject("/Subtype")] = NameObject("/XML")
            root[NameObject("/Metadata")] = writer._add_object(stream)
        elif scenario == "language":
            del root["/Lang"]
        elif scenario == "figure-alt":
            for node in nodes:
                if node["role"] == "Figure": node["dictionary"].pop(NameObject("/Alt"), None)
        elif scenario == "header-scope":
            for node in nodes:
                if node["role"] == "TH": node["dictionary"]["/A"].pop(NameObject("/Scope"), None)
        else:
            for page in writer.pages:
                if scenario == "tab-order": page.pop(NameObject("/Tabs"), None)
                elif scenario == "link-contents":
                    for annotation in page.get("/Annots", []): annotation.get_object().pop(NameObject("/Contents"), None)
                else:
                    for font in fonts(page["/Resources"]): font.pop(NameObject("/ToUnicode"), None)
        path = output / (scenario + ".pdf"); writer.write(path)
        process = subprocess.run(["java", "-Xmx512m", "-jar", str(jar), "--format", "json", "--maxfailuresdisplayed", "5", "--flavour", "ua1", str(path)], capture_output=True, text=True, timeout=120)
        (output / (scenario + ".json")).write_text(process.stdout, encoding="utf-8")
        result = json.loads(process.stdout)["report"]["jobs"][0]["validationResult"][0]
        require(not result["compliant"] and result["details"]["failedChecks"] > 0, scenario + " must fail PDF/UA machine checks")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    inspect(args.directory)
    negative_fixtures(args.directory)
    review_path = Path(__file__).resolve().parents[1] / "Resources" / "AccessibilityReview" / "human-review.json"
    review = json.loads(review_path.read_text(encoding="utf-8"))
    (args.directory / "independent-inspection.json").write_text(json.dumps({"passed_checks": checks, "positive_documents": 16, "negative_documents": 9, "human_review": review["status"], "human_review_pdf_sha256": review["pdf_sha256"]}, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {checks} independent checks over sixteen PDF/UA documents; nine deliberate failures rejected. Human review remains separate.")
