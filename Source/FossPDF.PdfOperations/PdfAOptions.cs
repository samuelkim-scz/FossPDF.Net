using System;
using System.Collections.Generic;
using FossPDF.Fluent;

namespace FossPDF.PdfOperations
{
    /// <summary>External validation and associated files for the optional .NET 9 archival writer.</summary>
    public sealed record PdfAOptions
    {
        /// <summary>Path to the unmodified veraPDF 1.30.3 cli-1.30.3.jar. Defaults to FOSSPDF_VERAPDF_JAR.</summary>
        public string? VeraPdfJarPath { get; init; }
        /// <summary>Java 8 or newer executable. Defaults to java on PATH. No shell is used.</summary>
        public string JavaExecutablePath { get; init; } = "java";
        /// <summary>Maximum duration of each validator invocation. Defaults to two minutes.</summary>
        public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(2);
        /// <summary>Associated files; supported only by PDF/A-3. Content is copied by PdfAAttachment.</summary>
        public IReadOnlyList<PdfAAttachment> Attachments { get; init; } = Array.Empty<PdfAAttachment>();
        /// <summary>
        /// Namespace-qualified XMP property elements. Custom properties need a PDF/A extension
        /// schema declaration. Reserved properties cannot be replaced; veraPDF checks the result.
        /// </summary>
        public string? MetadataExtensions { get; init; }
    }

    /// <summary>An owned snapshot of an associated file and its metadata.</summary>
    public sealed class PdfAAttachment
    {
        internal AttachmentInput Input { get; }

        /// <summary>
        /// Copies bytes and metadata immediately. ModificationDate is required for PDF/A-3.
        /// All five DocumentAttachmentRelationship values are supported.
        /// </summary>
        public PdfAAttachment(byte[] data, DocumentOperation.DocumentAttachment metadata)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var input = DocumentOperation.CaptureAttachment(metadata);
            if (!input.ModificationDate.HasValue)
                throw new ArgumentException("A PDF/A associated file requires ModificationDate.", nameof(metadata));
            Input = input with { Data = (byte[])data.Clone() };
        }
    }
}
