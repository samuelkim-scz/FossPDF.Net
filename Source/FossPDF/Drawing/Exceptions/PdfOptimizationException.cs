using System;

namespace FossPDF.Drawing.Exceptions
{
    /// <summary>
    /// The external qpdf optimization step could not produce a document. The inner exception, when
    /// present, carries the process failure. Messages never contain passwords; this API does not
    /// pass any.
    /// </summary>
    public class PdfOptimizationException : Exception
    {
        internal PdfOptimizationException(string message) : base(message)
        {
        }

        internal PdfOptimizationException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
