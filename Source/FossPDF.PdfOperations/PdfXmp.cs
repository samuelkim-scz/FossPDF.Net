using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Extends the output document's XMP metadata with caller-supplied XML fragments.
    /// </summary>
    /// <remarks>
    /// PDFsharp regenerates the catalog metadata stream whenever a document is saved, so overriding
    /// the packet before saving has no effect. The fragments are therefore merged into the saved
    /// packet and the result is written back as a PDF incremental update: the metadata stream object
    /// is redefined at the end of the file together with a one-entry cross-reference section and a
    /// trailer whose <c>/Prev</c> entry points at the original cross-reference table. The original
    /// bytes are retained, so the update can be read by ordinary PDF readers.
    /// </remarks>
    internal static class PdfXmp
    {
        private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string WrapperElement = "metadata-extension";

        public static void Validate(string metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (string.IsNullOrWhiteSpace(metadata))
            {
                throw new ArgumentException(
                    "A metadata extension must contain a well-formed XML fragment.", nameof(metadata));
            }

            XmlDocument wrapper;

            try
            {
                wrapper = Load($"<{WrapperElement}>" + metadata + $"</{WrapperElement}>");
            }
            catch (XmlException exception)
            {
                throw new ArgumentException(
                    "The metadata extension is not a well-formed XML fragment. " +
                    "Declare every namespace prefix inside the fragment.",
                    nameof(metadata),
                    exception);
            }

            foreach (XmlNode node in wrapper.DocumentElement!.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element)
                    return;
            }

            throw new ArgumentException(
                "A metadata extension must contain at least one XML element.", nameof(metadata));
        }

        /// <summary>
        /// Reads the XMP packet of a loaded input document, or returns <see langword="null"/> when
        /// the input has no metadata stream.
        /// </summary>
        public static string? Read(PdfDocument document, string sourceName)
        {
            var catalog = document.Internals.Catalog;

            if (catalog == null || !catalog.Elements.ContainsKey("/Metadata"))
                return null;

            var metadata = ResolveDictionary(catalog.Elements.GetObject("/Metadata"));
            var stream = metadata?.Stream;

            if (stream == null)
            {
                throw new PdfOperationException(
                    $"The XMP metadata of '{sourceName}' is missing or malformed and cannot be extended.");
            }

            if (stream.IsFiltered() && !stream.TryUncompress())
            {
                throw new PdfOperationException(
                    $"The XMP metadata of '{sourceName}' uses a stream filter that is not supported.");
            }

            var bytes = stream.UnfilteredValue;

            return bytes == null || bytes.Length == 0 ? null : Decode(bytes);
        }

        public static byte[] Apply(
            byte[] pdf,
            string sourceName,
            string? baseMetadata,
            IReadOnlyList<string> fragments)
        {
            var trailer = ReadTrailer(pdf);
            var saved = ReadSavedMetadata(pdf);
            var xmp = baseMetadata ?? saved.Text;
            var merged = Merge(xmp, fragments, sourceName);

            return AppendUpdate(pdf, trailer, saved.ObjectNumber, Encoding.UTF8.GetBytes(merged));
        }

        private static string Merge(string xmp, IReadOnlyList<string> fragments, string sourceName)
        {
            XmlDocument document;

            try
            {
                document = Load(xmp);
            }
            catch (XmlException exception)
            {
                throw new PdfOperationException(
                    $"The XMP metadata of '{sourceName}' is not well-formed XML and cannot be extended.",
                    exception);
            }

            var namespaceManager = new XmlNamespaceManager(document.NameTable);
            namespaceManager.AddNamespace("rdf", RdfNamespace);

            var description = document.SelectSingleNode("//rdf:Description", namespaceManager) as XmlElement;

            if (description == null)
            {
                var rdf = document.SelectSingleNode("//rdf:RDF", namespaceManager) as XmlElement
                    ?? throw new PdfOperationException(
                        $"The XMP metadata of '{sourceName}' does not contain an rdf:RDF element and cannot be extended.");

                description = document.CreateElement("rdf", "Description", RdfNamespace);
                rdf.AppendChild(description);
            }

            foreach (var fragment in fragments)
            {
                var wrapper = Load($"<{WrapperElement}>" + fragment + $"</{WrapperElement}>");

                foreach (XmlNode node in wrapper.DocumentElement!.ChildNodes)
                    description.AppendChild(document.ImportNode(node, true));
            }

            return document.OuterXml;
        }

        private static XmlDocument Load(string xml)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false
            };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            document.Load(reader);
            return document;
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            var text = Encoding.UTF8.GetString(bytes);

            return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
        }

        private static SavedMetadata ReadSavedMetadata(byte[] pdf)
        {
            try
            {
                using var buffer = new MemoryStream(pdf, writable: false);
                using var document = PdfReader.Open(buffer, PdfDocumentOpenMode.Import);

                var catalog = document.Internals.Catalog;
                var metadata = catalog == null
                    ? null
                    : ResolveDictionary(catalog.Elements.GetObject("/Metadata"));
                var reference = metadata == null ? null : PdfInternals.GetReference(metadata);
                var stream = metadata?.Stream;

                if (reference == null || stream == null)
                {
                    throw new PdfOperationException(
                        "The saved PDF does not contain an XMP metadata stream that can be extended.");
                }

                if (stream.IsFiltered() && !stream.TryUncompress())
                {
                    throw new PdfOperationException(
                        "The saved PDF's XMP metadata stream uses a filter that is not supported.");
                }

                var bytes = stream.UnfilteredValue;

                if (bytes == null || bytes.Length == 0)
                {
                    throw new PdfOperationException(
                        "The saved PDF's XMP metadata stream is empty and cannot be extended.");
                }

                return new SavedMetadata(reference.ObjectNumber, Decode(bytes));
            }
            catch (Exception exception) when (!(exception is PdfOperationException))
            {
                throw new PdfOperationException(
                    "The saved PDF could not be read back to extend its XMP metadata.", exception);
            }
        }

        private static TrailerInfo ReadTrailer(byte[] pdf)
        {
            var text = Encoding.Latin1.GetString(pdf);
            var startXrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);

            if (startXrefIndex < 0)
            {
                throw new PdfOperationException(
                    "The saved PDF does not contain a cross-reference table that can be extended.");
            }

            var previous = Regex.Match(text.Substring(startXrefIndex), @"startxref\s+(\d+)");
            var trailerIndex = text.LastIndexOf("trailer", startXrefIndex, StringComparison.Ordinal);

            if (!previous.Success || trailerIndex < 0)
            {
                throw new PdfOperationException(
                    "The saved PDF does not contain a cross-reference table that can be extended.");
            }

            var trailerEnd = text.IndexOf("startxref", trailerIndex, StringComparison.Ordinal);
            var trailer = text.Substring(trailerIndex, (trailerEnd < 0 ? text.Length : trailerEnd) - trailerIndex);
            var size = Regex.Match(trailer, @"/Size\s+(\d+)");
            var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+(\d+)\s+R");

            if (!size.Success || !root.Success)
            {
                throw new PdfOperationException(
                    "The saved PDF's trailer does not describe a document that can be extended.");
            }

            var id = Regex.Match(trailer, @"/ID\s*\[[^\]]*\]");
            var info = Regex.Match(trailer, @"/Info\s+(\d+)\s+(\d+)\s+R");

            return new TrailerInfo(
                long.Parse(previous.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(root.Groups[2].Value, CultureInfo.InvariantCulture),
                id.Success ? " " + id.Value : string.Empty,
                info.Success ? string.Format(CultureInfo.InvariantCulture, " /Info {0} {1} R", info.Groups[1].Value, info.Groups[2].Value) : string.Empty);
        }

        private static byte[] AppendUpdate(byte[] pdf, TrailerInfo trailer, int objectNumber, byte[] xmp)
        {
            using var output = new MemoryStream(pdf.Length + xmp.Length + 512);
            output.Write(pdf, 0, pdf.Length);

            if (pdf.Length == 0 || pdf[pdf.Length - 1] != (byte)'\n')
                output.WriteByte((byte)'\n');

            var offset = output.Position;
            Write(output, FormattableString.Invariant(
                $"{objectNumber} 0 obj\n<< /Type /Metadata /Subtype /XML /Length {xmp.Length} >>\nstream\n"));
            output.Write(xmp, 0, xmp.Length);
            Write(output, "\nendstream\nendobj\n");

            var xrefOffset = output.Position;

            // Each cross-reference entry is exactly twenty bytes: ten-digit offset, generation,
            // in-use marker and two separating characters.
            Write(output, FormattableString.Invariant(
                $"xref\n{objectNumber} 1\n{offset:D10} 00000 n \n"));

            var size = Math.Max(trailer.Size, objectNumber + 1);
            Write(output, string.Format(
                CultureInfo.InvariantCulture,
                "trailer\n<< /Size {0} /Root {1} {2} R{3}{4} /Prev {5} >>\n",
                size,
                trailer.RootObjectNumber,
                trailer.RootGeneration,
                trailer.IdEntry,
                trailer.InfoEntry,
                trailer.PreviousXrefOffset));
            Write(output, FormattableString.Invariant($"startxref\n{xrefOffset}\n%%EOF\n"));

            return output.ToArray();
        }

        private static void Write(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static PdfDictionary? ResolveDictionary(PdfItem? item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private readonly struct SavedMetadata
        {
            public SavedMetadata(int objectNumber, string text)
            {
                ObjectNumber = objectNumber;
                Text = text;
            }

            public int ObjectNumber { get; }
            public string Text { get; }
        }

        private readonly struct TrailerInfo
        {
            public TrailerInfo(
                long previousXrefOffset,
                int size,
                int rootObjectNumber,
                int rootGeneration,
                string idEntry,
                string infoEntry)
            {
                PreviousXrefOffset = previousXrefOffset;
                Size = size;
                RootObjectNumber = rootObjectNumber;
                RootGeneration = rootGeneration;
                IdEntry = idEntry;
                InfoEntry = infoEntry;
            }

            public long PreviousXrefOffset { get; }
            public int Size { get; }
            public int RootObjectNumber { get; }
            public int RootGeneration { get; }
            public string IdEntry { get; }
            public string InfoEntry { get; }
        }
    }
}
