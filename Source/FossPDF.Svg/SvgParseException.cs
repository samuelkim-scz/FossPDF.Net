using System;

namespace FossPDF.Svg
{
    /// <summary>
    /// Reports input that cannot be accepted as a self-contained SVG image.
    /// The message identifies the rejected construct; the inner exception, when present,
    /// preserves the parser's original failure.
    /// </summary>
    public sealed class SvgParseException : Exception
    {
        public SvgParseException(string message)
            : base(message)
        {
        }

        public SvgParseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
