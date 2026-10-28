using System;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Raised when an existing-PDF operation cannot be completed: the input is malformed or
    /// encrypted beyond the supplied password, a page selector references pages outside the
    /// document, or the selection produces no pages.
    /// </summary>
    public sealed class PdfOperationException : Exception
    {
        public PdfOperationException(string message)
            : base(message)
        {
        }

        public PdfOperationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
