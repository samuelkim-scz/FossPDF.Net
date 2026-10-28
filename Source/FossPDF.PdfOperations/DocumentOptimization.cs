using System;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public sealed partial class DocumentOperation
    {
        private bool _enableAdditionalCompression;
        private bool _disableCompression;
        private bool _linearize;
        private string? _qpdfExecutablePath;

        /// <summary>Recompresses Flate streams and generates object streams with qpdf 11 or newer.</summary>
        public DocumentOperation EnableAdditionalCompression()
        {
            if (_disableCompression)
                throw new InvalidOperationException("Additional compression cannot be combined with disabled compression.");
            _enableAdditionalCompression = true;
            return this;
        }

        /// <summary>Decompresses decodable streams and disables object streams with qpdf 11 or newer.</summary>
        public DocumentOperation DisableCompression()
        {
            if (_enableAdditionalCompression)
                throw new InvalidOperationException("Disabled compression cannot be combined with additional compression.");
            _disableCompression = true;
            return this;
        }

        /// <summary>Produces a linearized PDF with qpdf 11 or newer, after all other recorded operations.</summary>
        public DocumentOperation Linearize()
        {
            _linearize = true;
            return this;
        }

        /// <summary>Overrides the global qpdf executable path and PATH discovery for this operation.</summary>
        public DocumentOperation UseQpdfExecutable(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("A qpdf executable path must not be empty.", nameof(executablePath));
            _qpdfExecutablePath = executablePath;
            return this;
        }

        private bool HasOptimization => _enableAdditionalCompression || _disableCompression || _linearize;

        private void ValidateOptimization()
        {
            if (HasOptimization && _encryption != null)
                throw new InvalidOperationException(
                    "PDF optimization cannot be combined with encryption. " +
                    "This adapter does not forward passwords to qpdf; call Decrypt() to save without encryption.");
        }

        private byte[] OptimizeOutput(byte[] result) => !HasOptimization ? result : PdfOptimizer.Optimize(result,
            new PdfOptimizationSettings
            {
                DisableCompression = _disableCompression,
                EnableAdditionalCompression = _enableAdditionalCompression,
                Linearize = _linearize,
                QpdfExecutablePath = _qpdfExecutablePath
            });
    }
}
