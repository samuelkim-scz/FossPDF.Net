using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.PdfOperations
{
    internal sealed record ConformingPdf(byte[] Bytes, PdfAValidationReport? Archival, PdfUaValidationReport? Accessibility);

    internal static class PdfConformanceWriter
    {
        private static readonly Lazy<byte[]> OutputIntentSource = new(() => Document.Create(container =>
            container.Page(page => { page.Size(10, 10); page.Content().Height(1); }))
            .WithMetadata(new DocumentMetadata { PdfA = true }).GeneratePdf());

        internal static ConformingPdf Generate(IDocument document, PdfAOptions? options, bool requireArchival,
            bool requireAccessibility, CancellationToken cancellationToken)
        {
            var configuration = GenerationConfiguration.Resolve(document);
            var profile = configuration.Settings.PdfAConformance ??
                (configuration.Metadata.PdfA ? PdfAConformance.Legacy : PdfAConformance.None);
            var archival = profile != PdfAConformance.None;
            var accessible = configuration.Settings.PdfUaConformance == PdfUaConformance.PdfUa1;
            if (requireArchival && !archival)
                throw new ArgumentOutOfRangeException(nameof(profile), "Select a PDF/A profile before GeneratePdfA.");
            if (requireAccessibility && !accessible)
                throw new ArgumentException("GeneratePdfUa requires DocumentSettings.PdfUaConformance = PdfUaConformance.PdfUa1.", nameof(document));
            var flavour = archival ? PdfAValidator.Flavour(profile) : null;
            if (accessible)
            {
                if (string.IsNullOrWhiteSpace(configuration.Metadata.Title))
                    throw new ArgumentException("PDF/UA requires a document Title.", nameof(document));
                if (string.IsNullOrWhiteSpace(configuration.Metadata.Language))
                    throw new ArgumentException("PDF/UA requires a document Language, for example en-US.", nameof(document));
                ValidateLanguage(configuration.Metadata.Language!);
            }
            options ??= new PdfAOptions();
            var attachments = (options.Attachments ?? throw new ArgumentException("Attachments must not be null.", nameof(options)))
                .Select(x => x?.Input ?? throw new ArgumentException("An attachment must not be null.", nameof(options))).ToArray();
            if (attachments.Length > 0 && flavour?.StartsWith("3", StringComparison.Ordinal) != true)
                throw new NotSupportedException("Associated files require a selected PDF/A-3 profile in the conformance writer.");
            var extensions = options.MetadataExtensions == null ? Array.Empty<XElement>() : PdfXmpMetadata.ParseExtension(options.MetadataExtensions);
            var jar = PdfAValidator.Prepare(options, cancellationToken);
            configuration.Optimization?.Validate();
            if (configuration.Optimization != null)
                PdfOptimizer.ResolveExecutable(configuration.Optimization.QpdfExecutablePath);
            var render = configuration.WithSettings(configuration.Settings with
            {
                PdfAConformance = archival ? PdfAConformance.Legacy : PdfAConformance.None,
                PdfUaConformance = PdfUaConformance.None,
                CheckIfAllTextGlyphsAreAvailable = true,
                CompressDocument = true,
                EnableAdditionalCompression = false,
                Linearize = false
            });
            cancellationToken.ThrowIfCancellationRequested();
            var tagged = accessible || flavour?.EndsWith("a", StringComparison.Ordinal) == true;
            byte[] bytes;
            if (tagged)
            {
                var content = DocumentGenerator.GenerateTaggedContent(document, render);
                if (accessible)
                    ValidateAccessibilityInformation(content.Root);
                bytes = content.ToTaggedPdf();
            }
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
                if (tagged && archival)
                    AddOutputIntent(pdf);
                if (!string.IsNullOrWhiteSpace(configuration.Metadata.Language))
                    pdf.Internals.Catalog.Elements.SetString("/Lang", configuration.Metadata.Language);
                PdfAttachments.Apply(pdf, pdf, attachments);
                using var saved = new MemoryStream();
                pdf.Save(saved, false);
                bytes = PdfXmpMetadata.ApplyConformanceIdentification(saved.ToArray(), pdf, flavour, accessible, extensions);
            }
            if (configuration.Optimization != null)
                bytes = PdfOptimizer.Optimize(bytes, configuration.Optimization with { NewlineBeforeEndstream = archival }, cancellationToken);
            PdfAValidationReport? archivalReport = null;
            if (archival)
            {
                archivalReport = PdfAValidator.ValidatePrepared(bytes, profile, options, jar, cancellationToken);
                if (!archivalReport.IsCompliant)
                    throw new PdfAConformanceException(archivalReport);
            }
            PdfUaValidationReport? accessibilityReport = null;
            if (accessible)
            {
                accessibilityReport = new PdfUaValidationReport(PdfAValidator.ReadReport(bytes, "ua1", options, jar, cancellationToken), archivalReport);
                if (!accessibilityReport.MachineChecksPassed)
                    throw new PdfUaConformanceException(accessibilityReport);
            }
            return new ConformingPdf(bytes, archivalReport, accessibilityReport);
        }

        private static void ValidateAccessibilityInformation(TaggedStructureNode node)
        {
            if (node.Language != null)
                ValidateLanguage(node.Language);
            if (node.Role is "Figure" or "Formula" && string.IsNullOrWhiteSpace(node.AlternativeText))
                throw new ArgumentException($"PDF/UA {node.Role} '{node.Id}' needs alternative text. Supply SemanticFigure/SemanticFormula text or mark decorative content SemanticIgnore.");
            if (node.Role == "Link" && string.IsNullOrWhiteSpace(node.AlternativeText))
                throw new ArgumentException($"PDF/UA Link '{node.Id}' needs a description. Use SemanticLink(description) around container links; inline links use their supplied text.");
            foreach (var child in node.Children)
                ValidateAccessibilityInformation(child);
        }

        private static void ValidateLanguage(string language)
        {
            // Accept BCP 47 syntax, including private-use and grandfathered forms. Choosing the
            // correct registered language for the actual content remains an author responsibility.
            if (!Regex.IsMatch(language, @"\A[A-Za-z]{1,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant))
                throw new ArgumentException("PDF/UA languages must use BCP 47 syntax, for example en-US or fr-FR.", nameof(language));
        }

        private static void AddOutputIntent(PdfDocument destination)
        {
            using var stream = new MemoryStream(OutputIntentSource.Value, writable: false);
            using var source = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            var intent = source.Internals.Catalog.Elements.GetArray("/OutputIntents")!.Elements.GetDictionary(0)!;
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
