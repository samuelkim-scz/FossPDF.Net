#if NET9_0
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-302 acceptance: load existing PDFs, select and reorder pages, merge a second document and
    /// save without rasterizing. The PDF operations companion package targets net9.0 only, so this
    /// runner is compiled for that framework; the fixture declares frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-302")]
        private static void RunExistingPdfOperations(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var documentIds = FixtureCatalog.ReadArray(fixture.Input, "documents")
                .Select(x => x.GetString())
                .ToArray();

            Assert.That(documentIds, Has.Length.EqualTo(2),
                "PDF-302 requires two source contracts.");

            var firstFixture = catalog.Fixtures.Single(x => x.Id == documentIds[0]);
            var secondFixture = catalog.Fixtures.Single(x => x.Id == documentIds[1]);

            // Reuse the generated fixtures as independent inputs; the operation must import their
            // pages instead of composing them again.
            var firstPdf = ExplicitPagination(firstFixture).GeneratePdf();
            var secondPdf = OverflowPagination(secondFixture).GeneratePdf();

            var firstSelection = ReadSelection(fixture, "first_selection");
            var secondSelection = ReadSelection(fixture, "second_selection");
            var firstSelector = string.Join(",", firstSelection);
            var secondSelector = string.Join(",", secondSelection);

            SaveArtifact(fixture.Id, "sources/first.pdf", firstPdf);
            SaveArtifact(fixture.Id, "sources/second.pdf", secondPdf);

            var output = DocumentOperation
                .Load(firstPdf)
                .TakePages(firstSelector)
                .Merge(secondPdf, secondSelector)
                .SaveToBytes();

            Assert.That(Encoding.ASCII.GetString(output, 0, 5), Is.EqualTo("%PDF-"));
            SaveArtifact(fixture.Id, "selected-merged.pdf", output);

            var expectedPages = ReadSelectedPages(firstPdf, firstSelection)
                .Concat(ReadSelectedPages(secondPdf, secondSelection))
                .ToArray();

            Assert.That(ReadPageCount(output), Is.EqualTo(expectedPages.Length),
                "PDF-302: the result contains the selected pages only.");

            var actualPages = ReadPageFacts(output);

            Assert.That(actualPages.Length, Is.EqualTo(expectedPages.Length),
                "PDF-302: the result contains the selected pages only.");

            Assert.That(actualPages.Select(x => x.Signature),
                Is.EqualTo(expectedPages.Select(x => x.Signature)),
                "PDF-302: pages follow the selector order and keep their imported content streams.");

            Assert.That(actualPages.All(x => x.Signature.Length > 0), Is.True,
                "PDF-302: every selected page carries its original content.");

            for (var index = 0; index < actualPages.Length; index++)
            {
                Assert.That(actualPages[index].Width, Is.EqualTo(expectedPages[index].Width).Within(0.01),
                    $"PDF-302: page {index + 1} keeps its original width.");
                Assert.That(actualPages[index].Height, Is.EqualTo(expectedPages[index].Height).Within(0.01),
                    $"PDF-302: page {index + 1} keeps its original height.");
            }

            Assert.That(actualPages[0].Content, Does.Contain("0 0 1 rg"),
                "PDF-302: the first page is the blue page of PAG-001.");
            Assert.That(actualPages[0].Content, Does.Not.Contain("1 0 0 rg"),
                "PDF-302: the first page is not the red page.");

            Assert.That(actualPages[1].Content, Does.Contain("1 0 0 rg"),
                "PDF-302: the second page is the red page of PAG-001.");
            Assert.That(actualPages[1].Content, Does.Not.Contain("0 0 1 rg"),
                "PDF-302: the second page is not the blue page.");

            var overflowPage = actualPages[2].Content;
            Assert.That(overflowPage, Does.Contain("0 0 0 rg"),
                "PDF-302: the third page is page 1 of PAG-002 with its black header.");
            Assert.That(overflowPage, Does.Contain("1 0 0 rg"),
                "PDF-302: the third page keeps the red PAG-002 block.");
            Assert.That(overflowPage, Does.Contain("0 1 0 rg"),
                "PDF-302: the third page keeps the green PAG-002 block.");
            Assert.That(overflowPage, Does.Not.Contain("0 0 1 rg"),
                "PDF-302: the blue overflow page of PAG-002 is not selected.");

            Assert.That(ReadTitle(output), Is.EqualTo(ReadTitle(firstPdf)),
                "PDF-302: metadata is copied from the primary loaded document.");
            Assert.That(ReadTitle(output), Is.Not.Empty);

            AssertBorrowedStreamsAndArraysStayUsable(firstPdf, firstSelection);

            AssertFailuresAreExplicit(firstPdf);
        }

        private static int[] ReadSelection(ParityFixture fixture, string property)
        {
            var selection = FixtureCatalog.ReadArray(fixture.Input, property)
                .Select(x => x.GetInt32())
                .ToArray();

            Assert.That(selection, Is.Not.Empty, $"PDF-302: '{property}' must declare pages.");
            Assert.That(selection.All(x => x > 0), Is.True,
                $"PDF-302: '{property}' uses one-based page numbers.");

            return selection;
        }

        private static PdfPageFacts[] ReadSelectedPages(byte[] pdf, int[] selection)
        {
            var pages = ReadPageFacts(pdf);

            return selection.Select(page =>
            {
                Assert.That(page, Is.InRange(1, pages.Length),
                    "PDF-302: a declared selection must be inside the source document.");

                return pages[page - 1];
            }).ToArray();
        }

        private static void AssertBorrowedStreamsAndArraysStayUsable(byte[] firstPdf, int[] firstSelection)
        {
            using (var stream = new MemoryStream(firstPdf))
            {
                var streamed = DocumentOperation.Load(stream).TakePages("2,1").SaveToBytes();

                Assert.That(stream.CanRead, Is.True,
                    "PDF-302: the caller-owned input stream stays open.");
                Assert.That(stream.Position, Is.EqualTo(firstPdf.Length),
                    "PDF-302: the input stream is consumed from its current position.");
                Assert.That(ReadPageFacts(streamed).Select(x => x.Signature),
                    Is.EqualTo(ReadSelectedPages(firstPdf, firstSelection).Select(x => x.Signature)),
                    "PDF-302: stream loading selects the same pages as byte-array loading.");
            }

            var snapshot = firstPdf.ToArray();
            DocumentOperation.Load(firstPdf).TakePages("1").SaveToBytes();

            Assert.That(firstPdf, Is.EqualTo(snapshot),
                "PDF-302: loading a byte array does not modify the caller's bytes.");
        }

        private static void AssertFailuresAreExplicit(byte[] firstPdf)
        {
            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(new byte[] { 1, 2, 3, 4 }).SaveToBytes(),
                "PDF-302: malformed input fails clearly.");

            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(Array.Empty<byte>()),
                "PDF-302: empty input fails clearly.");

            Assert.Throws<ArgumentException>(
                () => DocumentOperation.Load(firstPdf).TakePages("1-").SaveToBytes(),
                "PDF-302: selector syntax errors are argument errors.");

            var missing = ReadPageCount(firstPdf) + 1;

            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(firstPdf).TakePages(missing.ToString()).SaveToBytes(),
                "PDF-302: selecting a page outside the document fails clearly.");

            var encrypted = CreateEncryptedPdf();

            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(encrypted).SaveToBytes(),
                "PDF-302: a protected document without a password fails clearly.");

            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(encrypted, "wrong").SaveToBytes(),
                "PDF-302: a wrong password fails clearly.");

            var decrypted = DocumentOperation.Load(encrypted, "user").SaveToBytes();

            Assert.That(ReadPageCount(decrypted), Is.EqualTo(1),
                "PDF-302: the declared password opens the protected document.");

            SaveArtifact("PDF-302", "password-protected.pdf", encrypted);
            SaveArtifact("PDF-302", "decrypted.pdf", decrypted);
        }

        private static byte[] CreateEncryptedPdf()
        {
            using var document = new PdfDocument();
            document.AddPage();
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";

            using var buffer = new MemoryStream();
            document.Save(buffer, false);

            return buffer.ToArray();
        }

        private static int ReadPageCount(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }

        private static string ReadTitle(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.Info.Title;
        }

        private static PdfPageFacts[] ReadPageFacts(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            return Enumerable.Range(0, document.PageCount)
                .Select(index => ReadPageFacts(document.Pages[index]))
                .ToArray();
        }

        private static PdfPageFacts ReadPageFacts(PdfPage page)
        {
            var signature = new StringBuilder();
            var content = new StringBuilder();

            foreach (var item in page.Contents.Elements)
            {
                // A content stream is a dictionary carrying its raw stream bytes. The nested
                // PdfStream type does not derive from PdfObject, so it is reached through the
                // dictionary.
                var dictionary = item switch
                {
                    PdfReference reference => reference.Value as PdfDictionary,
                    PdfDictionary direct => direct,
                    _ => null
                };

                var bytes = dictionary?.Stream?.Value;

                if (bytes == null)
                    continue;

                signature.Append(Convert.ToHexString(bytes));
                content.Append(DecodeStream(bytes));
            }

            return new PdfPageFacts(
                signature.ToString(),
                content.ToString(),
                page.Width.Point,
                page.Height.Point);
        }

        private static string DecodeStream(byte[] bytes)
        {
            try
            {
                using var compressed = new MemoryStream(bytes, writable: false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return Encoding.Latin1.GetString(output.ToArray());
            }
            catch (InvalidDataException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        private readonly struct PdfPageFacts
        {
            public PdfPageFacts(string signature, string content, double width, double height)
            {
                Signature = signature;
                Content = content;
                Width = width;
                Height = height;
            }

            public string Signature { get; }
            public string Content { get; }
            public double Width { get; }
            public double Height { get; }
        }
    }
}
#endif
