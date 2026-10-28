"""Independent FP-305 inspection. Identity-filter rendering is optional via MuPDF."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
from xml.etree import ElementTree as ET

import pypdf
from pypdf import PdfReader


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--mupdf", action="store_true")
    args = parser.parse_args()
    directory = args.directory
    catalog_path = Path(__file__).resolve().parents[3] / "docs/feature-parity/acceptance-fixtures.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
    fixture = next(item for item in catalog["fixtures"] if item["id"] == "PDF-305")["input"]
    passwords = next(item for item in catalog["fixtures"] if item["id"] == "PDF-304")["input"]
    checks, files = [], []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    def facts(document):
        return [(list(page.mediabox), page.get_contents().get_data()) for page in document.pages]

    source = PdfReader(directory / "source.pdf", strict=True)
    expected_pages = facts(source)
    expected_data = fixture["attachment"]["utf8"].encode("utf-8")
    for relationship in ("Data", "Source", "Alternative", "Supplement", "Unspecified"):
        path = directory / f"attachment-{relationship}.pdf"
        files.append(path)
        reader = PdfReader(path, strict=True)
        require(facts(reader) == expected_pages, f"{relationship}: page boxes and painting are unchanged")
        root = reader.trailer["/Root"]
        names = root["/Names"]["/EmbeddedFiles"]["/Names"]
        require(len(names) == 2 and names[0] == fixture["attachment"]["name"], f"{relationship}: one expected attachment key")
        specification = names[1].get_object()
        require(specification["/UF"] == fixture["attachment"]["name"], f"{relationship}: display name is preserved")
        embedded = specification["/EF"]["/UF"]
        require(embedded.get_data() == expected_data, f"{relationship}: independent extraction returns exact UTF-8 bytes")
        require(embedded["/Subtype"] == "/" + fixture["attachment"]["media_type"], f"{relationship}: MIME type is preserved")
        require(specification["/AFRelationship"] == "/" + relationship, f"{relationship}: associated-file role is correct")
        require(root["/AF"][0].idnum == names[1].idnum, f"{relationship}: association and name tree reference the same specification")
        xml = ET.fromstring(root["/Metadata"].get_data())
        property_name = "{" + fixture["xmp"]["namespace"] + "}" + fixture["xmp"]["key"]
        require(xml.find(".//" + property_name).text == fixture["xmp"]["value"], f"{relationship}: declared XMP property is preserved")

    limitations = []
    for path in sorted(directory.glob("encrypted-*.pdf")):
        files.append(path)
        reader = PdfReader(path, strict=True)
        require(reader.decrypt(passwords["user_password"]) == 1, path.name + ": user role authenticates")
        root = reader.trailer["/Root"]
        names = root["/Names"]["/EmbeddedFiles"]["/Names"]
        specification = names[1].get_object()
        require(specification["/EF"]["/UF"].get_data() == "invoice data: \u03b1\u03b2\u03b3".encode("utf-8"), path.name + ": attachment decrypts exactly")
        if "-True" in path.stem:
            xml = ET.fromstring(root["/Metadata"].get_data())
            require(xml.find(".//{urn:packing}Secret").text == "private-extension-value", path.name + ": protected XMP decrypts exactly")
            require(b"private-extension-value" not in path.read_bytes(), path.name + ": extension is absent in plaintext")
        else:
            # pypdf 6.21.2 does not decode /Crypt /Identity streams correctly. Do not
            # claim that reading path passed; the optional second reader checks it.
            limitations.append(path.name + ": identity-filter XMP requires the optional MuPDF check")

    renderer = None
    if args.mupdf:
        import pymupdf
        renderer = importlib.metadata.version("PyMuPDF")
        for path in files:
            with pymupdf.open(path) as document:
                if document.needs_pass:
                    require(bool(document.authenticate(passwords["user_password"])), path.name + ": MuPDF authenticates")
                xml = ET.fromstring(document.get_xml_metadata())
                if path.name.startswith("encrypted-"):
                    require(xml.find(".//{urn:packing}Secret").text == "private-extension-value", path.name + ": MuPDF reads XMP exactly")
                else:
                    with pymupdf.open(directory / "source.pdf") as original:
                        require(all(page.get_pixmap().samples == original[index].get_pixmap().samples
                                    for index, page in enumerate(document)), path.name + ": independent raster pages equal the source")

    report = {"pypdf": pypdf.__version__, "pymupdf": renderer, "checks": checks, "limitations": limitations,
              "sha256": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in files}}
    filename = "independent-rendering.json" if args.mupdf else "independent-validation.json"
    (directory / filename).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"FP-305: {len(checks)} independent checks passed; {len(limitations)} documented pypdf reading limitations.")


if __name__ == "__main__":
    main()
