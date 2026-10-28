using System;
using System.Collections.Generic;
using System.IO;
using FossPDF.PdfOperations;
using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Performs operations on existing PDF documents: load, select, reorder and merge pages, then
    /// save the result. Pages are imported into a new document, so they keep their original page
    /// boxes, content streams and resources instead of being rasterized.
    /// </summary>
    /// <remarks>
    /// Operations are recorded in order and executed by <see cref="Save(string)"/>,
    /// <see cref="Save(Stream)"/> or <see cref="SaveToBytes"/>. Loading reads the whole input into
    /// memory immediately; borrowed input streams are read from their current position and never
    /// closed. Output is staged in memory before it is written, so a failed operation does not leave
    /// a partially written file or stream. The class targets <c>net9.0</c> and belongs to the
    /// optional <c>FossPDF.PdfOperations</c> package, which the core package does not reference.
    /// </remarks>
    public sealed class DocumentOperation
    {
        private const string DefaultSelector = "1-z";

        private readonly List<InputSource> _sources = new List<InputSource>();
        private readonly List<PageSelection> _selections = new List<PageSelection>();
        private readonly List<LayerOperation> _underlays = new List<LayerOperation>();
        private readonly List<LayerOperation> _overlays = new List<LayerOperation>();

        private DocumentOperation()
        {
        }

        /// <summary>
        /// Describes one overlay or underlay source: the layer PDF file and how its pages are
        /// mapped onto the output pages.
        /// </summary>
        /// <remarks>
        /// <see cref="TargetPages"/> selects output pages and defaults to all of them.
        /// <see cref="SourcePages"/> selects the layer pages used for the first output pages and
        /// defaults to all layer pages. <see cref="RepeatSourcePages"/> cycles after the initial
        /// source pages are exhausted; when it is supplied without <see cref="SourcePages"/>, the
        /// repetition starts with the first target page.
        /// </remarks>
        public sealed class LayerConfiguration
        {
            /// <summary>
            /// The path of the overlay or underlay PDF file. The file is read immediately.
            /// </summary>
            public string FilePath { get; set; } = string.Empty;

            /// <summary>
            /// The output pages that receive the layer. Defaults to all output pages.
            /// </summary>
            public string? TargetPages { get; set; }

            /// <summary>
            /// The layer pages used first, in selector order. Defaults to all layer pages.
            /// </summary>
            public string? SourcePages { get; set; }

            /// <summary>
            /// Layer pages that repeat after <see cref="SourcePages"/> is exhausted. No repetition
            /// happens when this is not set.
            /// </summary>
            public string? RepeatSourcePages { get; set; }
        }

        /// <summary>
        /// Loads an unprotected PDF file.
        /// </summary>
        public static DocumentOperation LoadFile(string filePath)
        {
            return Load(ReadFile(filePath, nameof(filePath)), filePath);
        }

        /// <summary>
        /// Loads a PDF file, using the supplied password when the file is protected.
        /// </summary>
        public static DocumentOperation LoadFile(string filePath, string? password)
        {
            return Load(ReadFile(filePath, nameof(filePath)), filePath, password);
        }

        /// <summary>
        /// Loads a PDF from a byte array. The array is read-only to the operation and is not modified.
        /// </summary>
        public static DocumentOperation Load(byte[] data)
        {
            return Load(ValidateBytes(data), "(byte array)");
        }

        /// <summary>
        /// Loads a PDF from a byte array, using the supplied password when the document is protected.
        /// </summary>
        public static DocumentOperation Load(byte[] data, string? password)
        {
            return Load(ValidateBytes(data), "(byte array)", password);
        }

        /// <summary>
        /// Loads a PDF from a borrowed stream. The stream is read from its current position and is
        /// left open; it can be disposed as soon as this call returns.
        /// </summary>
        public static DocumentOperation Load(Stream stream)
        {
            return Load(ReadStream(stream, nameof(stream)), "(stream)");
        }

        /// <summary>
        /// Loads a PDF from a borrowed stream, using the supplied password when the document is protected.
        /// </summary>
        public static DocumentOperation Load(Stream stream, string? password)
        {
            return Load(ReadStream(stream, nameof(stream)), "(stream)", password);
        }

        /// <summary>
        /// Selects pages from the loaded document using a compact page selector:
        /// <c>1</c>, <c>z</c> (last page), <c>r2</c> (second to last), <c>1,6,4</c>,
        /// <c>3-7</c>, <c>7-3</c>, <c>1-20:even</c>, <c>1-10,x3-4</c>.
        /// Calling this method more than once appends the selections in order.
        /// </summary>
        public DocumentOperation TakePages(string pageSelector)
        {
            if (pageSelector == null)
                throw new ArgumentNullException(nameof(pageSelector));

            if (string.IsNullOrWhiteSpace(pageSelector))
                throw new ArgumentException("A page selector must not be empty.", nameof(pageSelector));

            _selections.Add(new PageSelection(0, pageSelector));
            return this;
        }

        /// <summary>
        /// Merges every page of another PDF file. When no selection was made for the loaded document
        /// yet, all of its pages are kept before the merged file.
        /// </summary>
        public DocumentOperation MergeFile(string filePath)
        {
            return MergeFile(filePath, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF file, in selector order.
        /// </summary>
        public DocumentOperation MergeFile(string filePath, string? pageSelector)
        {
            var source = AddSource(ReadFile(filePath, nameof(filePath)), filePath);
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Merges pages of another PDF supplied as a byte array.
        /// </summary>
        public DocumentOperation Merge(byte[] data)
        {
            return Merge(data, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF supplied as a byte array.
        /// </summary>
        public DocumentOperation Merge(byte[] data, string? pageSelector)
        {
            var source = AddSource(ValidateBytes(data), "(byte array)");
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Merges pages of another PDF supplied as a borrowed stream. The stream is read from its
        /// current position and left open.
        /// </summary>
        public DocumentOperation Merge(Stream stream)
        {
            return Merge(stream, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF supplied as a borrowed stream.
        /// </summary>
        public DocumentOperation Merge(Stream stream, string? pageSelector)
        {
            var source = AddSource(ReadStream(stream, nameof(stream)), "(stream)");
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Applies a PDF file beneath the selected output pages. Every underlay is drawn in the
        /// order the methods were called, before the page's own content.
        /// </summary>
        public DocumentOperation UnderlayFile(LayerConfiguration configuration)
        {
            AddLayer(configuration, _underlays);
            return this;
        }

        /// <summary>
        /// Applies a PDF file above the selected output pages. Every overlay is drawn in the order
        /// the methods were called, after the page's own content.
        /// </summary>
        public DocumentOperation OverlayFile(LayerConfiguration configuration)
        {
            AddLayer(configuration, _overlays);
            return this;
        }

        /// <summary>
        /// Executes the recorded operations and writes the result to a file. The file is only
        /// created after the operation has completed successfully.
        /// </summary>
        public void Save(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("An output file path must not be empty.", nameof(filePath));

            var output = SaveToBytes();
            File.WriteAllBytes(filePath, output);
        }

        /// <summary>
        /// Executes the recorded operations and writes the result to a borrowed stream. The stream is
        /// left open and remains writable.
        /// </summary>
        public void Save(Stream output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            if (!output.CanWrite)
                throw new ArgumentException("The output stream must be writable.", nameof(output));

            var bytes = SaveToBytes();
            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Executes the recorded operations and returns the resulting PDF bytes.
        /// </summary>
        public byte[] SaveToBytes()
        {
            return PdfDocumentMerger.Execute(_sources, _selections, _underlays, _overlays);
        }

        private void AddLayer(LayerConfiguration configuration, List<LayerOperation> layers)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var targetPages = ValidateSelector(configuration.TargetPages, nameof(configuration.TargetPages));
            var sourcePages = ValidateSelector(configuration.SourcePages, nameof(configuration.SourcePages));
            var repeatSourcePages = ValidateSelector(configuration.RepeatSourcePages, nameof(configuration.RepeatSourcePages));

            var data = ReadFile(configuration.FilePath, nameof(configuration.FilePath));

            layers.Add(new LayerOperation(
                data,
                configuration.FilePath,
                targetPages,
                sourcePages,
                repeatSourcePages));
        }

        private static string? ValidateSelector(string? selector, string parameterName)
        {
            if (selector == null)
                return null;

            if (string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("A page selector must not be empty.", parameterName);

            return selector;
        }

        private static DocumentOperation Load(byte[] data, string name, string? password = null)
        {
            var operation = new DocumentOperation();
            operation.AddSource(data, name, password);
            return operation;
        }

        private int AddSource(byte[] data, string name, string? password = null)
        {
            _sources.Add(new InputSource(data, name, password));
            return _sources.Count - 1;
        }

        private void AppendMerge(int sourceIndex, string? pageSelector)
        {
            if (_selections.Count == 0)
                _selections.Add(new PageSelection(0, DefaultSelector));

            _selections.Add(new PageSelection(sourceIndex, pageSelector ?? DefaultSelector));
        }

        private static byte[] ReadFile(string filePath, string parameterName)
        {
            if (filePath == null)
                throw new ArgumentNullException(parameterName);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("An input file path must not be empty.", parameterName);

            return File.ReadAllBytes(filePath);
        }

        private static byte[] ReadStream(Stream stream, string parameterName)
        {
            if (stream == null)
                throw new ArgumentNullException(parameterName);

            if (!stream.CanRead)
                throw new ArgumentException("The input stream must be readable.", parameterName);

            using var buffer = new MemoryStream();

            try
            {
                stream.CopyTo(buffer);
            }
            catch (IOException exception)
            {
                throw new PdfOperationException("The input PDF stream could not be read.", exception);
            }

            if (buffer.Length == 0)
                throw new PdfOperationException("The input PDF stream is empty.");

            return buffer.ToArray();
        }

        private static byte[] ValidateBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length == 0)
                throw new PdfOperationException("The input PDF byte array is empty.");

            return data;
        }

        internal sealed class InputSource
        {
            public InputSource(byte[] data, string name, string? password)
            {
                Data = data;
                Name = name;
                Password = password;
            }

            public byte[] Data { get; }
            public string Name { get; }
            public string? Password { get; }
        }

        internal readonly struct PageSelection
        {
            public PageSelection(int sourceIndex, string selector)
            {
                SourceIndex = sourceIndex;
                Selector = selector;
            }

            public int SourceIndex { get; }
            public string Selector { get; }
        }

        internal sealed class LayerOperation
        {
            public LayerOperation(
                byte[] data,
                string name,
                string? targetPages,
                string? sourcePages,
                string? repeatSourcePages)
            {
                Data = data;
                Name = name;
                TargetPages = targetPages;
                SourcePages = sourcePages;
                RepeatSourcePages = repeatSourcePages;
            }

            public byte[] Data { get; }
            public string Name { get; }
            public string? TargetPages { get; }
            public string? SourcePages { get; }
            public string? RepeatSourcePages { get; }
        }
    }

    /// <summary>
    /// Executes a recorded operation plan against PDFsharp. Kept separate from the fluent surface so
    /// the parsing and import behavior can be tested and reasoned about independently.
    /// </summary>
    internal static class PdfDocumentMerger
    {
        public static byte[] Execute(
            IReadOnlyList<DocumentOperation.InputSource> sources,
            IReadOnlyList<DocumentOperation.PageSelection> selections,
            IReadOnlyList<DocumentOperation.LayerOperation> underlays,
            IReadOnlyList<DocumentOperation.LayerOperation> overlays)
        {
            if (sources.Count == 0)
                throw new InvalidOperationException("Load a PDF document before saving an operation.");

            var opened = new List<PdfDocument>(sources.Count);
            // PDFsharp may read from the stream while the imported pages are used, so the streams
            // must stay alive for as long as the source documents are open.
            var buffers = new List<MemoryStream>(sources.Count);

            try
            {
                foreach (var source in sources)
                    opened.Add(Open(source, buffers));

                var effective = selections.Count == 0
                    ? new[] { new DocumentOperation.PageSelection(0, "1-z") }
                    : selections;

                using var destination = new PdfDocument();

                foreach (var selection in effective)
                {
                    var source = opened[selection.SourceIndex];
                    var pages = PageRangeSelector.Resolve(selection.Selector, source.PageCount, sources[selection.SourceIndex].Name);

                    foreach (var page in pages)
                        destination.AddPage(source.Pages[page]);
                }

                if (destination.PageCount == 0)
                    throw new PdfOperationException("The page selection produced no pages.");

                CopyMetadata(opened[0], destination);

                PdfLayers.Apply(destination, underlays, overlays);

                using var buffer = new MemoryStream();
                destination.Save(buffer, false);

                return buffer.ToArray();
            }
            catch (PdfSharpException exception)
            {
                throw new PdfOperationException(
                    "The input PDF could not be read. It may be malformed, truncated or protected by an unsupported password.", exception);
            }
            finally
            {
                foreach (var document in opened)
                    document.Dispose();

                foreach (var buffer in buffers)
                    buffer.Dispose();
            }
        }

        private static PdfDocument Open(DocumentOperation.InputSource source, List<MemoryStream> buffers)
        {
            var buffer = new MemoryStream(source.Data, writable: false);
            buffers.Add(buffer);

            try
            {
                return source.Password == null
                    ? PdfReader.Open(buffer, PdfDocumentOpenMode.Import)
                    : PdfReader.Open(buffer, source.Password, PdfDocumentOpenMode.Import, new PdfReaderOptions());
            }
            catch (Exception exception) when (!(exception is PdfOperationException))
            {
                // PDFsharp reports malformed and truncated files with several different exception
                // types, including plain System.Exception. They all mean the same thing here.
                throw new PdfOperationException(
                    $"'{source.Name}' could not be opened. It may be malformed, truncated or require a different password.", exception);
            }
        }

        private static void CopyMetadata(PdfDocument source, PdfDocument destination)
        {
            var info = source.Info;
            var target = destination.Info;

            target.Title = Copy(info.Title, target.Title);
            target.Author = Copy(info.Author, target.Author);
            target.Subject = Copy(info.Subject, target.Subject);
            target.Keywords = Copy(info.Keywords, target.Keywords);
            target.Creator = Copy(info.Creator, target.Creator);

            var creationDate = info.CreationDate.ToLocalTime();

            if (creationDate.Year > 1)
                target.CreationDate = creationDate;

            var modificationDate = info.ModificationDate.ToLocalTime();

            if (modificationDate.Year > 1)
                target.ModificationDate = modificationDate;
        }

        private static string Copy(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
