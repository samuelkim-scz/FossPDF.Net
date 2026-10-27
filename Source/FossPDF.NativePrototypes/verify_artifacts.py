"""Independent FP-002 inspection. No PDF/UA or PDF/A conformance claim."""
import hashlib
import json
from pathlib import Path
import platform
import sys

import pypdf
from pypdf import PdfReader
from pypdf.generic import ContentStream


def require(condition, message):
    if not condition:
        raise AssertionError(message)
    checks.append(message)


def same_reference(left, right):
    return (left.idnum, left.generation) == (right.idnum, right.generation)


def streams(page):
    contents = page.get("/Contents").get_object()
    return [item.get_object().get_data() for item in contents] if isinstance(contents, list) else [contents.get_data()]


def font_hashes(page):
    result = []
    for reference in page["/Resources"].get("/Font", {}).values():
        font = reference.get_object()
        if "/ToUnicode" in font:
            result.append(hashlib.sha256(font["/ToUnicode"].get_data()).hexdigest())
        for item in font.get("/DescendantFonts", [reference]):
            descriptor = item.get_object().get("/FontDescriptor")
            if descriptor:
                descriptor = descriptor.get_object()
                for key in ("/FontFile", "/FontFile2", "/FontFile3"):
                    if key in descriptor:
                        result.append(hashlib.sha256(descriptor[key].get_data()).hexdigest())
    return sorted(result)


def image_count(resources):
    count = 0
    for item in resources.get("/XObject", {}).values():
        obj = item.get_object()
        if obj.get("/Subtype") == "/Image":
            count += 1
        elif obj.get("/Subtype") == "/Form":
            count += image_count(obj.get("/Resources", {}))
    return count


def verify(directory):
    vector = PdfReader(directory / "svg-vector-in-foss-pdf.pdf", strict=True)
    raster = PdfReader(directory / "svg-in-foss-pdf.pdf", strict=True)
    require(len(vector.pages) == 1 and image_count(vector.pages[0]["/Resources"]) == 0,
            "SVG vector output has one page and no raster image XObjects")
    operations = ContentStream(vector.pages[0].get_contents(), vector).operations
    require(any(operator in (b"re", b"l", b"c") for _, operator in operations),
            "SVG vector output contains path painting geometry")
    require(image_count(raster.pages[0]["/Resources"]) > 0, "explicit SVG raster fallback embeds an image")

    reordered = PdfReader(directory / "pdf-ops-reordered.pdf", strict=True)
    require([float(page.mediabox.width) for page in reordered.pages] == [300, 200],
            "imported Skia PDFs retain the declared page order and media boxes")

    before = PdfReader(directory / "tagging-source-foss.pdf", strict=True)
    after = PdfReader(directory / "tagged-structure.pdf", strict=True)
    batches = json.loads((directory / "semantic-batches.json").read_text(encoding="utf-8-sig"))
    catalog = after.trailer["/Root"]
    require(catalog["/MarkInfo"]["/Marked"].value and catalog["/Lang"] == "en-US",
            "tagged catalog declares marked content and document language")
    root = catalog["/StructTreeRoot"]
    document_ref = root["/K"][0]
    document = document_ref.get_object()
    require(document["/S"] == "/Document" and same_reference(document.raw_get("/P"), root.indirect_reference),
            "Document structure element points to the structure tree root")
    nodes = document["/K"]
    numbers = root["/ParentTree"]["/Nums"]
    require(len(before.pages) == len(after.pages) == len(nodes) == len(batches) == 2,
            "both original pages have corresponding ordered semantic nodes")
    for index, (original, tagged, batch, node_ref) in enumerate(zip(before.pages, after.pages, batches, nodes)):
        node = node_ref.get_object()
        require(node["/S"] == "/" + batch["role"] and node["/Lang"] == batch["language"] and
                same_reference(node.raw_get("/P"), document_ref) and
                same_reference(node.raw_get("/Pg"), tagged.indirect_reference) and node["/K"] == 0 and
                tagged["/StructParents"] == numbers[index * 2] == index and
                same_reference(numbers[index * 2 + 1][0], node_ref),
                f"page {index + 1}: role, language, page, MCID and reverse parent-tree association agree")
        operations = ContentStream(tagged.get_contents(), after).operations
        marked = [(operands, operator) for operands, operator in operations if operator in (b"BDC", b"EMC")]
        require(len(marked) == 2 and marked[0][1] == b"BDC" and marked[1][1] == b"EMC" and
                marked[0][0][0] == "/" + batch["role"] and marked[0][0][1]["/MCID"] == 0 and
                marked[0][0][1]["/ActualText"] == batch["text"],
                f"page {index + 1}: balanced marked content declares exact role, MCID and Unicode text")
        require(all(any(data in candidate for candidate in streams(tagged)) for data in streams(original)),
                f"page {index + 1}: original Skia content stream bytes remain intact")
        require(font_hashes(original) and font_hashes(original) == font_hashes(tagged),
                f"page {index + 1}: embedded font and ToUnicode bytes remain intact")
        require(original.extract_text().strip() == tagged.extract_text().strip() == batch["text"],
                f"page {index + 1}: independent Unicode extraction retains the original text")

    assembly = PdfReader(directory / "semantic-batch-assembly.pdf", strict=True)
    require(len(assembly.pages) == 1, "semantic paint batches compose onto a single page")
    page = assembly.pages[0]
    mapping = json.loads((directory / "paint-batches.json").read_text(encoding="utf-8-sig"))
    used_forms = set()
    for batch in mapping:
        operations = ContentStream(page["/Contents"][batch["contentStreamIndex"]], assembly).operations
        invocations = [operands[0] for operands, operator in operations if operator == b"Do"]
        require(len(invocations) == 1 and invocations[0] not in used_forms,
                f"batch {batch['nodeId']}: sidecar identifies a distinct form invocation")
        used_forms.update(invocations)
        form = page["/Resources"]["/XObject"][invocations[0]]
        fragment = PdfReader(directory / batch["source"], strict=True)
        require(form["/Subtype"] == "/Form" and
                all(source in form.get_data() for source in streams(fragment.pages[0])) and
                font_hashes(form) == font_hashes(fragment.pages[0]),
                f"batch {batch['nodeId']}: vector form retains original Skia paint, fonts and Unicode mappings")
    require(assembly.pages[0].extract_text().splitlines() == [batch["text"] for batch in mapping],
            "assembled batches retain both text fragments in declared order")
    tagged_assembly = PdfReader(directory / "semantic-batch-tagged.pdf", strict=True)
    tagged_page = tagged_assembly.pages[0]
    root = tagged_assembly.trailer["/Root"]["/StructTreeRoot"]
    document_ref = root["/K"][0]
    nodes = document_ref.get_object()["/K"]
    numbers = root["/ParentTree"]["/Nums"]
    require(len(nodes) == 2 and len(numbers) == 2 and numbers[0] == tagged_page["/StructParents"] == 0,
            "one-page batch tags share one page-local parent-tree array")
    marked = [(operands, operator) for operands, operator in
              ContentStream(tagged_page.get_contents(), tagged_assembly).operations if operator in (b"BDC", b"EMC")]
    require([operator for _, operator in marked] == [b"BDC", b"EMC", b"BDC", b"EMC"],
            "both form invocations have separate balanced marked-content scopes")
    for index, (batch, node_ref) in enumerate(zip(mapping, nodes)):
        node = node_ref.get_object()
        properties = marked[index * 2][0][1]
        require(node["/S"] == "/" + batch["role"] and node["/K"] == properties["/MCID"] == index and
                properties["/ActualText"] == batch["text"] and
                same_reference(node.raw_get("/Pg"), tagged_page.indirect_reference) and
                same_reference(node.raw_get("/P"), document_ref) and same_reference(numbers[1][index], node_ref),
                f"batch {index}: heading/paragraph has its own MCID and correct forward/reverse association")
    require(all(any(data in candidate for candidate in streams(tagged_page)) for data in streams(page)) and
            tagged_page.extract_text() == page.extract_text(),
            "tagging multiple batches preserves original form invocation bytes and extracted text")


if __name__ == "__main__":
    directory = Path(sys.argv[1]).resolve()
    checks = []
    error = None
    try:
        verify(directory)
    except Exception as exception:
        error = f"{type(exception).__name__}: {exception}"
    result = {"passed": error is None, "inspector": f"pypdf {pypdf.__version__}",
              "platform": platform.platform(), "checks": checks, "error": error}
    (directory / "independent-inspection.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    sys.exit(0 if error is None else 1)
