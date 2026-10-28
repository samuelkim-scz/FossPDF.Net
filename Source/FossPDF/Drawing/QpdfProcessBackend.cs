using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FossPDF.Drawing.Exceptions;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Runs qpdf. An internal seam so failure, timeout and cancellation paths can be tested without
    /// launching a process.
    /// </summary>
    internal interface IQpdfBackend
    {
        string GetVersion(string executablePath, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Runs qpdf with <paramref name="arguments"/>, writes <paramref name="input"/> to a private
        /// temporary input file, and returns the document qpdf writes to standard output.
        /// </summary>
        byte[] Run(
            string executablePath,
            IReadOnlyList<string> arguments,
            byte[] input,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    internal sealed class QpdfProcessBackend : IQpdfBackend
    {
        public string GetVersion(string executablePath, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var output = RunProcess(
                executablePath, new[] { "--version" }, timeout, cancellationToken);

            return System.Text.Encoding.UTF8.GetString(output);
        }

        public byte[] Run(
            string executablePath,
            IReadOnlyList<string> arguments,
            byte[] input,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // qpdf requires a seekable input file and uses "-" for standard output. The directory is
            // unique per invocation so concurrent generations never share files, and it is removed
            // before this method returns.
            var directory = Path.Combine(Path.GetTempPath(), "fosspdf-qpdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var inputPath = Path.Combine(directory, "input.pdf");
                File.WriteAllBytes(inputPath, input);

                var fullArguments = new List<string>(arguments.Count + 2);
                fullArguments.AddRange(arguments);
                fullArguments.Add(inputPath);
                fullArguments.Add("-");

                return RunProcess(executablePath, fullArguments, timeout, cancellationToken);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                    // Best effort: a locked file must not hide the primary result or failure.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static byte[] RunProcess(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            Process process;

            try
            {
                process = Process.Start(startInfo)
                    ?? throw new PdfOptimizationException($"The qpdf process '{executablePath}' could not be started.");
            }
            catch (Win32Exception exception)
            {
                throw new PdfOptimizationException(
                    $"The qpdf process '{executablePath}' could not be started. " +
                    "Install qpdf 11 or newer, or set the executable path explicitly.", exception);
            }

            using (process)
            {
                using var output = new MemoryStream();
                var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
                var errorTask = process.StandardError.ReadToEndAsync();
                var stopwatch = Stopwatch.StartNew();

                while (!process.WaitForExit(50))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Terminate(process);
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (stopwatch.Elapsed > timeout)
                    {
                        Terminate(process);
                        throw new PdfOptimizationException(
                            $"qpdf did not finish within {timeout.TotalSeconds:0} seconds. " +
                            "Increase PdfOptimizationSettings.ProcessTimeout if the document is large.");
                    }
                }

                outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();

                // qpdf uses exit code 3 for warnings. The document is still complete; only 1 and 2
                // indicate errors.
                if (process.ExitCode != 0 && process.ExitCode != 3)
                {
                    throw new PdfOptimizationException(
                        $"qpdf failed with exit code {process.ExitCode}. {Describe(error)}");
                }

                return output.ToArray();
            }
        }

        private static void Terminate(Process process)
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        }

        private static string Describe(string error)
        {
            var text = (error ?? string.Empty).Trim();

            if (text.Length == 0)
                return "No diagnostic output was produced.";

            const int limit = 2000;

            return text.Length <= limit ? text : text.Substring(0, limit) + "...";
        }
    }
}
