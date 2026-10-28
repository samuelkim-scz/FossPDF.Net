using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using FossPDF.Drawing.Exceptions;
using FossPDF.Infrastructure;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Applies optional PDF post-processing with the external <c>qpdf</c> tool: additional stream
    /// compression, decompression or linearization.
    /// </summary>
    /// <remarks>
    /// qpdf is not distributed with FossPDF. The application supplies an qpdf 11.0 or newer
    /// executable through <see cref="PdfOptimizationSettings.QpdfExecutablePath"/>,
    /// <see cref="FossPDF.Settings.QpdfExecutablePath"/> or the <c>PATH</c> environment variable.
    /// The input bytes are staged in a private temporary directory and passed to qpdf as a file;
    /// no passwords or document content appear on the command line. The process is terminated on
    /// cancellation or when the configured timeout expires.
    /// </remarks>
    public static class PdfOptimizer
    {
        private static readonly Regex VersionPattern =
            new Regex(@"qpdf version (\d+)\.(\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns an optimized copy of <paramref name="document"/>. The input array is not
        /// modified. When no option is selected the input is returned unchanged and qpdf is not
        /// started.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="settings"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The document is empty or the options are contradictory.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The process timeout is not positive.</exception>
        /// <exception cref="NotSupportedException">No supported qpdf executable could be found.</exception>
        /// <exception cref="PdfOptimizationException">qpdf could not be started, failed, or timed out.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public static byte[] Optimize(
            byte[] document,
            PdfOptimizationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Optimize(document, settings, null, cancellationToken);
        }

        internal static byte[] Optimize(
            byte[] document,
            PdfOptimizationSettings settings,
            IQpdfBackend? backend,
            CancellationToken cancellationToken)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (document.Length == 0)
                throw new ArgumentException("The PDF document must not be empty.", nameof(document));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            settings.Validate();

            if (!settings.HasWork)
                return document;

            cancellationToken.ThrowIfCancellationRequested();

            var executablePath = ResolveExecutable(settings.QpdfExecutablePath);
            backend ??= new QpdfProcessBackend();

            var version = backend.GetVersion(executablePath, settings.ProcessTimeout, cancellationToken);
            EnsureSupportedVersion(version);

            var arguments = new List<string>();

            if (settings.Linearize)
                arguments.Add("--linearize");

            if (settings.DisableCompression)
            {
                arguments.Add("--stream-data=uncompress");
                arguments.Add("--object-streams=disable");
            }
            else if (settings.EnableAdditionalCompression)
            {
                arguments.Add("--object-streams=generate");
                arguments.Add("--recompress-flate");
                arguments.Add("--compression-level=9");
            }

            return backend.Run(executablePath, arguments, document, settings.ProcessTimeout, cancellationToken);
        }

        /// <summary>
        /// Resolves the configured executable, then <see cref="FossPDF.Settings.QpdfExecutablePath"/>,
        /// then the first <c>qpdf</c> or <c>qpdf.exe</c> found on <c>PATH</c>.
        /// </summary>
        internal static string ResolveExecutable(string? explicitPath)
        {
            var configured = !string.IsNullOrWhiteSpace(explicitPath)
                ? explicitPath
                : FossPDF.Settings.QpdfExecutablePath;

            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!File.Exists(configured))
                {
                    throw new NotSupportedException(
                        $"The configured qpdf executable '{configured}' does not exist. " +
                        "Install qpdf 11 or newer or correct the configured path.");
                }

                return configured;
            }

            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                foreach (var name in new[] { "qpdf", "qpdf.exe" })
                {
                    var candidate = Path.Combine(directory.Trim(), name);

                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            throw new NotSupportedException(
                "PDF optimization requires qpdf 11.0 or newer, but no qpdf executable was found on PATH. " +
                "Install qpdf, set FossPDF.Settings.QpdfExecutablePath, or set " +
                "PdfOptimizationSettings.QpdfExecutablePath.");
        }

        internal static void EnsureSupportedVersion(string versionText)
        {
            var match = VersionPattern.Match(versionText ?? string.Empty);

            if (!match.Success)
            {
                throw new NotSupportedException(
                    "The qpdf executable did not report a recognizable version. qpdf 11.0 or newer is required.");
            }

            var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            if (major < 11)
            {
                throw new NotSupportedException(
                    $"qpdf {major}.{minor} is not supported. qpdf 11.0 or newer is required for PDF optimization.");
            }
        }
    }
}
