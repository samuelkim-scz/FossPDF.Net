using System;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// A concrete PDF/A part and conformance level written into the document's XMP identification
    /// packet. Only the profiles validated by this package are represented.
    /// </summary>
    internal readonly struct PdfAProfile
    {
        private PdfAProfile(int part, char conformance)
        {
            Part = part;
            Conformance = conformance;
        }

        /// <summary>The PDF/A part: 2 or 3.</summary>
        public int Part { get; }

        /// <summary>The conformance level: <c>A</c>, <c>B</c> or <c>U</c>.</summary>
        public char Conformance { get; }

        /// <summary>Accessible profiles require a complete structure tree and a document language.</summary>
        public bool IsAccessible => Conformance == 'A';

        /// <summary>Only PDF/A-3 permits embedded files.</summary>
        public bool AllowsAttachments => Part == 3;

        public string DisplayName => $"PDF/A-{Part}{char.ToLowerInvariant(Conformance)}";

        public static bool IsSelectable(PdfAConformance conformance)
        {
            return conformance == PdfAConformance.PdfA2A ||
                   conformance == PdfAConformance.PdfA2B ||
                   conformance == PdfAConformance.PdfA2U ||
                   conformance == PdfAConformance.PdfA3A ||
                   conformance == PdfAConformance.PdfA3B ||
                   conformance == PdfAConformance.PdfA3U;
        }

        /// <summary>
        /// Resolves the profile for an output document. An explicitly requested level wins, then the
        /// level selected through <see cref="DocumentSettings.PdfAConformance"/>, then the legacy
        /// <see cref="DocumentMetadata.PdfA"/> flag.
        /// </summary>
        /// <remarks>
        /// The legacy flag has no part or conformance information, so it maps to PDF/A-2B: it permits
        /// transparency and embedded fonts and is the closest validated equivalent. The legacy flag
        /// alone is never reported as a specific profile by any API; the written XMP always states
        /// the resolved level.
        /// </remarks>
        public static PdfAProfile? Resolve(
            PdfAConformance? requested,
            PdfAConformance? configured,
            bool legacyFlag)
        {
            if (requested.HasValue)
                return FromConformance(requested.Value);

            if (configured.HasValue)
                return FromConformance(configured.Value);

            return legacyFlag ? new PdfAProfile(2, 'B') : (PdfAProfile?)null;
        }

        public static PdfAProfile? FromConformance(PdfAConformance conformance)
        {
            switch (conformance)
            {
                case PdfAConformance.PdfA2A:
                    return new PdfAProfile(2, 'A');
                case PdfAConformance.PdfA2B:
                    return new PdfAProfile(2, 'B');
                case PdfAConformance.PdfA2U:
                    return new PdfAProfile(2, 'U');
                case PdfAConformance.PdfA3A:
                    return new PdfAProfile(3, 'A');
                case PdfAConformance.PdfA3B:
                    return new PdfAProfile(3, 'B');
                case PdfAConformance.PdfA3U:
                    return new PdfAProfile(3, 'U');
                case PdfAConformance.Legacy:
                    return new PdfAProfile(2, 'B');
                case PdfAConformance.None:
                default:
                    return null;
            }
        }
    }
}
