using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Writes embedded files to the destination document. Each attachment is stored as an embedded
    /// file stream referenced by a file specification, listed in the catalog's embedded-files name
    /// tree and in its associated-files array (<c>/AF</c>).
    /// </summary>
    /// <remarks>
    /// Attachments carried by the primary loaded document are re-created in the output, because the
    /// destination is rebuilt from imported pages and therefore starts without a catalog-level name
    /// tree. Attachments added by the operation are applied afterwards, in call order, and can
    /// replace an attachment that is already present.
    /// </remarks>
    internal static class PdfAttachments
    {
        private const string DefaultMimeType = "application/octet-stream";

        public static void Apply(
            PdfDocument destination,
            PdfDocument primary,
            IReadOnlyList<DocumentOperation.AttachmentOperation> additions)
        {
            var attachments = ReadExisting(primary);

            foreach (var addition in additions)
            {
                if (attachments.ContainsKey(addition.Key) && !addition.Replace)
                {
                    throw new PdfOperationException(
                        $"The document already contains an attachment with the key '{addition.Key}'. " +
                        "Set Replace to true to replace it instead of losing one of the attachments.");
                }

                attachments[addition.Key] = addition;
            }

            if (attachments.Count == 0)
                return;

            Write(destination, attachments);
        }

        private static Dictionary<string, DocumentOperation.AttachmentOperation> ReadExisting(PdfDocument document)
        {
            var attachments = new Dictionary<string, DocumentOperation.AttachmentOperation>(StringComparer.Ordinal);
            var catalog = document.Internals.Catalog;

            if (catalog == null || !catalog.Elements.ContainsKey("/Names"))
                return attachments;

            var names = catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            if (entries == null)
                return attachments;

            for (var index = 0; index + 1 < entries.Elements.Count; index += 2)
            {
                if (!(entries.Elements[index] is PdfString key))
                    continue;

                var specification = ResolveDictionary(entries.Elements[index + 1]);

                if (specification == null)
                {
                    throw new PdfOperationException(
                        $"The attachment '{key.Value}' could not be read. Its file specification is missing or malformed.");
                }

                attachments[key.Value] = ReadEntry(key.Value, specification);
            }

            return attachments;
        }

        private static DocumentOperation.AttachmentOperation ReadEntry(string key, PdfDictionary specification)
        {
            var name = specification.Elements.GetString("/UF");

            if (string.IsNullOrEmpty(name))
                name = specification.Elements.GetString("/F");

            if (string.IsNullOrEmpty(name))
                name = key;

            var description = specification.Elements.GetString("/Desc");
            var relationship = ParseRelationship(specification.Elements.GetName("/AFRelationship"));

            var files = specification.Elements.GetDictionary("/EF");
            var streamDictionary = ResolveDictionary(files?.Elements.GetObject("/F"))
                ?? ResolveDictionary(files?.Elements.GetObject("/UF"));

            if (streamDictionary?.Stream == null)
            {
                throw new PdfOperationException(
                    $"The attachment '{key}' could not be read. Its embedded file stream is missing.");
            }

            if (streamDictionary.Stream.IsFiltered() && !streamDictionary.Stream.TryUncompress())
            {
                throw new PdfOperationException(
                    $"The attachment '{key}' uses an embedded file stream filter that is not supported.");
            }

            var data = streamDictionary.Stream.UnfilteredValue ?? Array.Empty<byte>();
            var mimeType = NormalizeMimeType(streamDictionary.Elements.GetName("/Subtype"));
            var parameters = streamDictionary.Elements.GetDictionary("/Params");

            return new DocumentOperation.AttachmentOperation(
                key,
                data,
                name,
                ReadDate(parameters, "/CreationDate"),
                ReadDate(parameters, "/ModDate"),
                mimeType,
                string.IsNullOrEmpty(description) ? null : description,
                replace: false,
                relationship);
        }

        private static void Write(
            PdfDocument document,
            Dictionary<string, DocumentOperation.AttachmentOperation> attachments)
        {
            var catalog = document.Internals.Catalog;

            if (catalog == null)
                throw new PdfOperationException("The output PDF does not expose a document catalog.");

            // Name-tree keys must be sorted so readers can search them; ordinal ordering over the
            // logical strings matches the byte ordering of the encoded keys.
            var keys = attachments.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var specifications = new Dictionary<string, PdfReference>(StringComparer.Ordinal);

            foreach (var key in keys)
                specifications.Add(key, CreateSpecification(document, key, attachments[key]));

            var names = catalog.Elements.ContainsKey("/Names")
                ? catalog.Elements.GetDictionary("/Names")
                : null;

            if (names == null)
            {
                names = new PdfDictionary(document);
                catalog.Elements.SetObject("/Names", names);
            }

            var embedded = names.Elements.ContainsKey("/EmbeddedFiles")
                ? names.Elements.GetDictionary("/EmbeddedFiles")
                : null;

            if (embedded == null)
            {
                embedded = new PdfDictionary(document);
                names.Elements.SetObject("/EmbeddedFiles", embedded);
            }

            var nameArray = new PdfArray(document);

            foreach (var key in keys)
            {
                nameArray.Elements.Add(new PdfString(key));
                nameArray.Elements.Add(specifications[key]);
            }

            embedded.Elements.SetObject("/Names", nameArray);

            var associatedFiles = new PdfArray(document);

            foreach (var key in keys)
                associatedFiles.Elements.Add(specifications[key]);

            catalog.Elements.SetObject("/AF", associatedFiles);
        }

        private static PdfReference CreateSpecification(
            PdfDocument document,
            string key,
            DocumentOperation.AttachmentOperation attachment)
        {
            var stream = new PdfDictionary(document);
            stream.Elements.SetName("/Type", "/EmbeddedFile");
            stream.Elements.SetName("/Subtype", "/" + attachment.MimeType);

            var parameters = new PdfDictionary(document);
            parameters.Elements.SetInteger("/Size", attachment.Data.Length);

            if (attachment.CreationDate.HasValue)
                parameters.Elements.SetDateTime("/CreationDate", attachment.CreationDate.Value);

            if (attachment.ModificationDate.HasValue)
                parameters.Elements.SetDateTime("/ModDate", attachment.ModificationDate.Value);

            stream.Elements.SetObject("/Params", parameters);
            stream.CreateStream(attachment.Data);
            document.Internals.AddObject(stream);

            var streamReference = PdfInternals.GetReference(stream)
                ?? throw new PdfOperationException($"The attachment '{key}' could not be registered.");

            var specification = new PdfDictionary(document);
            specification.Elements.SetName("/Type", "/Filespec");
            // /F is the PDFDocEncoding name. Characters it cannot represent are replaced by '?';
            // /UF carries the exact Unicode name.
            specification.Elements.SetString("/F", ToAscii(attachment.Name));
            specification.Elements.SetString("/UF", attachment.Name);

            if (!string.IsNullOrEmpty(attachment.Description))
                specification.Elements.SetString("/Desc", attachment.Description);

            specification.Elements.SetName("/AFRelationship", "/" + GetRelationshipName(attachment.Relationship));

            var files = new PdfDictionary(document);
            files.Elements.SetReference("/F", streamReference);
            files.Elements.SetReference("/UF", streamReference);
            specification.Elements.SetObject("/EF", files);

            document.Internals.AddObject(specification);

            return PdfInternals.GetReference(specification)
                ?? throw new PdfOperationException($"The attachment '{key}' could not be registered.");
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

        private static DateTime? ReadDate(PdfDictionary? parameters, string key)
        {
            if (parameters == null || !parameters.Elements.ContainsKey(key))
                return null;

            var value = parameters.Elements.GetDateTime(key, DateTime.MinValue);
            return value.Year > 1 ? value : (DateTime?)null;
        }

        private static string NormalizeMimeType(string value)
        {
            if (string.IsNullOrEmpty(value))
                return DefaultMimeType;

            return value[0] == '/' ? value.Substring(1) : value;
        }

        private static string GetRelationshipName(DocumentOperation.DocumentAttachmentRelationship relationship)
        {
            switch (relationship)
            {
                case DocumentOperation.DocumentAttachmentRelationship.Data:
                    return "Data";
                case DocumentOperation.DocumentAttachmentRelationship.Source:
                    return "Source";
                case DocumentOperation.DocumentAttachmentRelationship.Alternative:
                    return "Alternative";
                case DocumentOperation.DocumentAttachmentRelationship.Supplement:
                    return "Supplement";
                default:
                    return "Unspecified";
            }
        }

        private static DocumentOperation.DocumentAttachmentRelationship ParseRelationship(string value)
        {
            if (value.StartsWith("/", StringComparison.Ordinal))
                value = value.Substring(1);

            switch (value)
            {
                case "Data":
                    return DocumentOperation.DocumentAttachmentRelationship.Data;
                case "Source":
                    return DocumentOperation.DocumentAttachmentRelationship.Source;
                case "Alternative":
                    return DocumentOperation.DocumentAttachmentRelationship.Alternative;
                case "Supplement":
                    return DocumentOperation.DocumentAttachmentRelationship.Supplement;
                default:
                    return DocumentOperation.DocumentAttachmentRelationship.Unspecified;
            }
        }

        private static string ToAscii(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
                builder.Append(character >= 0x20 && character <= 0x7E ? character : '?');

            return builder.ToString();
        }
    }
}
