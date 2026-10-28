using System;
using System.Collections.Generic;
using FossPDF.Fluent;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Additional content written into a PDF/A document. Attachments are embedded files listed in
    /// the document's associated-files array; they are permitted only in PDF/A-3.
    /// </summary>
    /// <remarks>
    /// The attachment files are read when the PDF/A document is generated, so later changes to a
    /// file do not affect an output that was already written. When an attachment does not set a
    /// modification date, the document's modification date is used, because PDF/A-3 requires a
    /// modification date for every embedded file.
    /// </remarks>
    public sealed class PdfAOptions
    {
        /// <summary>
        /// Embedded files to write. The list is copied when the document is generated.
        /// </summary>
        public IReadOnlyList<DocumentOperation.DocumentAttachment> Attachments { get; init; } =
            Array.Empty<DocumentOperation.DocumentAttachment>();
    }
}
