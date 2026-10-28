using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    public sealed partial class DocumentOperation
    {
        private readonly List<AttachmentInput> _attachments = new();
        private readonly List<XElement> _metadataExtensions = new();

        /// <summary>Describes how an embedded file relates to the document.</summary>
        public enum DocumentAttachmentRelationship { Data, Source, Alternative, Supplement, Unspecified }

        /// <summary>Describes an attachment. Values and file content are captured when added.</summary>
        public sealed class DocumentAttachment
        {
            public string FilePath { get; set; } = string.Empty;
            public string? Key { get; set; }
            public string? AttachmentName { get; set; }
            public string? MimeType { get; set; }
            public string? Description { get; set; }
            public DateTime? CreationDate { get; set; }
            public DateTime? ModificationDate { get; set; }
            public bool Replace { get; set; }
            public DocumentAttachmentRelationship Relationship { get; set; } = DocumentAttachmentRelationship.Unspecified;
        }

        /// <summary>Reads and records an attachment file immediately.</summary>
        public DocumentOperation AddAttachment(DocumentAttachment attachment)
        {
            if (attachment == null)
                throw new ArgumentNullException(nameof(attachment));
            var input = CaptureAttachment(attachment);
            var data = ReadFile(attachment.FilePath, nameof(attachment.FilePath));
            _attachments.Add(input with
            {
                Data = data,
                CreationDate = input.CreationDate ?? File.GetCreationTimeUtc(attachment.FilePath),
                ModificationDate = input.ModificationDate ?? File.GetLastWriteTimeUtc(attachment.FilePath)
            });
            return this;
        }

        /// <summary>Copies attachment bytes. An attachment name or file path must supply a display name.</summary>
        public DocumentOperation AddAttachment(byte[] data, DocumentAttachment attachment)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var input = CaptureAttachment(attachment);
            _attachments.Add(input with { Data = (byte[])data.Clone() });
            return this;
        }

        /// <summary>Reads a borrowed stream from its current position and leaves it open.</summary>
        public DocumentOperation AddAttachment(Stream stream, DocumentAttachment attachment)
        {
            var input = CaptureAttachment(attachment);
            _attachments.Add(input with { Data = ReadStreamAllowEmpty(stream) });
            return this;
        }

        /// <summary>
        /// Adds namespace-qualified XML properties to the primary document's XMP description.
        /// Existing properties and namespaces are preserved; duplicate property names fail on save.
        /// </summary>
        public DocumentOperation ExtendMetadata(string metadata)
        {
            _metadataExtensions.AddRange(PdfXmpMetadata.ParseExtension(metadata));
            return this;
        }

        private static AttachmentInput CaptureAttachment(DocumentAttachment attachment)
        {
            if (attachment == null)
                throw new ArgumentNullException(nameof(attachment));
            var fileName = Path.GetFileName(attachment.FilePath);
            var name = attachment.AttachmentName ?? fileName;
            var key = attachment.Key ?? (string.IsNullOrEmpty(fileName) ? name : fileName);
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0)
                throw new ArgumentException("An attachment needs a non-empty display name without null characters.", nameof(attachment));
            if (string.IsNullOrWhiteSpace(key) || key.IndexOf('\0') >= 0)
                throw new ArgumentException("An attachment needs a non-empty replacement key without null characters.", nameof(attachment));
            var mime = attachment.MimeType ?? "application/octet-stream";
            if (!Regex.IsMatch(mime, @"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+/[!#$%&'*+\-.^_`|~0-9A-Za-z]+$"))
                throw new ArgumentException("The attachment MIME type must contain a valid type and subtype.", nameof(attachment));
            if (!Enum.IsDefined(typeof(DocumentAttachmentRelationship), attachment.Relationship))
                throw new ArgumentOutOfRangeException(nameof(attachment), "The attachment relationship is not supported.");
            return new AttachmentInput(Array.Empty<byte>(), key, name, mime, attachment.Description,
                attachment.CreationDate, attachment.ModificationDate, attachment.Replace, attachment.Relationship.ToString());
        }

        private static byte[] ReadStreamAllowEmpty(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("The attachment stream must be readable.", nameof(stream));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}

namespace FossPDF.PdfOperations
{
    internal sealed record AttachmentInput(byte[] Data, string Key, string Name, string MimeType,
        string? Description, DateTime? CreationDate, DateTime? ModificationDate, bool Replace, string? Relationship);
}
