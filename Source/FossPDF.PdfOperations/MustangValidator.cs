using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

namespace FossPDF.PdfOperations
{
    internal static class MustangValidator
    {
        internal const string Version = "2.26.0";
        internal const string JarSha256 = "42D7868CB68264874A7B8CAB4C3587B03B23CCC7CD72373DA917F66758BB9736";

        internal static string Prepare(EInvoiceOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = options.MustangJarPath ?? Environment.GetEnvironmentVariable("FOSSPDF_MUSTANG_JAR");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new NotSupportedException("E-invoice generation requires Mustang 2.26.0 and Java 17+. Set EInvoiceOptions.MustangJarPath or FOSSPDF_MUSTANG_JAR to Mustang-CLI-2.26.0.jar.");
            using var input = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(input)) != JarSha256)
                throw new NotSupportedException("The Mustang JAR does not match the qualified 2.26.0 release. Use the unmodified official Mustang-CLI-2.26.0.jar.");
            return Path.GetFullPath(path);
        }

        internal static EInvoiceValidationReport Validate(byte[] data, bool packaged, EInvoiceOptions options,
            string jar, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Path.GetTempPath(), "fosspdf-invoice-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, packaged ? "invoice.pdf" : "invoice.xml");
                File.WriteAllBytes(path, data);
                var executable = options.JavaExecutablePath;
                if (!string.IsNullOrEmpty(Path.GetDirectoryName(executable)))
                    executable = Path.GetFullPath(executable);
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = directory, StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var argument in new[] { "-Xmx512m", "-Dfile.encoding=UTF-8", "-jar", jar,
                    "--disable-file-logging", "--action", "validate", "--source", path }) start.ArgumentList.Add(argument);
                var xml = PdfAValidator.RunProcess(start, options.ProcessTimeout, cancellationToken, "Mustang",
                    OperatingSystem.IsWindows() ? -1 : 255);
                try { return new EInvoiceValidationReport(xml, packaged); }
                catch (XmlException exception) { throw new PdfOperationException("Mustang returned malformed validation XML.", exception); }
            }
            finally
            {
                try { Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
