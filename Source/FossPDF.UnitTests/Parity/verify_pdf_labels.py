"""Inspect label example PDFs using the CI-pinned pypdf and PyMuPDF readers."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path

import fitz
from pypdf import PdfReader


def inspect(root):
    results = {}
    for name, size, page_count in (
        ("avery-5160-skip-5.pdf", (612, 792), 2),
        ("avery-5160-calibration.pdf", (612, 792), 1),
        ("thermal-4x6.pdf", (288, 432), 2),
    ):
        path = root / name
        reader = PdfReader(path, strict=True)
        assert len(reader.pages) == page_count, name
        assert all((float(p.mediabox.width), float(p.mediabox.height)) == size for p in reader.pages), name
        text = [p.extract_text() for p in reader.pages]
        if name == "avery-5160-skip-5.pdf":
            assert [p.count("Sample Recipient") for p in text] == [25, 2]
            for item in range(1, 28):
                page_index = 0 if item <= 25 else 1
                token = f"Sample Recipient {item:02}"
                assert token in text[page_index] and token not in text[1 - page_index], token
            with fitz.open(path) as pdf:
                for page_index, page in enumerate(pdf):
                    image = page.get_pixmap(matrix=fitz.Matrix(1, 1), alpha=False)
                    occupied = range(5, 30) if page_index == 0 else range(2)
                    for slot in range(30):
                        x, y = 13.5 + slot % 3 * 198, 36 + slot // 3 * 72
                        # Check the entire interior of every skipped/unused physical label.
                        if slot not in occupied:
                            for pixel_y in range(int(y) + 1, int(y + 72) - 1):
                                for pixel_x in range(int(x) + 1, int(x + 189) - 1):
                                    assert image.pixel(pixel_x, pixel_y) == (255, 255, 255), (page_index, slot)
                    for item in range(1, 28):
                        expected_page = 0 if item <= 25 else 1
                        if page_index != expected_page:
                            continue
                        slot = item + 4 if page_index == 0 else item - 26
                        x, y = 13.5 + slot % 3 * 198, 36 + slot // 3 * 72
                        bounds = page.search_for(f"Sample Recipient {item:02}")
                        assert len(bounds) == 1, item
                        box = bounds[0]
                        assert x <= box.x0 < box.x1 <= x + 189 and y <= box.y0 < box.y1 <= y + 72, (item, box)
        elif name == "avery-5160-calibration.pdf":
            assert text[0].count("Slot ") == 30
            assert all(f"Slot {slot}" in text[0] for slot in range(1, 31))
        else:
            assert "Sample Recipient 01" in text[0] and "Sample Recipient 02" in text[1]
        results[name] = {"pages": page_count, "page_size_points": size,
                         "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    return {"readers": {name: importlib.metadata.version(name) for name in ("pypdf", "PyMuPDF")},
            "checks": "Page dimensions, ordered recipients, physical text bounds and blank label interiors",
            "documents": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    result = inspect(args.directory)
    output = json.dumps(result, indent=2) + "\n"
    (args.directory / "inspection.json").write_text(output, encoding="utf-8")
    print(output, end="")
