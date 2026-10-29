using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Infrastructure;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Validates bytes using the unmodified veraPDF 1.30.3 CLI and its bundled profiles.
    /// Java and veraPDF are deployed by the application, never downloaded by this library.
    /// </summary>
    public static class PdfAValidator
    {
        public const string Version = "1.30.3";
        internal const string JarSha256 = "19F6EDD5C92841A38D9B3B018A555B7BA173B13E4E0D29EDECC232E5BA6C1D70";

        /// <summary>
        /// Validates one PDF, including existing legacy output. A nonconforming file returns a
        /// negative report; missing tools, process failures and malformed reports throw.
        /// Temporary document files are private and removed on success, failure or cancellation.
        /// </summary>
        public static PdfAValidationReport Validate(byte[] document, PdfAConformance profile,
            PdfAOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.Length == 0)
                throw new ArgumentException("The PDF must not be empty.", nameof(document));
            Flavour(profile);
            options ??= new PdfAOptions();
            var jar = Prepare(options, cancellationToken);
            return ValidatePrepared(document, profile, options, jar, cancellationToken);
        }

        internal static string Prepare(PdfAOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.ProcessTimeout <= TimeSpan.Zero || options.ProcessTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options.ProcessTimeout));
            if (string.IsNullOrWhiteSpace(options.JavaExecutablePath))
                throw new ArgumentException("A Java executable is required.", nameof(options));
            var path = options.VeraPdfJarPath ?? Environment.GetEnvironmentVariable("FOSSPDF_VERAPDF_JAR");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new NotSupportedException("Conformance generation requires veraPDF 1.30.3. Set PdfAOptions/PdfUaOptions.VeraPdfJarPath or FOSSPDF_VERAPDF_JAR to cli-1.30.3.jar, and install Java 8 or newer.");
            path = Path.GetFullPath(path);
            using (var input = File.OpenRead(path))
                if (Convert.ToHexString(SHA256.HashData(input)) != JarSha256)
                    throw new NotSupportedException("The veraPDF JAR does not match the qualified 1.30.3 release. Use the unmodified official cli-1.30.3.jar; see the PDF/A deployment guide.");
            return path;
        }

        internal static PdfAValidationReport ValidatePrepared(byte[] document, PdfAConformance profile,
            PdfAOptions options, string jar, CancellationToken cancellationToken)
            => new(ReadReport(document, Flavour(profile), options, jar, cancellationToken), profile);

        internal static string ReadReport(byte[] document, string flavour,
            PdfAOptions options, string jar, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Path.GetTempPath(), "fosspdf-pdfa-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(directory);
            else
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, "document.pdf");
                File.WriteAllBytes(path, document);
                var start = new ProcessStartInfo(options.JavaExecutablePath);
                foreach (var argument in new[] { "-Xmx512m", "-jar", jar, "--format", "json", "--maxfailuresdisplayed", "5", "--flavour", flavour, path })
                    start.ArgumentList.Add(argument);
                var json = RunProcess(start, options.ProcessTimeout, cancellationToken);
                return json;
            }
            finally
            {
                try { Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static string RunProcess(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancellationToken)
        {
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.Start(start) ?? throw new PdfOperationException("The veraPDF Java process could not start.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(timeout);
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try
                {
                    process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    process.WaitForExit();
                    Task.WhenAll(output, error).GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new PdfOperationException("veraPDF exceeded PdfAOptions.ProcessTimeout. No output was written.");
                }
                Task.WhenAll(output, error).GetAwaiter().GetResult();
                // 0 means conforming, 1 nonconforming. Other codes denote process/parser errors.
                // Even exit 1 requires a complete normal report; an out-of-memory failure cannot pass.
                if (process.ExitCode != 0 && process.ExitCode != 1)
                    throw new PdfOperationException($"veraPDF failed with exit code {process.ExitCode}. No output was written.");
                cancellationToken.ThrowIfCancellationRequested();
                return output.Result;
            }
            catch (Win32Exception exception)
            {
                throw new PdfOperationException("Java could not be started. Install Java 8 or newer or set PdfAOptions.JavaExecutablePath.", exception);
            }
        }

        internal static string Flavour(PdfAConformance profile) => profile switch
        {
            PdfAConformance.Legacy or PdfAConformance.PdfA2B => "2b",
            PdfAConformance.PdfA2A => "2a",
            PdfAConformance.PdfA2U => "2u",
            PdfAConformance.PdfA3A => "3a",
            PdfAConformance.PdfA3B => "3b",
            PdfAConformance.PdfA3U => "3u",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), "Choose one of the six PDF/A-2/3 profiles or Legacy (PDF/A-2b).")
        };
    }
}
