"""Independently verify FP-401 tagged PDF artifacts with pypdf and qpdf; validation only, never shipped with FossPDF."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

from pypdf import PdfReader
from pypdf.generic import ArrayObject, IndirectObject


def resolve(value):
    return value.get_object() if isinstance(value, IndirectObject) else value


def name(value):
    text = str(resolve(value)) if value is not None else ""
    return text[1:] if text.startswith("/") else text


def read_structure_tree(reader):
    catalog = resolve(reader.trailer["/Root"])
    struct_tree_root = resolve(catalog.get("/StructTreeRoot"))

    if struct_tree_root is None:
        raise AssertionError("The document has no structure tree root")

    document = resolve(struct_tree_root["/K"])
    elements = []

    def read_element(dictionary, parent):
        element = {
            "role": name(dictionary.get("/S")),
            "alt": str(resolve(dictionary["/Alt"])) if "/Alt" in dictionary else None,
            "lang": str(resolve(dictionary["/Lang"])) if "/Lang" in dictionary else None,
            "children": [],
            "marked_content": [],
            "object_references": [],
            "parent": parent,
            "dictionary": dictionary,
        }

        kind = resolve(dictionary.get("/K"))
        items = list(kind) if isinstance(kind, ArrayObject) else ([kind] if kind is not None else [])

        for item in items:
            item = resolve(item)
            item_type = name(item.get("/Type")) if hasattr(item, "get") else ""

            if item_type == "MCR":
                element["marked_content"].append(item)
            elif item_type == "OBJR":
                element["object_references"].append(item)
            elif hasattr(item, "get") and "/S" in item:
                child = read_element(item, element)
                element["children"].append(child)
                elements.append(child)

        return element

    root = read_element(document, None)
    elements.insert(0, root)
    return catalog, struct_tree_root, root, elements


def read_parent_tree(struct_tree_root):
    parent_tree = resolve(struct_tree_root.get("/ParentTree"))
    numbers = resolve(parent_tree.get("/Nums")) if parent_tree is not None else None
    result = {}

    if numbers is None:
        return result

    for index in range(0, len(numbers) - 1, 2):
        result[int(numbers[index])] = numbers[index + 1]

    return result


def page_forms(page):
    resources = resolve(page.get("/Resources"))
    xobjects = resolve(resources.get("/XObject")) if resources is not None else None

    if xobjects is None:
        return []

    forms = []

    for item in xobjects.values():
        form = resolve(item)

        if name(form.get("/Subtype")) != "Form":
            continue

        forms.append({
            "dictionary": form,
            "content": form.get_data(),
            "struct_parents": int(form["/StructParents"]) if "/StructParents" in form else None,
        })

    return forms


def find_role_node(element, role):
    if element["role"] == role:
        return element

    for child in element["children"]:
        match = find_role_node(child, role)

        if match is not None:
            return match

    return None


def flatten(elements):
    return [element for element in elements if element["role"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--qpdf", default="qpdf")
    parser.add_argument("--render", action="store_true")
    parser.add_argument("--catalog", type=Path, default=Path(__file__).resolve().parents[3] /
                        "docs/feature-parity/acceptance-fixtures.json")
    args = parser.parse_args()

    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    fixture = next(item for item in catalog["fixtures"] if item["id"] == "ACC-401")["input"]
    content = fixture["content"]

    def text_for(role):
        return next(item["text"] for item in content if item["role"] == role)

    checks = []

    def require(condition, message):
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    path = args.directory / "tagged.pdf"
    reader = PdfReader(path, strict=True)

    require(not reader.is_encrypted, "ACC-401: the document is not encrypted")
    require(reader.metadata.get("/Title") == fixture["title"],
            "ACC-401: the document title is present")
    require(reader.metadata.get("/Producer") is not None,
            "ACC-401: the document declares a producer")

    catalog_object, struct_tree_root, root, elements = read_structure_tree(reader)
    parent_tree = read_parent_tree(struct_tree_root)
    page_references = [resolve(page.indirect_reference) for page in reader.pages]

    require(name(catalog_object.get("/Lang")) == fixture["language"],
            "ACC-401: the catalog declares the requested language")
    require(bool(resolve(catalog_object["/MarkInfo"])["/Marked"]),
            "ACC-401: the catalog declares a tagged document")
    require(bool(resolve(catalog_object["/ViewerPreferences"])["/DisplayDocTitle"]),
            "ACC-401: the title is configured for display")
    require(int(struct_tree_root["/ParentTreeNextKey"]) > 0,
            "ACC-401: the parent tree declares the next free key")

    roles = [element["role"] for element in elements]
    require(roles == ["Document", "H1", "P", "Figure", "P"],
            "ACC-401: structure elements are H1, P, Figure and the spanning paragraph in reading order")
    require(all(element["parent"] is not None or element is root for element in elements),
            "ACC-401: every structure element is connected to the Document element")
    require("Artifact" not in roles,
            "ACC-401: decorative content is absent from the structure tree")

    figure = find_role_node(root, "Figure")
    figure_alt = next(item["alt"] for item in content if item["role"] == "Figure")
    require(figure is not None, "ACC-401: the figure has a structure element")
    require(figure["alt"] == figure_alt, "ACC-401: the figure alternative text is exact")
    require(len(figure["marked_content"]) == 1,
            "ACC-401: the figure owns exactly one marked-content reference")

    require(len(reader.pages) > 1, "ACC-401: the fixture spans more than one page")

    forms_by_page = [page_forms(page) for page in reader.pages]
    artifacts = [form for forms in forms_by_page for form in forms if form["struct_parents"] is None]
    tagged_forms = [form for forms in forms_by_page for form in forms if form["struct_parents"] is not None]

    require(artifacts, "ACC-401: decorative content is stored as an artifact form")
    require(all(form["content"].startswith(b"/Artifact BMC") for form in artifacts),
            "ACC-401: artifact streams start with /Artifact BMC")
    require(all(form["struct_parents"] is None for form in artifacts),
            "ACC-401: artifacts are not linked to the parent tree")
    require(all(any(form["struct_parents"] is None for form in forms) for forms in forms_by_page),
            "ACC-401: the repeated header stays an artifact on every page")

    require(tagged_forms, "ACC-401: semantic content is stored as tagged forms")
    require(all(form["struct_parents"] in parent_tree for form in tagged_forms),
            "ACC-401: every tagged form has a parent-tree entry")

    spanning = elements[-1]
    require(spanning["role"] == "P", "ACC-401: the last element is the spanning paragraph")
    require(len(spanning["marked_content"]) == len(reader.pages),
            "ACC-401: a paragraph split across pages stays one structure element")
    require([page_references.index(resolve(mcr["/Pg"])) for mcr in spanning["marked_content"]]
            == list(range(len(reader.pages))),
            "ACC-401: each page contributes its own marked-content reference in order")

    element_by_dictionary = {id(element["dictionary"]): element for element in elements}

    for element in flatten(elements):
        for mcr in element["marked_content"]:
            form = resolve(mcr["/Stm"])
            page = resolve(mcr["/Pg"])
            key = int(form["/StructParents"])
            require(key in parent_tree, f"ACC-401: {element['role']} form is in the parent tree")
            owners = resolve(parent_tree[key])
            owners = list(owners) if isinstance(owners, ArrayObject) else [owners]
            require(any(resolve(owner) is element["dictionary"] for owner in owners),
                    f"ACC-401: the parent tree maps the form back to {element['role']}")
            require(any(resolve(candidate) is page for candidate in page_references),
                    f"ACC-401: {element['role']} is attached to a real page")
            require(b"BDC" in form.get_data() and b"EMC" in form.get_data(),
                    f"ACC-401: {element['role']} stream is wrapped in marked content")
            require(b"/MCID 0" in form.get_data(),
                    f"ACC-401: {element['role']} declares a marked-content identifier")

    for element in flatten(elements):
        for reference in element["object_references"]:
            annotation = resolve(reference["/Obj"])
            key = int(annotation["/StructParent"])
            require(key in parent_tree, "ACC-401: tagged links are in the parent tree")
            require(resolve(parent_tree[key]) is element["dictionary"],
                    "ACC-401: a tagged link resolves back to its structure element")

    extracted = "\n".join(page.extract_text() for page in reader.pages)
    require(text_for("H1") in extracted, "ACC-401: heading text is present")
    require(text_for("P") in extracted, "ACC-401: paragraph text is present")

    result = subprocess.run([args.qpdf, "--check", str(path)], capture_output=True, text=True, check=False)
    require(result.returncode == 0,
            "ACC-401: qpdf --check reports a structurally valid PDF")
    require("no syntax or stream encoding errors" in (result.stdout + result.stderr).lower(),
            "ACC-401: qpdf reports no syntax or stream encoding errors")

    rendering = []
    if args.render:
        import pymupdf
        for mode in ("rounded", "rotated", "empty", "canvas", "svg"):
            prefix = args.directory / ("graphics-state-" + mode)
            before, after = Path(str(prefix) + "-source.pdf"), Path(str(prefix) + "-tagged.pdf")
            with pymupdf.open(before) as original, pymupdf.open(after) as tagged:
                require(len(original) == len(tagged) == 1, mode + ": visual regression fixture retains one page")
                left = original[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
                right = tagged[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
                left.save(str(prefix) + "-source.png")
                right.save(str(prefix) + "-tagged.png")
                require((left.width, left.height, left.n) == (right.width, right.height, right.n),
                        mode + ": rendered page geometry is unchanged")
                error = max(abs(x-y) for x, y in zip(left.samples, right.samples))
                require(error <= 2, mode + ": every rendered channel differs by at most 2/255 (edge antialiasing)")
                rendering.append({"fixture": mode, "maximum_channel_error": error,
                                  "source_sha256": hashlib.sha256(before.read_bytes()).hexdigest(),
                                  "tagged_sha256": hashlib.sha256(after.read_bytes()).hexdigest()})
            result = subprocess.run([args.qpdf, "--check", str(after)], capture_output=True, text=True)
            require(result.returncode == 0, mode + ": regression PDF passes qpdf without warnings")
    (args.directory / "independent-validation.json").write_text(json.dumps(
        {"checks": checks, "rendering": rendering}, indent=2), encoding="utf-8")

    for message in checks:
        print("PASS " + message)

    print(f"PASS ACC-401 ({len(checks)} independent checks)")


if __name__ == "__main__":
    main()
