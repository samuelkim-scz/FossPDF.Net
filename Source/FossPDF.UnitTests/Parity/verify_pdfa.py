"""Independently verify FP-402 PDF/A artifacts with a pinned veraPDF and pypdf; validation only, never shipped with FossPDF."""
import argparse
import hashlib
import io
import json
import re
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

from pypdf import PdfReader
from pypdf.generic import ArrayObject, IndirectObject

# veraPDF is pinned by version, download URL and installer digest. The CLI jar is extracted
# from the installer's CLI pack; the installer itself is never executed.
VERAPDF_VERSION = "1.30.3"
VERAPDF_INSTALLER_URL = (
    "https://software.verapdf.org/releases/1.30/"
    "verapdf-greenfield-1.30.3-installer.zip"
)
VERAPDF_INSTALLER_SHA256 = (
    "B512BF24945503A33FC10738ECFFC1F2DCCAAF8593A448E94EE9AC86A9559A59"
)

LEVELS = ["2a", "2b", "2u", "3a", "3b", "3u"]
ATTACHMENT_LEVELS = ["3a", "3b", "3u"]
PDFA_NS = "http://www.aiim.org/pdfa/ns/id/"
RDF_NS = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"


def resolve(value):
    return value.get_object() if isinstance(value, IndirectObject) else value


def name(value):
    text = str(resolve(value)) if value is not None else ""
    return text[1:] if text.startswith("/") else text


def sha256(path):
    digest = hashlib.sha256()

    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)

    return digest.hexdigest().upper()


def ensure_verapdf(cache):
    jar = cache / f"verapdf-cli-{VERAPDF_VERSION}.jar"

    if not jar.exists():
        cache.mkdir(parents=True, exist_ok=True)
        installer = cache / f"verapdf-greenfield-{VERAPDF_VERSION}-installer.zip"

        if not installer.exists():
            print(f"Downloading pinned veraPDF {VERAPDF_VERSION} installer")
            urllib.request.urlretrieve(VERAPDF_INSTALLER_URL, installer)

        digest = sha256(installer)

        if digest != VERAPDF_INSTALLER_SHA256:
            raise AssertionError(
                "veraPDF installer digest mismatch: "
                f"expected {VERAPDF_INSTALLER_SHA256}, got {digest}"
            )

        with zipfile.ZipFile(installer) as archive:
            installer_jar = archive.read(
                f"verapdf-greenfield-{VERAPDF_VERSION}/"
                f"verapdf-izpack-installer-{VERAPDF_VERSION}.jar"
            )

        with zipfile.ZipFile(io.BytesIO(installer_jar)) as archive:
            pack = archive.read("resources/packs/pack-veraPDF CLI")

        jar.write_bytes(trim_jar(pack))
        print(f"Extracted veraPDF {VERAPDF_VERSION} CLI to {jar}")

    return ["java", "-jar", str(jar)]


def trim_jar(data):
    """Drop the IzPack launcher script appended after the CLI fat jar's end record."""
    marker = b"PK\x05\x06"
    index = data.rfind(marker, max(0, len(data) - 65557))

    if index < 0:
        raise AssertionError("The veraPDF CLI pack has no ZIP end record")

    comment_length = int.from_bytes(data[index + 20:index + 22], "little")
    return data[:index + 22 + comment_length]


def verapdf_command(args, cache):
    if args.verapdf:
        if args.verapdf.lower().endswith((".bat", ".cmd")):
            return ["cmd.exe", "/c", args.verapdf]

        return [args.verapdf]

    return ensure_verapdf(cache)


def run_verapdf(command, flavour, path, output_format, reports, suffix):
    result = subprocess.run(
        command + ["--flavour", flavour, "--format", output_format, str(path)],
        capture_output=True,
        text=True,
        check=False,
    )
    output = result.stdout + result.stderr
    (reports / f"verapdf-{path.stem}-{suffix}").write_text(output, encoding="utf-8")

    if result.returncode != 0:
        raise AssertionError(
            f"veraPDF rejected {path.name} as {flavour}:\n{output.strip()}"
        )

    if output_format == "text" and not output.lstrip().startswith("PASS"):
        raise AssertionError(
            f"veraPDF did not report compliance for {path.name} as {flavour}:\n"
            + output.strip()
        )

    return output.strip()


def read_xmp(reader):
    metadata = resolve(reader.trailer["/Root"].get("/Metadata"))

    if metadata is None:
        raise AssertionError("The document has no XMP metadata stream")

    return metadata.get_data().decode("utf-8", errors="replace")


def read_structure_tree(reader):
    catalog = resolve(reader.trailer["/Root"])
    root = resolve(catalog.get("/StructTreeRoot"))

    if root is None:
        raise AssertionError("The document has no structure tree root")

    elements = []

    def read(element):
        role = name(element.get("/S"))
        elements.append(role)

        kind = resolve(element.get("/K"))
        items = list(kind) if isinstance(kind, ArrayObject) else ([kind] if kind is not None else [])

        for item in items:
            item = resolve(item)

            if hasattr(item, "get") and "/S" in item:
                read(item)

    document = resolve(root.get("/K"))
    read(document)
    return catalog, root, elements


def iter_fonts(page):
    resources = resolve(page.get("/Resources"))
    fonts = resolve(resources.get("/Font")) if resources is not None else None

    if fonts is None:
        return

    for value in fonts.values():
        font = resolve(value)
        yield font

        descendants = resolve(font.get("/DescendantFonts"))

        if isinstance(descendants, ArrayObject):
            for descendant in descendants:
                yield resolve(descendant)


def require_font_mappings(reader, message):
    for page in reader.pages:
        for font in iter_fonts(page):
            if resolve(font.get("/ToUnicode")) is None:
                raise AssertionError(message + ": a font has no /ToUnicode mapping")

            descriptor = resolve(font.get("/FontDescriptor"))

            if descriptor is not None and resolve(descriptor.get("/FontFile2")) is not None:
                continue

            if resolve(font.get("/Subtype")) == "/Type0":
                continue

            raise AssertionError(message + ": a font is not embedded")


def read_attachments(reader):
    catalog = resolve(reader.trailer["/Root"])
    names = resolve(catalog.get("/Names"))
    embedded = resolve(names.get("/EmbeddedFiles")) if names is not None else None
    entries = resolve(embedded.get("/Names")) if embedded is not None else None

    if entries is None:
        return catalog, []

    attachments = []

    for index in range(0, len(entries) - 1, 2):
        key = str(resolve(entries[index]))
        filespec = resolve(entries[index + 1])
        files = resolve(filespec.get("/EF"))
        stream = resolve(files.get("/F"))
        parameters = resolve(stream.get("/Params"))
        attachments.append(
            {
                "key": key,
                "name": str(resolve(filespec.get("/UF"))),
                "relationship": name(filespec.get("/AFRelationship")),
                "mime": name(stream.get("/Subtype")),
                "data": stream.get_data(),
                "mod_date": resolve(parameters.get("/ModDate")) if parameters is not None else None,
                "filespec": filespec,
            }
        )

    return catalog, attachments


def verify_level(path, level, fixture, reports):
    checks = []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    reader = PdfReader(path, strict=True)

    require(not reader.is_encrypted, f"PDF/A-{level}: the document is not encrypted")
    require(path.read_bytes().startswith(b"%PDF-1.7"),
            f"PDF/A-{level}: the header is PDF 1.7")

    xmp = read_xmp(reader)
    part = "3" if level.startswith("3") else "2"
    conformance = level[1].upper()

    require(re.search(rf"<pdfaid:part>\s*{part}\s*</pdfaid:part>", xmp) is not None,
            f"PDF/A-{level}: the XMP packet declares part {part}")
    require(re.search(rf"<pdfaid:conformance>\s*{conformance}\s*</pdfaid:conformance>", xmp) is not None,
            f"PDF/A-{level}: the XMP packet declares conformance {conformance}")
    require(PDFA_NS in xmp, f"PDF/A-{level}: the pdfaid namespace is declared")
    require(re.search(r"<dc:title>", xmp) is not None,
            f"PDF/A-{level}: the XMP packet carries the document title")

    catalog = resolve(reader.trailer["/Root"])
    intents = resolve(catalog.get("/OutputIntents"))

    require(intents is not None and len(intents) > 0,
            f"PDF/A-{level}: an output intent is present")

    intent = resolve(intents[0])
    require(name(intent.get("/S")) == "GTS_PDFA1",
            f"PDF/A-{level}: the output intent subtype is GTS_PDFA1")

    profile = resolve(intent.get("/DestOutputProfile"))
    require(profile is not None, f"PDF/A-{level}: the output intent embeds a profile")
    icc = profile.get_data()
    require(len(icc) > 128 and b"acsp" in icc[:128],
            f"PDF/A-{level}: the embedded output intent is an ICC profile")

    require(bool(resolve(catalog.get("/MarkInfo")).get("/Marked")),
            f"PDF/A-{level}: the document is tagged")
    require(name(catalog.get("/Lang")) == fixture["language"],
            f"PDF/A-{level}: the catalog declares the requested language")
    require(bool(resolve(catalog.get("/ViewerPreferences")).get("/DisplayDocTitle")),
            f"PDF/A-{level}: the title is configured for display")

    catalog, root, roles = read_structure_tree(reader)
    require(roles == ["Document", "H1", "P", "Figure", "P"],
            f"PDF/A-{level}: structure elements keep the logical reading order")
    require(int(root["/ParentTreeNextKey"]) > 0,
            f"PDF/A-{level}: the parent tree declares the next free key")

    figure = None
    elements = []

    def find(element):
        nonlocal figure
        elements.append(element)

        if name(element.get("/S")) == "Figure":
            figure = element

        kind = resolve(element.get("/K"))
        items = list(kind) if isinstance(kind, ArrayObject) else ([kind] if kind is not None else [])

        for item in items:
            item = resolve(item)

            if hasattr(item, "get") and "/S" in item:
                find(item)

    find(resolve(root.get("/K")))
    figure_alt = next(item["alt"] for item in fixture["content"] if item["role"] == "Figure")
    require(figure is not None and str(resolve(figure.get("/Alt"))) == figure_alt,
            f"PDF/A-{level}: the figure alternative text is exact")

    require_font_mappings(reader, f"PDF/A-{level}: every font has an embedded Unicode mapping")

    extracted = "\n".join(page.extract_text() for page in reader.pages)
    heading = next(item["text"] for item in fixture["content"] if item["role"] == "H1")
    require(heading in extracted, f"PDF/A-{level}: an independent reader extracts the heading text")

    catalog, attachments = read_attachments(reader)

    if level in ATTACHMENT_LEVELS:
        attachment_path = path.with_name(path.stem + "-attachment.pdf")

        if attachment_path.exists():
            attachment_reader = PdfReader(attachment_path, strict=True)
            attachment_catalog, files = read_attachments(attachment_reader)
            require(len(files) == 1, f"PDF/A-{level} attachment: the name tree holds one file")
            require(files[0]["mod_date"] is not None,
                    f"PDF/A-{level} attachment: the file declares a modification date")
            require(files[0]["mime"] not in ("", "None"),
                    f"PDF/A-{level} attachment: the file declares a media type")
            require(files[0]["relationship"] not in ("", "Unspecified"),
                    f"PDF/A-{level} attachment: the file declares a relationship")
            require(resolve(attachment_catalog.get("/AF")) is not None,
                    f"PDF/A-{level} attachment: the file is listed in the associated-files array")
            require(files[0]["data"], f"PDF/A-{level} attachment: the embedded bytes are present")

    require(not (path.parent / f"pdfa-{level}-attachment.pdf").exists()
            or level in ATTACHMENT_LEVELS,
            f"PDF/A-{level}: embedded files are only accepted for PDF/A-3")

    return checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path,
                        help="parity-artifacts/ACC-402 produced by the ACC-402 parity fixture")
    parser.add_argument("--verapdf", default=None,
                        help="existing veraPDF executable; otherwise the pinned release is downloaded")
    parser.add_argument("--cache", type=Path, default=Path.home() / ".cache" / "fosspdf-verapdf",
                        help="cache for the pinned veraPDF CLI jar")
    parser.add_argument("--reports", type=Path, default=None,
                        help="directory for text and MRR reports (default: <directory>/reports)")
    parser.add_argument("--catalog", type=Path,
                        default=Path(__file__).resolve().parents[3] /
                        "docs/feature-parity/acceptance-fixtures.json")
    parser.add_argument("--skip-verapdf", action="store_true",
                        help="run only the independent pypdf checks")
    args = parser.parse_args()

    reports = args.reports or (args.directory / "reports")
    reports.mkdir(parents=True, exist_ok=True)

    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    source = next(item for item in catalog["fixtures"] if item["id"] == "ACC-401")["input"]
    fixture = {
        "language": source["language"],
        "content": source["content"],
    }

    command = None if args.skip_verapdf else verapdf_command(args, args.cache)
    checks = []

    for level in LEVELS:
        path = args.directory / f"pdfa-{level}.pdf"

        if not path.exists():
            raise AssertionError(f"Missing PDF/A-{level} artifact: {path}")

        checks.extend(verify_level(path, level, fixture, reports))

        if command is not None:
            summary = run_verapdf(command, level, path, "text", reports, "txt")
            print(summary)
            run_verapdf(command, level, path, "mrr", reports, "mrr.xml")
            checks.append(f"veraPDF {VERAPDF_VERSION}: PDF/A-{level} ({path.name}) is compliant")

    for level in ATTACHMENT_LEVELS:
        path = args.directory / f"pdfa-{level}-attachment.pdf"

        if not path.exists():
            raise AssertionError(f"Missing attachment-bearing PDF/A-{level} artifact: {path}")

        if command is not None:
            summary = run_verapdf(command, level, path, "text", reports, "txt")
            print(summary)
            run_verapdf(command, level, path, "mrr", reports, "mrr.xml")
            checks.append(
                f"veraPDF {VERAPDF_VERSION}: PDF/A-{level} with an embedded file ({path.name}) is compliant"
            )

    for message in checks:
        print("PASS " + message)

    print(f"PASS ACC-402 ({len(checks)} independent checks)")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("FAIL " + str(error), file=sys.stderr)
        sys.exit(1)
