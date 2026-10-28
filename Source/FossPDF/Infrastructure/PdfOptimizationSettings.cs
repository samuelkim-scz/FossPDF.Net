using System;

namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Selects optional post-processing for a PDF document: additional stream compression,
    /// decompression, or linearization. The options are applied by the external <c>qpdf</c> tool,
    /// which the application must provide; see <see cref="FossPDF.Drawing.PdfOptimizer"/>.
    /// </summary>
    /// <remarks>
    /// The same selections are available per document through
    /// <see cref="DocumentSettings.CompressDocument"/>, <see cref="DocumentSettings.EnableAdditionalCompression"/>
    /// and <see cref="DocumentSettings.Linearize"/>.
    /// </remarks>
    public sealed record PdfOptimizationSettings
    {
        /// <summary>
        /// Recompresses Flate streams at the maximum compression level and generates compressed
        /// object streams. Usually reduces size, but costs processing time and cannot reduce data
        /// that is already incompressible.
        /// </summary>
        public bool EnableAdditionalCompression { get; init; }

        /// <summary>
        /// Decompresses decodable streams and disables object streams. The output is larger but
        /// easier to inspect. Cannot be combined with <see cref="EnableAdditionalCompression"/>.
        /// </summary>
        public bool DisableCompression { get; init; }

        /// <summary>Produces a linearized (web-optimized) document.</summary>
        public bool Linearize { get; init; }

        /// <summary>
        /// The qpdf executable to use. When <see langword="null"/>,
        /// <see cref="FossPDF.Settings.QpdfExecutablePath"/> is used and then the <c>PATH</c>
        /// environment variable.
        /// </summary>
        public string? QpdfExecutablePath { get; init; }

        /// <summary>How long a qpdf invocation may run. Defaults to two minutes.</summary>
        public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(2);

        internal bool HasWork => EnableAdditionalCompression || DisableCompression || Linearize;

        internal void Validate()
        {
            if (DisableCompression && EnableAdditionalCompression)
            {
                throw new ArgumentException(
                    "Additional compression cannot be combined with disabling compression.",
                    nameof(PdfOptimizationSettings));
            }

            if (QpdfExecutablePath != null && string.IsNullOrWhiteSpace(QpdfExecutablePath))
            {
                throw new ArgumentException(
                    "A qpdf executable path must not be empty.", nameof(PdfOptimizationSettings));
            }

            if (ProcessTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ProcessTimeout), "The qpdf process timeout must be positive.");
            }
        }
    }
}
