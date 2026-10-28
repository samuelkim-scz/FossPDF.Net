using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Security;

namespace FossPDF.PdfOperations
{
    internal static class PdfXmpMetadata
    {
        private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private static readonly XNamespace Xmp = "adobe:ns:meta/";

        internal static IReadOnlyList<XElement> ParseExtension(string xml)
        {
            if (xml == null)
                throw new ArgumentNullException(nameof(xml));
            try
            {
                var wrapper = Parse("<extension>" + xml + "</extension>").Root!;
                var properties = wrapper.Elements().ToArray();
                if (properties.Length == 0 || wrapper.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value)) ||
                    properties.Any(x => x.Name.Namespace == XNamespace.None || x.Name.Namespace == Rdf || x.Name.Namespace == Xmp))
                    throw new ArgumentException("Metadata extensions must contain namespace-qualified property elements, without RDF or XMP wrappers.", nameof(xml));
                return properties;
            }
            catch (XmlException exception)
            {
                throw new ArgumentException("The metadata extension is not a valid XML property fragment.", nameof(xml), exception);
            }
        }

        internal static byte[] ReadStream(PdfDictionary dictionary)
        {
            var stream = dictionary.Stream ?? throw new PdfOperationException("A required PDF stream is missing.");
            // PdfReader has already applied crypt filters while loading indirect objects. Remove
            // that security stage before asking its compression decoder for the remaining data.
            var filters = Items(dictionary.Elements["/Filter"]);
            if (filters.Any(x => x is PdfName name && name.Value == "/Crypt"))
            {
                var parameters = Items(dictionary.Elements["/DecodeParms"]);
                if (dictionary.Owner.SecurityHandler.Elements.GetInteger("/V") == 0)
                {
                    for (var index = 0; index < filters.Length; index++)
                        if (filters[index] is PdfName name && name.Value == "/Crypt" &&
                            (index >= parameters.Length || parameters[index] is not PdfDictionary crypt ||
                             crypt.Elements.GetName("/Name") != "/Identity"))
                            throw new PdfOperationException("An unprotected PDF declares a non-identity crypt filter.");
                }
                dictionary.Owner.SecurityHandler.ResetCryptFilter(dictionary);
            }
            if (stream.IsFiltered() && !stream.TryUncompress())
                throw new PdfOperationException("An attachment or metadata stream uses an unsupported filter.");
            return stream.UnfilteredValue ?? Array.Empty<byte>();

            static PdfItem[] Items(PdfItem? item)
            {
                if (item is PdfReference reference)
                    item = reference.Value;
                return item is PdfArray array ? array.Elements.Select(x => x is PdfReference value ? value.Value : x).ToArray()
                    : item == null ? Array.Empty<PdfItem>() : new[] { item };
            }
        }

        private static XDocument Parse(string xml)
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        internal static byte[] PreserveAndExtend(byte[] saved, PdfDocument source, PdfDocument destination,
            IReadOnlyList<XElement> extensions, bool encrypted, bool encryptMetadata)
        {
            var original = source.Internals.Catalog.Elements.GetDictionary("/Metadata");
            if (original == null && extensions.Count == 0)
                return saved;
            if (original != null && extensions.Count == 0)
                return AppendMetadataRevision(saved, destination, ReadStream(original), encrypted, encryptMetadata);
            XDocument metadata;
            try
            {
                if (original != null)
                {
                    using var input = new MemoryStream(ReadStream(original), writable: false);
                    using var reader = XmlReader.Create(input, new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = 16 * 1024 * 1024
                    });
                    metadata = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                }
                else
                    metadata = Create(destination.Info);
            }
            catch (XmlException exception)
            {
                throw new PdfOperationException("The primary PDF contains malformed or unsupported XMP metadata.", exception);
            }

            var rdf = metadata.Root?.Name == Rdf + "RDF" ? metadata.Root : metadata.Root?.Element(Rdf + "RDF");
            if (rdf == null || (metadata.Root!.Name != Xmp + "xmpmeta" && metadata.Root.Name != Rdf + "RDF"))
                throw new PdfOperationException("The primary PDF metadata does not contain a supported XMP/RDF packet.");
            var descriptions = rdf.Elements(Rdf + "Description")
                .Where(x => string.IsNullOrEmpty((string?)x.Attribute(Rdf + "about"))).ToList();
            var target = descriptions.FirstOrDefault();
            if (target == null)
            {
                target = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty));
                rdf.Add(target);
                descriptions.Add(target);
            }
            var names = descriptions.SelectMany(x => x.Elements().Select(y => y.Name)
                .Concat(x.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name != Rdf + "about").Select(a => a.Name)))
                .ToHashSet();
            foreach (var property in extensions)
            {
                if (!names.Add(property.Name))
                    throw new PdfOperationException($"The metadata property '{property.Name}' already exists.");
                target.Add(new XElement(property));
            }

            var bytes = Encoding.UTF8.GetBytes(metadata.ToString(SaveOptions.DisableFormatting));
            if (bytes.Length > 16 * 1024 * 1024)
                throw new PdfOperationException("The extended XMP packet exceeds the 16 MiB output limit.");
            return AppendMetadataRevision(saved, destination, bytes, encrypted, encryptMetadata);
        }

        private static XDocument Create(PdfDocumentInformation info)
        {
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
            XNamespace pdf = "http://ns.adobe.com/pdf/1.3/";
            return new XDocument(new XElement(Xmp + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", Xmp),
                new XElement(Rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                    new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty),
                        new XElement(dc + "title", new XElement(Rdf + "Alt", new XElement(Rdf + "li",
                            new XAttribute(XNamespace.Xml + "lang", "x-default"), info.Title))),
                        new XElement(dc + "creator", new XElement(Rdf + "Seq", new XElement(Rdf + "li", info.Author))),
                        new XElement(dc + "description", new XElement(Rdf + "Alt", new XElement(Rdf + "li",
                            new XAttribute(XNamespace.Xml + "lang", "x-default"), info.Subject))),
                        new XElement(pdf + "Keywords", info.Keywords), new XElement(pdf + "Producer", info.Producer),
                        new XElement(xmp + "CreatorTool", info.Creator),
                        new XElement(xmp + "CreateDate", info.CreationDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
                        new XElement(xmp + "ModifyDate", info.ModificationDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))))));
        }

        internal static byte[] ApplyArchivalIdentification(byte[] saved, PdfDocument document, string flavour,
            IReadOnlyList<XElement> extensions)
        {
            var packet = Create(document.Info);
            var description = packet.Root!.Element(Rdf + "RDF")!.Element(Rdf + "Description")!;
            XNamespace pdfaid = "http://www.aiim.org/pdfa/ns/id/";
            description.Add(new XAttribute(XNamespace.Xmlns + "pdfaid", pdfaid),
                new XElement(pdfaid + "part", flavour[0].ToString()),
                new XElement(pdfaid + "conformance", flavour[1].ToString().ToUpperInvariant()));
            var names = description.Elements().Select(x => x.Name).ToHashSet();
            foreach (var property in extensions)
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException($"The metadata property '{property.Name}' already exists.", nameof(extensions));
                description.Add(new XElement(property));
            }
            var bytes = Encoding.UTF8.GetBytes(packet.ToString(SaveOptions.DisableFormatting));
            if (bytes.Length > 16 * 1024 * 1024)
                throw new PdfOperationException("The archival XMP packet exceeds the 16 MiB output limit.");
            return AppendMetadataRevision(saved, document, bytes, false, false);
        }

        private static byte[] AppendMetadataRevision(byte[] saved, PdfDocument document, byte[] data, bool encrypted, bool encryptMetadata)
        {
            // PDFsharp 6.2.4 replaces /Metadata during Save. Append one bounded incremental
            // revision to our own freshly serialized file; the pages/resources stay untouched.
            var metadata = document.Internals.Catalog.Elements.GetDictionary("/Metadata")
                ?? throw new PdfOperationException("The backend did not write an XMP metadata object.");
            var objectId = PdfInternals.GetObjectID(metadata);
            var number = PdfInternals.GetObjectNumber(metadata);
            var generation = PdfInternals.GenerationNumber(metadata);
            if (encrypted && encryptMetadata)
            {
                try
                {
                    EnterObject(document.SecurityHandler, objectId);
                    try { EncryptStream(document.SecurityHandler, ref data, metadata); }
                    finally { LeaveObject(document.SecurityHandler); }
                }
                catch (Exception exception)
                {
                    throw new PdfOperationException("The pinned PDF backend could not protect the extended metadata.", exception);
                }
            }
            var tail = Encoding.ASCII.GetString(saved, Math.Max(0, saved.Length - 2048), Math.Min(saved.Length, 2048));
            var match = Regex.Match(tail, @"startxref\s+(\d+)\s+%%EOF\s*$", RegexOptions.CultureInvariant);
            if (!match.Success || number <= 0)
                throw new PdfOperationException("The backend output has an unsupported cross-reference trailer.");
            var size = document.Internals.GetAllObjects().Max(PdfInternals.GetObjectNumber) + 1;
            using var output = new MemoryStream();
            output.Write(saved, 0, saved.Length);
            Write("\n");
            var objectOffset = output.Position;
            Write($"{number} {generation} obj\n<< /Type /Metadata /Subtype /XML /Length {data.Length}");
            if (encrypted && !encryptMetadata)
                Write(" /Filter /Crypt /DecodeParms << /Type /CryptFilterDecodeParms /Name /Identity >>");
            Write(" >>\nstream\n");
            output.Write(data, 0, data.Length);
            Write("\nendstream\nendobj\n");
            var xref = output.Position;
            Write($"xref\n0 1\n0000000000 65535 f \n{number} 1\n{objectOffset.ToString("D10", CultureInfo.InvariantCulture)} {generation.ToString("D5", CultureInfo.InvariantCulture)} n \n");
            Write($"trailer\n<< /Size {size} /Root {Reference(document.Internals.Catalog)} /Info {Reference(document.Info)} /Prev {match.Groups[1].Value}");
            if (encrypted)
                Write($" /Encrypt {Reference(document.SecurityHandler)}");
            Write($" /ID [<{Hex(document.Internals.FirstDocumentID)}><{Hex(document.Internals.SecondDocumentID)}>] >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
            return output.ToArray();

            void Write(string value)
            {
                var bytes = Encoding.ASCII.GetBytes(value);
                output.Write(bytes, 0, bytes.Length);
            }
            static string Reference(PdfObject value) => $"{PdfInternals.GetObjectNumber(value)} {PdfInternals.GenerationNumber(value)} R";
            static string Hex(string value) => Convert.ToHexString(Encoding.Latin1.GetBytes(value));
        }

        // Exact managed ABI bridge for the pinned net9.0 backend. It uses the backend's own
        // initialized security handler, including its EncryptMetadata policy, rather than
        // implementing password derivation or ciphers. No reflection or runtime code generation.
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "EnterObject")]
        private static extern void EnterObject(PdfStandardSecurityHandler handler, PdfObjectID id);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "LeaveObject")]
        private static extern void LeaveObject(PdfStandardSecurityHandler handler);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "EncryptStream")]
        private static extern void EncryptStream(PdfStandardSecurityHandler handler, ref byte[] data, PdfDictionary dictionary);
    }
}
