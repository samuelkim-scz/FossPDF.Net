# FP-404 invoice fixture

`en16931.xml` is an independently hand-authored UTF-8 CII fixture maintained in this repository.
It is not produced by FossPDF, Mustang or an invoice-generation dependency. All parties and
addresses are example data. The XML declares the exact `urn:cen.eu:en16931:2017` guideline.

The aligned Factur-X 1.09.2 / ZUGFeRD 2.5.2 EN16931 releases use this same XML and Factur-X
packaging. Tests invoke both explicit API selections; the underlying XML/XMP cannot distinguish
those brand names or minor releases. The pinned Mustang 2.26.0 resource hashes record the rule
set used to qualify the claimed releases.

Invoice FP-2026-001 contains two units of report preparation at EUR 6.00: EUR 12.00 net,
19% VAT of EUR 2.28, EUR 14.28 payable by bank transfer on 2026-11-29. Invoice and delivery date
are 2026-10-29. `EInvoiceTests.CreateDocument` and `EInvoiceExamples` independently compose the
matching visible fields. Applications remain responsible for that agreement in their documents.

The acceptance runner retains original XML and package/archival reports. The independent Python
inspector extracts the original bytes through pypdf and PyMuPDF and rejects deliberately changed
relationships, filenames, XMP profiles, currency codes and totals using the pinned validator
and strict structural checks. Mustang 2.26.0 alone accepts the wrong relationship and an allowed
but mismatched XMP profile; final runtime checks and independent inspection reject both.
