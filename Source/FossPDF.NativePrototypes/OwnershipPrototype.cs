using System.Text;
using System.Xml;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;
using SkiaSharp;

namespace FossPDF.NativePrototypes;

internal static class OwnershipPrototype
{
    public static PrototypeResult Run()
    {
        var checks = new List<string>();
        var pdf = Document.Create(container => container.Page(page =>
        {
            page.Size(120, 80);
            page.Content().Text("Borrowed streams");
        })).GeneratePdf();

        using var first = new NonSeekableInput(pdf);
        using var second = new NonSeekableInput(pdf);
        using var sink = new NonSeekableOutput();
        PdfOperationsPrototype.Merge(first, second, sink);
        Require(!first.WasDisposed && !second.WasDisposed && !sink.WasDisposed && sink.Bytes.Length > 0,
            "non-seekable input/output works and caller streams survive backend cleanup");

        using var valid = new NonSeekableInput(pdf);
        using var malformed = new NonSeekableInput(Encoding.ASCII.GetBytes("not a PDF"));
        using var untouched = new NonSeekableOutput();
        Throws<InvalidOperationException>(() => PdfOperationsPrototype.Merge(valid, malformed, untouched));
        Require(untouched.Bytes.Length == 0 && !valid.WasDisposed && !malformed.WasDisposed && !untouched.WasDisposed,
            "malformed PDF fails before output and leaves borrowed streams available");

        using var tagInput = new NonSeekableInput(pdf);
        Throws<ArgumentException>(() => TaggingPrototype.TagKnownPageBatches(tagInput, untouched, []));
        Require(untouched.Bytes.Length == 0 && !tagInput.WasDisposed,
            "missing semantic-to-paint association is rejected before output");
        using var duplicateInput = new NonSeekableInput(pdf);
        TaggingPrototype.PageBatch[] duplicates =
        [new(0, "H1", "en-US", "First", 0), new(0, "P", "en-US", "Second", 0)];
        Throws<ArgumentException>(() => TaggingPrototype.TagKnownPageBatches(duplicateInput, untouched, duplicates));
        Require(untouched.Bytes.Length == 0 && !duplicateInput.WasDisposed,
            "duplicate paint associations are rejected before output");

        using var failFirst = new NonSeekableInput(pdf);
        using var failSecond = new NonSeekableInput(pdf);
        using var failing = new NonSeekableOutput(failWrites: true);
        Throws<IOException>(() => PdfOperationsPrototype.Merge(failFirst, failSecond, failing));
        Require(!failFirst.WasDisposed && !failSecond.WasDisposed && !failing.WasDisposed,
            "output write failure propagates without disposing caller streams");

        using var svgInput = new NonSeekableInput(Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>"));
        SKPicture picture;
        using (var owner = SvgPrototype.Parse(svgInput))
            picture = owner.Picture ?? throw new InvalidDataException("Missing SVG picture.");
        Require(picture.Handle == IntPtr.Zero && !svgInput.WasDisposed,
            "disposing SKSvg releases its SKPicture and leaves the caller input open");
        using var malformedSvg = new NonSeekableInput(Encoding.UTF8.GetBytes("<svg"));
        Throws<XmlException>(() => { using var rejected = SvgPrototype.Parse(malformedSvg); });
        using var externalSvg = new NonSeekableInput(Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg'><image href='https://example.invalid/image.png'/></svg>"));
        Throws<InvalidDataException>(() => { using var rejected = SvgPrototype.Parse(externalSvg); });
        Require(!malformedSvg.WasDisposed && !externalSvg.WasDisposed,
            "malformed/external-resource SVG is rejected without taking stream ownership");

        using var importStream = new MemoryStream(pdf);
        using var import = PdfReader.Open(importStream, PdfDocumentOpenMode.Import);
        using var uaDocument = new PdfDocument();
        UAManager.ForDocument(uaDocument);
        Throws<InvalidOperationException>(() => uaDocument.AddPage(import.Pages[0]));
        checks.Add("OK: UAManager rejects imported pages; the selected tagging path uses the low-level object model instead.");
        return new PrototypeResult { Name = "ownership-and-errors", Passed = true, Checks = checks };

        void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidDataException(description);
            checks.Add("OK: " + description + ".");
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidDataException("Expected " + typeof(T).Name + ".");
    }

    private sealed class NonSeekableInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        internal bool WasDisposed { get; private set; }
        public override bool CanRead => !WasDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableOutput(bool failWrites = false) : Stream
    {
        private readonly MemoryStream _inner = new();
        internal bool WasDisposed { get; private set; }
        internal byte[] Bytes => _inner.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !WasDisposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (failWrites) throw new IOException("Fixture output failure.");
            _inner.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
