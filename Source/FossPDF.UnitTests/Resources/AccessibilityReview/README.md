# Accessibility review fixture

`quarterly-service-report-r1.pdf` is an immutable, two-page PDF/UA-1 + PDF/A-3a review file.
Its SHA-256 is `7f954761d56222cc8c8cb42f61c9d17041451e923ea75ece88436ef7c8189b68`.
The API, veraPDF 1.30.3, qpdf and rendered pages have been checked. Those checks do not constitute
a human screen-reader/accessibility inspection.

Use NVDA, an Acrobat accessibility inspection, PAC, or another suitable accessibility tool.
Record its name/version, the reviewer, UTC date, actual observations and any defects in
`human-review.json`. The eight checks are:

1. Reading order follows the title, introduction, results, list, table, chart, links and details.
2. Heading navigation exposes one H1 and two H2 headings in that order.
3. The list has two items, each with a label and body.
4. Table navigation identifies the Request status and Count column headers and the two data rows.
5. The chart alternative communicates twelve completed requests and four awaiting a response.
6. The external, inline and section links expose useful descriptions; keyboard navigation follows
   the structure, and the section link reaches the second-page details.
7. The document language is English; the French paragraph exposes a French language override.
8. Decorative header/footer text is excluded from the logical reading sequence.

`status` stays `pending` until an actual person supplies these results. Never infer a human pass
from validator output, a generated transcript or an agent's visual inspection. ACC-403 stays
planned while this evidence is pending; separate machine tests execute regardless.

The CI inspector compares every generated profile's semantic transcript with these exact review
bytes. If a defect requires changing the review PDF, create r2, record its new hash and obtain a
new review. Keep earlier records as historical evidence.

The repository user reported this fixture was satisfactory and identified Acrobat as the tool.
The record labels the observations as a reviewer self-report. Tool version and exact inspection
time were not supplied; the available timestamp records when the report was captured. ACC-403
is active against this exact PDF hash, with those evidence limits disclosed.
