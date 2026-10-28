using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.Fluent
{
    /// <summary>Generates and validates PDF/A-2/3 in the optional .NET 9 operations package.</summary>
    public static class PdfAExtensions
    {
        private static readonly Lazy<byte[]> OutputIntentSource = new(() => Document.Create(container =>
            container.Page(page => { page.Size(10, 10); page.Content().Height(1); }))
            .WithMetadata(new DocumentMetadata { PdfA = true }).GeneratePdf());

        /// <summary>
        /// Renders the selected DocumentSettings.PdfAConformance profile and validates the final
        /// bytes with veraPDF 1.30.3. Legacy or an inherited PdfA=true maps to PDF/A-2b.
        /// A-level profiles use the semantic writer; every profile requires complete glyph coverage.
        /// </summary>
        public static byte[] GeneratePdfA(this IDocument document, PdfAOptions? options = null,
            CancellationToken cancellationToken = default) => document.GeneratePdfA(out _, options, cancellationToken);

        /// <summary>Generates validated bytes and returns the complete veraPDF report for audit or CI.</summary>
        public static byte[] GeneratePdfA(this IDocument document, out PdfAValidationReport report,
            PdfAOptions? options = null, CancellationToken cancellationToken = default)
        {
            var configuration = GenerationConfiguration.Resolve(document);
            var profile = configuration.Settings.PdfAConformance ??
                (configuration.Metadata.PdfA ? PdfAConformance.Legacy : PdfAConformance.None);
            var flavour = PdfAValidator.Flavour(profile);
            if (configuration.Settings.PdfUaConformance != PdfUaConformance.None)
                throw new NotSupportedException("Combined PDF/A and PDF/UA output is not qualified. Select PdfUaConformance.None.");
            options ??= new PdfAOptions();
            var attachments = (options.Attachments ?? throw new ArgumentException("Attachments must not be null.", nameof(options)))
                .Select(x => x?.Input ?? throw new ArgumentException("An attachment must not be null.", nameof(options))).ToArray();
            if (flavour[0] == '2' && attachments.Length > 0)
                throw new NotSupportedException("This writer supports associated files only with PDF/A-3. Select a PDF/A-3 profile or remove the attachments.");
            var extensions = options.MetadataExtensions == null ? Array.Empty<XElement>() : PdfXmpMetadata.ParseExtension(options.MetadataExtensions);
            var jar = PdfAValidator.Prepare(options, cancellationToken);
            configuration.Optimization?.Validate();
            if (configuration.Optimization != null)
                PdfOptimizer.ResolveExecutable(configuration.Optimization.QpdfExecutablePath);

            // Keep the original document (including merged pagination) and the resolved defaults.
            var render = configuration.WithSettings(configuration.Settings with
            {
                PdfAConformance = PdfAConformance.Legacy,
                CheckIfAllTextGlyphsAreAvailable = true,
                CompressDocument = true,
                EnableAdditionalCompression = false,
                Linearize = false
            });
            cancellationToken.ThrowIfCancellationRequested();
            var tagged = flavour[1] == 'a';
            byte[] bytes;
            if (tagged)
                bytes = DocumentGenerator.GenerateTaggedContent(document, render).ToTaggedPdf();
            else
            {
                using var rendered = new MemoryStream();
                DocumentGenerator.GeneratePdf(rendered, document, render);
                bytes = rendered.ToArray();
            }
            using (var input = new MemoryStream(bytes, writable: false))
            using (var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
            {
                pdf.Version = 17;
                if (tagged)
                    AddOutputIntent(pdf);
                if (!string.IsNullOrWhiteSpace(configuration.Metadata.Language))
                    pdf.Internals.Catalog.Elements.SetString("/Lang", configuration.Metadata.Language);
                PdfAttachments.Apply(pdf, pdf, attachments);
                using var saved = new MemoryStream();
                pdf.Save(saved, false);
                bytes = PdfXmpMetadata.ApplyArchivalIdentification(saved.ToArray(), pdf, flavour, extensions);
            }
            if (configuration.Optimization != null)
                bytes = PdfOptimizer.Optimize(bytes, configuration.Optimization with { NewlineBeforeEndstream = true }, cancellationToken);
            report = PdfAValidator.ValidatePrepared(bytes, profile, options, jar, cancellationToken);
            if (!report.IsCompliant)
                throw new PdfAConformanceException(report);
            return bytes;
        }

        /// <summary>Stages validated bytes, then writes at the current position without closing or seeking the stream.</summary>
        public static void GeneratePdfA(this IDocument document, Stream stream, PdfAOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
                throw new ArgumentException("The stream must be writable.", nameof(stream));
            var bytes = document.GeneratePdfA(options, cancellationToken);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Generates and validates before replacing the destination file.</summary>
        public static void GeneratePdfA(this IDocument document, string filePath, PdfAOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            var bytes = document.GeneratePdfA(options, cancellationToken);
            File.WriteAllBytes(filePath, bytes);
        }

        private static void AddOutputIntent(PdfDocument destination)
        {
            using var stream = new MemoryStream(OutputIntentSource.Value, writable: false);
            using var source = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            var intent = (PdfDictionary)source.Internals.Catalog.Elements.GetArray("/OutputIntents")!.Elements.GetDictionary(0)!;
            var original = intent.Elements.GetDictionary("/DestOutputProfile")!;
            var profile = new PdfDictionary(destination);
            profile.Elements.SetInteger("/N", original.Elements.GetInteger("/N"));
            PdfStreams.CreateCompressed(profile, PdfXmpMetadata.ReadStream(original));
            destination.Internals.AddObject(profile);
            var copy = new PdfDictionary(destination);
            copy.Elements.SetName("/Type", "/OutputIntent");
            copy.Elements.SetName("/S", "/GTS_PDFA1");
            foreach (var key in new[] { "/RegistryName", "/OutputConditionIdentifier", "/Info" })
                copy.Elements.SetString(key, intent.Elements.GetString(key));
            copy.Elements.SetReference("/DestOutputProfile", profile);
            destination.Internals.AddObject(copy);
            destination.Internals.Catalog.Elements.SetObject("/OutputIntents", new PdfArray(destination, copy.Reference!));
        }

    }
}
