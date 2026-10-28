#if NET9_0
using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-302: existing-PDF loading, selection, reordering, merging and saving. The operations
    /// companion targets net9.0, so the whole fixture is compiled only for that framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PdfOperationTests
    {
        [Test]
        public void LoadAndSave_PreservesPageCountGeometryAndMetadata()
        {
            var source = CreateSource(
                (200, 100, "#d32f2f"),
                (300, 120, "#1976d2"));

            var result = DocumentOperation.Load(source).SaveToBytes();

            var pages = ReadPages(result);

            pages.Should().HaveCount(2);
            ((double)pages[0].Width).Should().BeApproximately(200, 0.01);
            ((double)pages[0].Height).Should().BeApproximately(100, 0.01);
            ((double)pages[1].Width).Should().BeApproximately(300, 0.01);
            ((double)pages[1].Height).Should().BeApproximately(120, 0.01);

            using var document = Open(result);
            document.Info.Title.Should().Be("Source document");
            document.Info.Author.Should().Be("FossPDF tests");
        }

        [Test]
        public void TakePages_ReordersAndRepeatsPages()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"));

            var result = DocumentOperation.Load(source).TakePages("3,1,3").SaveToBytes();

            ReadPages(result).Select(x => x.Width).Should().Equal(400f, 200f, 400f);
        }

        [Test]
        public void TakePages_SupportsLastAndRelativePages()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"));

            var result = DocumentOperation.Load(source).TakePages("z,r2").SaveToBytes();

            ReadPages(result).Select(x => x.Width).Should().Equal(400f, 300f);

            var relative = DocumentOperation.Load(source).TakePages("r1").SaveToBytes();
            var last = DocumentOperation.Load(source).TakePages("z").SaveToBytes();

            ReadPages(relative).Select(x => x.Width).Should().Equal(ReadPages(last).Select(x => x.Width));
        }

        [Test]
        public void TakePages_SupportsAscendingAndDescendingRanges()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"), (500, 100, "#fbc02d"));

            DocumentOperation.Load(source).TakePages("2-4").SaveToBytes()
                .Let(x => ReadPages(x).Select(p => p.Width)).Should().Equal(300f, 400f, 500f);

            DocumentOperation.Load(source).TakePages("4-2").SaveToBytes()
                .Let(x => ReadPages(x).Select(p => p.Width)).Should().Equal(500f, 400f, 300f);

            DocumentOperation.Load(source).TakePages("z-1").SaveToBytes()
                .Let(x => ReadPages(x).Select(p => p.Width)).Should().Equal(500f, 400f, 300f, 200f);
        }

        [Test]
        public void TakePages_FiltersOddAndEvenPages()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"), (500, 100, "#fbc02d"));

            DocumentOperation.Load(source).TakePages("1-4:even").SaveToBytes()
                .Let(x => ReadPages(x).Select(p => p.Width)).Should().Equal(300f, 500f);

            DocumentOperation.Load(source).TakePages("1-4:odd").SaveToBytes()
                .Let(x => ReadPages(x).Select(p => p.Width)).Should().Equal(200f, 400f);
        }

        [Test]
        public void TakePages_ExcludesSelectedPages()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"), (500, 100, "#fbc02d"));

            var result = DocumentOperation.Load(source).TakePages("4,1-3,x2-3").SaveToBytes();

            ReadPages(result).Select(x => x.Width).Should().Equal(500f, 200f);
        }

        [Test]
        public void TakePages_RejectsEmptyOrInvalidSelections()
        {
            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"));

            foreach (var selector in new[] { "1-", "abc", ":odd", "1-2:sideways", "1,,2", "x" })
            {
                Action action = () => DocumentOperation.Load(source).TakePages(selector).SaveToBytes();
                action.Should().Throw<ArgumentException>($"selector '{selector}' is malformed");
            }

            Action nullSelector = () => DocumentOperation.Load(source).TakePages(null);
            nullSelector.Should().Throw<ArgumentNullException>();

            Action blankSelector = () => DocumentOperation.Load(source).TakePages("  ");
            blankSelector.Should().Throw<ArgumentException>();

            foreach (var selector in new[] { "4", "0", "r4", "r0", "x1-3", "1-3,x1-4" })
            {
                Action action = () => DocumentOperation.Load(source).TakePages(selector).SaveToBytes();
                action.Should().Throw<PdfOperationException>($"selector '{selector}' references or selects no pages");
            }
        }

        [Test]
        public void MergeFile_AppendsSelectedPagesFromAnotherFile()
        {
            var first = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"));
            var second = CreateSource((400, 100, "#388e3c"), (500, 100, "#fbc02d"), (600, 100, "#8e24aa"));

            var path = WriteTemporaryFile(second);

            try
            {
                var result = DocumentOperation.Load(first).MergeFile(path, "3,1").SaveToBytes();

                ReadPages(result).Select(x => x.Width).Should().Equal(200f, 300f, 600f, 400f);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Merge_WithoutSelectionKeepsAllPagesOfBothDocuments()
        {
            var first = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"));
            var second = CreateSource((400, 100, "#388e3c"));

            var result = DocumentOperation.Load(first).Merge(second).SaveToBytes();

            ReadPages(result).Select(x => x.Width).Should().Equal(200f, 300f, 400f);
        }

        [Test]
        public void Merge_AfterTakePagesUsesSelectionForLoadedDocument()
        {
            var first = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"), (400, 100, "#388e3c"));
            var second = CreateSource((500, 100, "#fbc02d"));

            var result = DocumentOperation.Load(first).TakePages("3,1").Merge(second, "1").SaveToBytes();

            ReadPages(result).Select(x => x.Width).Should().Equal(400f, 200f, 500f);
        }

        [Test]
        public void LoadFromStream_LeavesStreamOpenAndReadsFromCurrentPosition()
        {
            var source = CreateSource((200, 100, "#d32f2f"));

            using var stream = new MemoryStream();
            stream.Write(source, 0, source.Length);
            stream.Position = 0;

            var operation = DocumentOperation.Load(stream);

            stream.CanRead.Should().BeTrue();
            stream.Position.Should().Be(source.Length);

            ReadPages(operation.SaveToBytes()).Should().HaveCount(1);
        }

        [Test]
        public void SaveToStream_LeavesStreamOpenAndAppendable()
        {
            var source = CreateSource((200, 100, "#d32f2f"));

            using var output = new MemoryStream();

            DocumentOperation.Load(source).Save(output);

            output.CanRead.Should().BeTrue();
            output.CanWrite.Should().BeTrue();

            var written = output.ToArray();
            written.Take(5).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-');

            output.WriteByte(0x42);
            output.Length.Should().Be(written.Length + 1);
        }

        [Test]
        public void SaveToFile_CreatesAValidDocument()
        {
            var source = CreateSource((200, 100, "#d32f2f"));

            var path = Path.Combine(Path.GetTempPath(), $"foss-pdf-{Guid.NewGuid():N}.pdf");
            File.Delete(path);

            try
            {
                DocumentOperation.Load(source).Save(path);

                File.Exists(path).Should().BeTrue();
                ReadPages(File.ReadAllBytes(path)).Should().HaveCount(1);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void MalformedAndTruncatedInput_ThrowsOperationException()
        {
            Action malformed = () => DocumentOperation.Load(new byte[] { 1, 2, 3, 4 }).SaveToBytes();
            malformed.Should().Throw<PdfOperationException>();

            var source = CreateSource((200, 100, "#d32f2f"), (300, 100, "#1976d2"));
            var truncated = source.Take(source.Length / 2).ToArray();

            Action truncatedAction = () => DocumentOperation.Load(truncated).SaveToBytes();
            truncatedAction.Should().Throw<PdfOperationException>();

            Action empty = () => DocumentOperation.Load(Array.Empty<byte>());
            empty.Should().Throw<PdfOperationException>();
        }

        [Test]
        public void FailedOperation_DoesNotWriteToTheOutputStream()
        {
            using var output = new MemoryStream();

            Action action = () => DocumentOperation.Load(new byte[] { 1, 2, 3 }).Save(output);

            action.Should().Throw<PdfOperationException>();
            output.Length.Should().Be(0);
        }

        [Test]
        public void PasswordProtectedInput_HonorsTheSuppliedPassword()
        {
            var protectedDocument = CreateProtectedSource();

            Action withoutPassword = () => DocumentOperation.Load(protectedDocument).SaveToBytes();
            withoutPassword.Should().Throw<PdfOperationException>();

            Action wrongPassword = () => DocumentOperation.Load(protectedDocument, "wrong").SaveToBytes();
            wrongPassword.Should().Throw<PdfOperationException>();

            var result = DocumentOperation.Load(protectedDocument, "user").SaveToBytes();
            ReadPages(result).Should().HaveCount(1);
        }

        [Test]
        public void ImportedPages_KeepLinkAnnotations()
        {
            var source = Document.Create(container => container.Page(page =>
            {
                page.Size(200, 100);
                page.Margin(10);
                page.Content().Hyperlink("https://example.com").Text("Example");
            })).GeneratePdf();

            var result = DocumentOperation.Load(source).SaveToBytes();

            using var sourceDocument = Open(source);
            using var resultDocument = Open(result);

            sourceDocument.Pages[0].Annotations.Count.Should().BeGreaterThan(0);
            resultDocument.Pages[0].Annotations.Count.Should()
                .Be(sourceDocument.Pages[0].Annotations.Count);
        }

        [Test]
        public void SourceByteArray_IsNotModified()
        {
            var source = CreateSource((200, 100, "#d32f2f"));
            var copy = source.ToArray();

            DocumentOperation.Load(source).SaveToBytes();

            source.Should().Equal(copy);
        }

        private static byte[] CreateSource(params (float Width, float Height, string Color)[] pages)
        {
            return Document.Create(container =>
            {
                foreach (var page in pages)
                {
                    container.Page(descriptor =>
                    {
                        descriptor.Size(page.Width, page.Height);
                        descriptor.Margin(0);
                        descriptor.PageColor(page.Color);
                    });
                }
            })
                .WithMetadata(new DocumentMetadata
                {
                    Title = "Source document",
                    Author = "FossPDF tests"
                })
                .GeneratePdf();
        }

        private static byte[] CreateProtectedSource()
        {
            using var document = new PdfDocument();
            document.AddPage();
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";

            using var buffer = new MemoryStream();
            document.Save(buffer, false);

            return buffer.ToArray();
        }

        [Test]
        public void Merge_PreservesPageContentInSelectionOrder()
        {
            var first = CreateSource((200, 100, "#d32f2f"), (201, 100, "#1976d2"));
            var second = CreateSource((202, 100, "#388e3c"));

            var firstPages = ReadContentSignatures(first);
            var secondPages = ReadContentSignatures(second);

            var result = DocumentOperation.Load(first).TakePages("2,1").Merge(second).SaveToBytes();

            ReadContentSignatures(result).Should().Equal(firstPages[1], firstPages[0], secondPages[0]);
        }

        private static string[] ReadContentSignatures(byte[] pdf)
        {
            using var document = Open(pdf);

            return Enumerable.Range(0, document.PageCount)
                .Select(index =>
                {
                    var builder = new System.Text.StringBuilder();

                    foreach (var item in document.Pages[index].Contents.Elements)
                    {
                        // A content stream is a dictionary carrying its raw stream bytes. The
                        // nested PdfStream type does not derive from PdfObject, so it is reached
                        // through the dictionary.
                        var dictionary = item switch
                        {
                            PdfReference reference => reference.Value as PdfDictionary,
                            PdfDictionary direct => direct,
                            _ => null
                        };

                        if (dictionary?.Stream?.Value != null)
                            builder.Append(Convert.ToHexString(dictionary.Stream.Value));
                    }

                    return builder.ToString();
                })
                .ToArray();
        }

        private static PageSize[] ReadPages(byte[] pdf)
        {
            using var document = Open(pdf);

            return Enumerable.Range(0, document.PageCount)
                .Select(index => new PageSize(
                    (float)document.Pages[index].Width.Point,
                    (float)document.Pages[index].Height.Point))
                .ToArray();
        }

        private static PdfDocument Open(byte[] pdf)
        {
            var stream = new MemoryStream(pdf, writable: false);
            return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        }

        private static string WriteTemporaryFile(byte[] content)
        {
            var path = Path.Combine(Path.GetTempPath(), $"foss-pdf-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(path, content);
            return path;
        }

        private readonly struct PageSize
        {
            public PageSize(float width, float height)
            {
                Width = width;
                Height = height;
            }

            public float Width { get; }
            public float Height { get; }
        }
    }

    internal static class PdfOperationTestExtensions
    {
        public static TResult Let<T, TResult>(this T value, Func<T, TResult> map)
        {
            return map(value);
        }
    }
}
#endif
