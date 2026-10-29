using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.PdfOperations
{
    internal static class EInvoicePackaging
    {
        internal const string Guideline = "urn:cen.eu:en16931:2017";
        internal const string FileName = "factur-x.xml";
        internal static readonly XNamespace InvoiceNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
        internal static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        internal static readonly XNamespace Extension = "http://www.aiim.org/pdfa/ns/extension/";
        internal static readonly XNamespace Schema = "http://www.aiim.org/pdfa/ns/schema#";
        private static readonly XNamespace Property = "http://www.aiim.org/pdfa/ns/property#";

        internal static void CheckProfile(EInvoiceProfile profile)
        {
            if (!Enum.IsDefined(typeof(EInvoiceProfile), profile))
                throw new ArgumentOutOfRangeException(nameof(profile), "Supported profiles: Factur-X 1.09.2 EN16931 and ZUGFeRD 2.5.2 EN16931.");
        }

        internal static XDocument ParseXml(byte[] data)
        {
            using var input = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        internal static byte[] CaptureXml(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0 || data.Length > 16 * 1024 * 1024)
                throw new ArgumentException("Invoice XML must contain 1 byte to 16 MiB.", nameof(data));
            var snapshot = (byte[])data.Clone();
            try
            {
                // Mustang consumes UTF-8. Reject other encodings instead of silently changing bytes.
                new UTF8Encoding(false, true).GetString(snapshot);
                var xml = ParseXml(snapshot);
                if (xml.Declaration?.Encoding is { } encoding && !encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Invoice XML must use UTF-8 encoding.", nameof(data));
                XNamespace rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
                XNamespace ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
                var ids = xml.Root?.Element(rsm + "ExchangedDocumentContext")?
                    .Elements(ram + "GuidelineSpecifiedDocumentContextParameter").SelectMany(x => x.Elements(ram + "ID")).ToArray();
                if (xml.Root?.Name != rsm + "CrossIndustryInvoice" || ids?.Length != 1 || ids[0].Value != Guideline)
                    throw new ArgumentException("Supply CII invoice XML with the exact EN16931 guideline urn:cen.eu:en16931:2017.", nameof(data));
            }
            catch (Exception exception) when (exception is XmlException or DecoderFallbackException)
            {
                throw new ArgumentException("Invoice XML must be valid UTF-8 XML without a DTD or external entities.", nameof(data), exception);
            }
            return snapshot;
        }

        internal static string Metadata(string? custom)
        {
            var properties = new[] { ("DocumentFileName", FileName), ("DocumentType", "INVOICE"),
                ("Version", "1.0"), ("ConformanceLevel", "EN 16931") };
            var definitions = new XElement(Rdf + "Seq", properties.Select(p =>
                new XElement(Rdf + "li", new XAttribute(Rdf + "parseType", "Resource"),
                    new XElement(Property + "name", p.Item1), new XElement(Property + "valueType", "Text"),
                    new XElement(Property + "category", "external"),
                    new XElement(Property + "description", "Factur-X " + p.Item1))));
            var declarations = new XElement(Extension + "schemas", new XAttribute(XNamespace.Xmlns + "pdfaExtension", Extension),
                new XAttribute(XNamespace.Xmlns + "pdfaSchema", Schema), new XAttribute(XNamespace.Xmlns + "pdfaProperty", Property),
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                new XElement(Rdf + "Bag", new XElement(Rdf + "li", new XAttribute(Rdf + "parseType", "Resource"),
                    new XElement(Schema + "schema", "Factur-X PDF/A Extension Schema"),
                    new XElement(Schema + "namespaceURI", InvoiceNamespace.NamespaceName),
                    new XElement(Schema + "prefix", "fx"),
                    new XElement(Schema + "property", definitions))));
            var extra = custom == null ? Array.Empty<XElement>() : PdfXmpMetadata.ParseExtension(custom).ToArray();
            if (extra.Any(x => x.Name.Namespace == InvoiceNamespace || x.Descendants(Schema + "namespaceURI").Any(n => n.Value == InvoiceNamespace)))
                throw new ArgumentException("Invoice XMP properties and its extension schema are reserved.", nameof(custom));
            return string.Concat(properties.Select(p => new XElement(InvoiceNamespace + p.Item1,
                new XAttribute(XNamespace.Xmlns + "fx", InvoiceNamespace), p.Item2).ToString(SaveOptions.DisableFormatting))) +
                declarations.ToString(SaveOptions.DisableFormatting) + string.Concat(extra.Select(x => x.ToString(SaveOptions.DisableFormatting)));
        }

        internal static void ValidateOutput(byte[] document, byte[] expectedXml)
        {
            using var input = new MemoryStream(document, writable: false);
            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            var catalog = pdf.Internals.Catalog;
            var associated = catalog.Elements.GetArray("/AF");
            var candidates = associated == null ? Array.Empty<PdfDictionary>() :
                Enumerable.Range(0, associated.Elements.Count).Select(i => associated.Elements.GetDictionary(i))
                    .Where(x => x?.Elements.GetString("/UF") == FileName).Cast<PdfDictionary>().ToArray();
            if (candidates.Length != 1)
                throw Failure("associated invoice file");
            var file = candidates[0];
            if (file.Elements.GetName("/AFRelationship") != "/Alternative")
                throw Failure("Alternative relationship");
            if (file.Elements.GetString("/F") != FileName)
                throw Failure("invoice filename");
            var names = catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles")?.Elements.GetArray("/Names");
            var matchingNames = names == null || names.Elements.Count % 2 != 0 ? Array.Empty<PdfDictionary?>() :
                Enumerable.Range(0, names.Elements.Count / 2).Select(i => 2 * i)
                    .Where(i => names.Elements[i] is PdfString key && key.Value == FileName)
                    .Select(i => names.Elements.GetDictionary(i + 1)).ToArray();
            if (matchingNames.Length != 1 || !ReferenceEquals(matchingNames[0], file))
                throw Failure("shared invoice name-tree/AF reference");
            var embeddedFiles = file.Elements.GetDictionary("/EF");
            var embedded = embeddedFiles?.Elements.GetDictionary("/F");
            if (embedded == null || !ReferenceEquals(embedded, embeddedFiles?.Elements.GetDictionary("/UF")) ||
                embedded.Elements.GetName("/Subtype") != "/text/xml" ||
                embedded.Elements.GetDictionary("/Params")?.Elements.ContainsKey("/ModDate") != true ||
                !PdfXmpMetadata.ReadStream(embedded).SequenceEqual(expectedXml))
                throw Failure("exact invoice payload, MIME type and modification date");
            var metadata = catalog.Elements.GetDictionary("/Metadata") ?? throw Failure("invoice XMP");
            var packet = ParseXml(PdfXmpMetadata.ReadStream(metadata));
            var properties = packet.Descendants(Rdf + "Description")
                .Where(x => string.IsNullOrEmpty((string?)x.Attribute(Rdf + "about"))).SelectMany(x => x.Elements()).ToArray();
            foreach (var expected in new[] { ("DocumentFileName", FileName), ("DocumentType", "INVOICE"),
                ("Version", "1.0"), ("ConformanceLevel", "EN 16931") })
            {
                var values = properties.Where(x => x.Name == InvoiceNamespace + expected.Item1).ToArray();
                if (values.Length != 1 || values[0].Value != expected.Item2)
                    throw Failure("invoice XMP " + expected.Item1);
            }
            if (packet.Descendants(Schema + "namespaceURI").Count(x => x.Value == InvoiceNamespace) != 1)
                throw Failure("invoice extension schema");

            static PdfOperationException Failure(string requirement) =>
                new("The finished PDF failed the " + requirement + " packaging check. No output was written.");
        }
    }
}
