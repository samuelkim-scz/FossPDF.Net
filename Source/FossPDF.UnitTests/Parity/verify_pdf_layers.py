"""Independent FP-303 inspection with pypdf; optional MuPDF rendering for visual review.

Install the pinned inspection dependency from
Source/FossPDF.NativePrototypes/requirements-validation.txt. No PDF/A or PDF/UA claim.
"""
import argparse
import hashlib
import importlib.metadata
import json
import zlib
from pathlib import Path

import pypdf
from pypdf import PdfReader
from pypdf.generic import ContentStream


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--render", action="store_true", help="Also verify pixels with an installed PyMuPDF.")
    args = parser.parse_args()
    directory = args.directory
    checks = []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    def resolve(item):
        return item.get_object() if hasattr(item, "get_object") else item

    def forms(page):
        resources = resolve(page["/Resources"])
        return [resolve(item) for item in resolve(resources.get("/XObject", {})).values()]

    def verify_resources(resources):
        for reference in resolve(resources.get("/XObject", {})).values():
            obj = reference.get_object()
            require(obj.get("/Subtype") != "/Image", "Layer fixtures retain vectors without image XObjects")
            if obj.get("/Subtype") == "/Form":
                ContentStream(obj, reader)
                verify_resources(resolve(obj.get("/Resources", {})))

    files = ["source.pdf", "layered.pdf", "state-transform.pdf", "state-clip.pdf", "transparent-layers.pdf"]
    readers = {name: PdfReader(directory / name, strict=True) for name in files}
    source, layered = readers["source.pdf"], readers["layered.pdf"]
    require(len(source.pages) == len(layered.pages) == 2, "Layering retains both source pages")
    for index, (before, after) in enumerate(zip(source.pages, layered.pages)):
        require(list(before.mediabox) == list(after.mediabox), f"Page {index + 1} retains its media box")
        def encoded_streams(page):
            contents = resolve(page.raw_get("/Contents"))
            entries = contents if isinstance(contents, list) else [contents]
            return [(str(resolve(entry).get("/Filter")), resolve(entry)._data) for entry in entries]
        require(all(stream in encoded_streams(after) for stream in encoded_streams(before)),
                f"Page {index + 1} retains the original encoded content bytes and filters")
    require(len(forms(layered.pages[0])) == 2, "Target page references two vector layer forms")
    require(source.pages[1].get_contents().get_data() == layered.pages[1].get_contents().get_data(),
            "Untargeted page keeps its decoded painting instructions")

    identity = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)

    def concatenate(left, right):
        a, b, c, d, e, f = left
        g, h, i, j, k, l = right
        return (a*g + c*h, b*g + d*h, a*i + c*j, b*i + d*j, a*k + c*l + e, b*k + d*l + f)

    for name, reader in readers.items():
        for number in reader.xref.get(0, {}):
            if number == 0:
                continue
            obj = reader.get_object(number)
            if hasattr(obj, "_data") and obj.get("/Filter") == "/FlateDecode":
                decoder = zlib.decompressobj()
                decoder.decompress(obj._data)
                require(decoder.eof and not decoder.unused_data,
                        f"{name}: stream {number} has a complete zlib envelope and checksum")
        for page in reader.pages:
            verify_resources(page["/Resources"].get_object())
            # Parse every page and recursively referenced form with a different PDF backend.
            operations = ContentStream(page.get_contents(), reader).operations
            if name not in ("state-transform.pdf", "state-clip.pdf"):
                continue
            matrix, clipped, stack = identity, False, []
            invocations = 0
            for operands, operator in operations:
                if operator == b"q":
                    stack.append((matrix, clipped))
                elif operator == b"Q":
                    require(bool(stack), f"{name}: graphics-state restores have matching saves")
                    matrix, clipped = stack.pop()
                elif operator == b"cm":
                    matrix = concatenate(matrix, tuple(float(value) for value in operands))
                elif operator in (b"W", b"W*"):
                    clipped = True
                elif operator == b"Do":
                    require(matrix == identity and not clipped,
                            f"{name}: overlay starts with the default transform and clip")
                    invocations += 1
            require(not stack and invocations == 1, f"{name}: balanced graphics state and exactly one overlay")

    transparency = readers["transparent-layers.pdf"]
    transparent_forms = forms(transparency.pages[0])
    require(len(transparent_forms) == 2, "Transparent fixture retains two separate forms")
    alpha = [float(state.get_object()["/ca"])
             for form in transparent_forms
             for state in resolve(resolve(form["/Resources"]).get("/ExtGState", {})).values()
             if "/ca" in state.get_object()]
    require(any(abs(value - 0.5) < 0.01 for value in alpha), "Overlay retains its non-stroking transparency")

    renderer = None
    if args.render:
        import pymupdf
        renderer = importlib.metadata.version("PyMuPDF")

        def color(pixmap, x, y, expected, label, tolerance=2):
            actual = pixmap.pixel(x * 2, y * 2)
            require(all(abs(a - b) <= tolerance for a, b in zip(actual, expected)),
                    f"{label}: pixel {x},{y} is {actual}, expected {expected}")

        for name in files[1:]:
            with pymupdf.open(directory / name) as document:
                for index, page in enumerate(document):
                    pixmap = page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
                    pixmap.save(directory / f"{Path(name).stem}-page-{index + 1}.png")
                    if name.startswith("state-"):
                        color(pixmap, 20, 80, (0, 255, 0), name + " overlay")
                        color(pixmap, 60, 80, (255, 255, 255), name + " no shifted overlay")
                    elif name == "transparent-layers.pdf":
                        color(pixmap, 90, 80, (255, 255, 0), "Visible underlay")
                        color(pixmap, 12, 88, (255, 0, 0), "Original content above underlay")
                        color(pixmap, 20, 80, (127, 128, 0), "Transparent overlay over original")
                        color(pixmap, 32, 80, (127, 255, 0), "Transparent overlay over underlay")
                    elif name == "layered.pdf":
                        color(pixmap, 50, 20, (255, 0, 0) if index == 0 else (0, 0, 255), "Original page color")
                        color(pixmap, 20, 80, (0, 255, 0) if index == 0 else (255, 255, 255), "Selected-page overlay")
                        color(pixmap, 90, 80, (255, 255, 255), "Opaque primary background hides underlay")

    report = {"pypdf": pypdf.__version__, "pymupdf": renderer, "checks": checks,
              "sha256": {name: hashlib.sha256((directory / name).read_bytes()).hexdigest() for name in files}}
    report_name = "independent-rendering.json" if args.render else "independent-validation.json"
    (directory / report_name).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"FP-303: {len(checks)} independent checks passed (pypdf {pypdf.__version__}, renderer {renderer}).")


if __name__ == "__main__":
    main()
