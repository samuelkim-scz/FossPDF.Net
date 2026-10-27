using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FossPDF.Drawing.Exceptions;
using FossPDF.Drawing.Proxy;
using FossPDF.Elements;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Items;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using HarfBuzzSharp;
using SkiaSharp;

namespace FossPDF.Drawing
{
    static class DocumentGenerator
    {
        static DocumentGenerator()
        {
            NativeDependencyCompatibilityChecker.Test();
        }

        internal static int GeneratePdf(Stream stream, IDocument document)
        {
            CheckIfStreamIsCompatible(stream);

            var configuration = GenerationConfiguration.Resolve(document);
            configuration.ValidatePdf();
            using var canvas = new PdfCanvas(stream, configuration.Metadata);
            return RenderDocument(canvas, document, configuration);
        }

        internal static void GenerateXps(Stream stream, IDocument document)
        {
            CheckIfStreamIsCompatible(stream);

            var configuration = GenerationConfiguration.Resolve(document);
            using var canvas = new XpsCanvas(stream, configuration.Metadata);
            RenderDocument(canvas, document, configuration);
        }

        private static void CheckIfStreamIsCompatible(Stream stream)
        {
            if (!stream.CanWrite)
                throw new ArgumentException("The library requires a Stream object with the 'write' capability available (the CanWrite flag). Please consider using the MemoryStream class.");

            if (!stream.CanSeek)
                throw new ArgumentException("The library requires a Stream object with the 'seek' capability available (the CanSeek flag). Please consider using the MemoryStream class.");
        }

        internal static ICollection<byte[]> GenerateImages(IDocument document, ImageGenerationSettings? settings = null)
        {
            var configuration = GenerationConfiguration.Resolve(document);
            var imageSettings = settings ?? ImageGenerationSettings.Default;
            configuration.ApplyImageOutput(imageSettings);
            using var canvas = new ImageCanvas(configuration.Metadata, imageSettings);
            RenderDocument(canvas, document, configuration);

            return canvas.Images;
        }

        internal static ICollection<string> GenerateSvg(IDocument document)
        {
            var configuration = GenerationConfiguration.Resolve(document);
            using var canvas = new SvgCanvas();
            RenderDocument(canvas, document, configuration);

            return canvas.Images;
        }

        internal static ICollection<PreviewerPicture> GeneratePreviewerPictures(IDocument document)
        {
            var configuration = GenerationConfiguration.Resolve(document);
            using var canvas = new SkiaPictureCanvas();
            RenderDocument(canvas, document, configuration);
            return canvas.Pictures;
        }

        internal static int RenderDocument<TCanvas>(TCanvas canvas, IDocument document, GenerationConfiguration configuration)
            where TCanvas : ICanvas, IRenderingCanvas
        {
            if (document is MergedDocument mergedDocument)
                return RenderMergedDocument(canvas, mergedDocument, configuration);

            var debuggingState = configuration.EnableDebugging ? new DebuggingState() : null;
            var content = ComposeContent(document, configuration, debuggingState);

            var documentSpecificFontManager = FontManager.MakeDocumentSpecific();
            try
            {
                documentSpecificFontManager.Tag = configuration.Metadata.Title ?? string.Empty;
                var pageContext = CreatePageContext(documentSpecificFontManager, configuration);
                pageContext.DocumentLength = RenderPass(pageContext, new FreeCanvas(), content, debuggingState, configuration.PageLimit);
                pageContext.IsInitialRenderingPhase = false;

                var subsets = PrepareSubsetting(new[] { content }, documentSpecificFontManager);
                documentSpecificFontManager.FireSubsetCallback(subsets);
                pageContext.DocumentLength = RenderPass(pageContext, new FreeCanvas(), content, debuggingState, configuration.PageLimit);

                pageContext.DocumentLength = RenderPass(pageContext, canvas, content, debuggingState, configuration.PageLimit);

                if (document is IContentPositionSink positionsSink)
                    positionsSink.SetContentPositions(pageContext.GetContentCapturedPositions());

                return pageContext.DocumentLength;
            }
            finally
            {
                documentSpecificFontManager.DisposeDocumentSpecificObjects();
            }
        }

        private sealed class MergedPart
        {
            public int Index { get; init; }
            public Container Content { get; init; } = null!;
            public PageContext PageContext { get; init; } = null!;
            public int StartPage { get; set; }
            public int PageCount { get; set; }
        }

        private static int RenderMergedDocument<TCanvas>(TCanvas canvas, MergedDocument document, GenerationConfiguration configuration)
            where TCanvas : ICanvas, IRenderingCanvas
        {
            var debuggingState = configuration.EnableDebugging ? new DebuggingState() : null;
            var documentSpecificFontManager = FontManager.MakeDocumentSpecific();

            try
            {
                documentSpecificFontManager.Tag = configuration.Metadata.Title ?? string.Empty;

                var useContinuousNumbering = document.PageNumberStrategy == MergedDocumentPageNumberStrategy.Continuous;
                PageContext? sharedPageContext = null;
                var parts = new List<MergedPart>(document.Documents.Count);

                for (var index = 0; index < document.Documents.Count; index++)
                {
                    var content = ComposeContent(document.Documents[index], configuration, debuggingState);
                    var pageContext = useContinuousNumbering
                        ? sharedPageContext ??= CreatePageContext(documentSpecificFontManager, configuration)
                        : CreatePageContext(documentSpecificFontManager, configuration);

                    parts.Add(new MergedPart
                    {
                        Index = index,
                        Content = content,
                        PageContext = pageContext
                    });
                }

                if (useContinuousNumbering)
                    sharedPageContext!.IsInitialRenderingPhase = true;

                var totalPages = 0;

                foreach (var part in parts)
                {
                    part.PageContext.SetDocumentId(part.Index);

                    if (!useContinuousNumbering)
                        part.PageContext.IsInitialRenderingPhase = true;

                    part.StartPage = useContinuousNumbering ? totalPages + 1 : 1;
                    var lastPage = RenderPass(
                        part.PageContext,
                        new FreeCanvas(),
                        part.Content,
                        debuggingState,
                        configuration.PageLimit,
                        part.StartPage);

                    part.PageCount = lastPage - part.StartPage + 1;
                    totalPages += part.PageCount;

                    if (!useContinuousNumbering)
                        part.PageContext.DocumentLength = part.PageCount;

                    if (totalPages > configuration.PageLimit)
                        ThrowLayoutException(configuration.PageLimit, debuggingState);
                }

                if (useContinuousNumbering)
                {
                    sharedPageContext!.DocumentLength = totalPages;
                    sharedPageContext.IsInitialRenderingPhase = false;
                }
                else
                {
                    foreach (var part in parts)
                        part.PageContext.IsInitialRenderingPhase = false;
                }

                var subsets = PrepareSubsetting(parts.Select(x => x.Content), documentSpecificFontManager);
                documentSpecificFontManager.FireSubsetCallback(subsets);

                var secondPassTotal = 0;

                foreach (var part in parts)
                {
                    part.PageContext.SetDocumentId(part.Index);
                    var lastPage = RenderPass(
                        part.PageContext,
                        new FreeCanvas(),
                        part.Content,
                        debuggingState,
                        configuration.PageLimit,
                        part.StartPage);

                    part.PageCount = lastPage - part.StartPage + 1;
                    secondPassTotal += part.PageCount;

                    if (!useContinuousNumbering)
                        part.PageContext.DocumentLength = part.PageCount;
                }

                if (useContinuousNumbering)
                {
                    sharedPageContext!.DocumentLength = secondPassTotal;

                    var nextPage = 1;
                    foreach (var part in parts)
                    {
                        part.StartPage = nextPage;
                        nextPage += part.PageCount;
                    }
                }

                totalPages = secondPassTotal;

                if (totalPages > configuration.PageLimit)
                    ThrowLayoutException(configuration.PageLimit, debuggingState);

                canvas.BeginDocument();

                try
                {
                    foreach (var part in parts)
                    {
                        part.PageContext.SetDocumentId(part.Index);
                        RenderPass(
                            part.PageContext,
                            canvas,
                            part.Content,
                            debuggingState,
                            configuration.PageLimit,
                            part.StartPage,
                            beginDocument: false,
                            endDocument: false);
                    }
                }
                catch
                {
                    canvas.EndDocument();
                    throw;
                }

                canvas.EndDocument();
                return totalPages;
            }
            finally
            {
                documentSpecificFontManager.DisposeDocumentSpecificObjects();
            }
        }

        private static Container ComposeContent(IDocument document, GenerationConfiguration configuration, DebuggingState? debuggingState)
        {
            var container = new DocumentContainer();
            document.Compose(container);
            var content = container.Compose();

            ApplyInheritedAndGlobalTexStyle(content, TextStyle.Default);
            ApplyContentDirection(content, configuration.ContentDirection);

            if (debuggingState != null)
                ApplyDebugging(content, debuggingState);

            if (configuration.EnableCaching)
                ApplyCaching(content);

            return content;
        }

        private static PageContext CreatePageContext(DocumentSpecificFontManager fontManager, GenerationConfiguration configuration)
        {
            return new PageContext
            {
                FontManager = fontManager,
                CheckIfAllTextGlyphsAreAvailable = configuration.CheckGlyphs,
                IsInitialRenderingPhase = true
            };
        }

        private static List<FontToBeSubset> PrepareSubsetting(IEnumerable<Container> contents, DocumentSpecificFontManager fontManager)
        {
            var typeFaceToGlyphs = new Dictionary<SKTypeface, HashSet<uint>>();
            var typeFaceToShaperFont = new Dictionary<SKTypeface, Font>();

            foreach (var content in contents)
            {
                content.VisitChildren(el =>
                {
                    ProcessElementPrepareForSubsetting(el, typeFaceToGlyphs, typeFaceToShaperFont, fontManager);
                });
            }

            return typeFaceToGlyphs.Select(entry => new FontToBeSubset
            {
                Glyphs = entry.Value,
                Typeface = entry.Key,
                ShaperFont = typeFaceToShaperFont[entry.Key]
            }).ToList();
        }

        private static void ProcessElementPrepareForSubsetting(Element? el,
            IDictionary<SKTypeface, HashSet<uint>> typeFaceToGlyphs, Dictionary<SKTypeface, Font> typeFaceToShaperFont,
            DocumentSpecificFontManager fontManager)
        {
            if (el is DynamicHost dh)
            {
                // Dynamic components keep their generated TextBlocks internally.
                foreach (var textBlock in dh.GetTextBlocks())
                {
                    ProcessElementPrepareForSubsetting(textBlock, typeFaceToGlyphs, typeFaceToShaperFont, fontManager);
                }
            }

            if (el is not TextBlock tb)
                return;

            foreach (var textBlockItem in tb.Items)
            {
                if (textBlockItem is not TextBlockSpan span)
                    continue;

                var glyphCodepoints = span.GetGlyphCodepoints();
                if (glyphCodepoints == null)
                    continue;

                var shaperFont = fontManager.ToShaperFont(span.Style);
                var typeface = fontManager.ToFont(span.Style).Typeface;

                typeFaceToShaperFont[typeface] = shaperFont;

                if (!typeFaceToGlyphs.TryGetValue(typeface, out var glyphs))
                {
                    glyphs = new HashSet<uint>();
                    typeFaceToGlyphs[typeface] = glyphs;
                }

                foreach (var glyphCodepoint in glyphCodepoints)
                    glyphs.Add(glyphCodepoint);

                if (tb.LineClamp.HasValue && !string.IsNullOrEmpty(tb.LineClampEllipsis))
                {
                    AddGlyphsForText(
                        tb.LineClampEllipsis,
                        span.Style,
                        typeFaceToGlyphs,
                        typeFaceToShaperFont,
                        fontManager);
                }

                span.ClearTextShapingResult();
            }
        }

        private static void AddGlyphsForText(
            string text,
            TextStyle style,
            IDictionary<SKTypeface, HashSet<uint>> typeFaceToGlyphs,
            Dictionary<SKTypeface, Font> typeFaceToShaperFont,
            DocumentSpecificFontManager fontManager)
        {
            var shapingResult = fontManager.ToTextShaper(style).Shape(text);
            var typeface = fontManager.ToFont(style).Typeface;

            typeFaceToShaperFont[typeface] = fontManager.ToShaperFont(style);

            if (!typeFaceToGlyphs.TryGetValue(typeface, out var glyphs))
            {
                glyphs = new HashSet<uint>();
                typeFaceToGlyphs[typeface] = glyphs;
            }

            for (var index = 0; index < shapingResult.Length; index++)
                glyphs.Add(shapingResult[index].Codepoint);
        }

        internal static int RenderPass<TCanvas>(
            PageContext pageContext,
            TCanvas canvas,
            Container content,
            DebuggingState? debuggingState,
            int pageLimit,
            int startPage = 1,
            bool beginDocument = true,
            bool endDocument = true)
            where TCanvas : ICanvas, IRenderingCanvas
        {
            InjectDependencies(content, pageContext, canvas);
            content.VisitChildren(x => (x as IStateResettable)?.ResetState());

            if (beginDocument)
                canvas.BeginDocument();

            var currentPage = startPage;

            while (true)
            {
                pageContext.SetPageNumber(currentPage);
                debuggingState?.Reset();

                var spacePlan = content.Measure(Size.Max);

                if (spacePlan.Type == SpacePlanType.Wrap)
                {
                    if (endDocument)
                        canvas.EndDocument();

                    ThrowLayoutException(pageLimit, debuggingState);
                }

                try
                {
                    canvas.BeginPage(spacePlan);
                    content.Draw(spacePlan);
                }
                catch (Exception exception)
                {
                    if (endDocument)
                        canvas.EndDocument();

                    throw new DocumentDrawingException("An exception occured during document drawing.", exception);
                }

                canvas.EndPage();

                if (spacePlan.Type == SpacePlanType.FullRender)
                    break;

                if (currentPage >= pageLimit)
                {
                    if (endDocument)
                        canvas.EndDocument();

                    ThrowLayoutException(pageLimit, debuggingState);
                }

                currentPage++;
            }

            if (endDocument)
                canvas.EndDocument();

            return currentPage;
        }

        internal static void ThrowLayoutException(int pageLimit, DebuggingState? debuggingState)
        {
            var message = $"Composed layout generates infinite document. This may happen in two cases. " +
                          $"1) Your document and its layout configuration is correct but the content takes more than {pageLimit} pages. " +
                          $"In this case, increase DocumentSettings.DocumentLayoutExceptionThreshold for this document " +
                          $"or the {nameof(FossPDF)}.{nameof(Settings)}.{nameof(Settings.DocumentLayoutExceptionThreshold)} process default. " +
                          $"2) The layout configuration of your document is invalid. Some of the elements require more space than is provided." +
                          $"Please analyze your documents structure to detect this element and fix its size constraints.";

            var elementTrace = debuggingState?.BuildTrace() ?? "Debug trace is available only in the DEBUG mode.";

            throw new DocumentLayoutException(message, elementTrace);
        }

        internal static void InjectDependencies(this Element content, IPageContext pageContext, ICanvas canvas)
        {
            content.VisitChildren(x =>
            {
                if (x == null)
                    return;

                x.PageContext = pageContext;
                x.Canvas = canvas;
            });
        }

        private static void ApplyCaching(Container content)
        {
            content.VisitChildren(x =>
            {
                if (x is ICacheable)
                    x.CreateProxy(y => y == null ? null : new CacheProxy(y));
            });
        }

        private static void ApplyDebugging(Container content, DebuggingState debuggingState)
        {
            content.VisitChildren(x =>
            {
                x.CreateProxy(y => y == null ? null : new DebuggingProxy(debuggingState, y));
            });
        }

        internal static void ApplyContentDirection(this Element? content, ContentDirection direction)
        {
            if (content == null)
                return;

            if (content is ContentDirectionSetter contentDirectionSetter)
            {
                ApplyContentDirection(contentDirectionSetter.Child, contentDirectionSetter.ContentDirection);
                return;
            }

            if (content is IContentDirectionAware contentDirectionAware)
                contentDirectionAware.ContentDirection = direction;

            foreach (var child in content.GetChildren())
                ApplyContentDirection(child, direction);
        }

        internal static void ApplyInheritedAndGlobalTexStyle(this Element? content, TextStyle documentDefaultTextStyle)
        {
            if (content == null)
                return;

            if (content is TextBlock textBlock)
            {
                foreach (var textBlockItem in textBlock.Items)
                {
                    if (textBlockItem is TextBlockSpan textSpan)
                        textSpan.Style = textSpan.Style.ApplyInheritedStyle(documentDefaultTextStyle).ApplyGlobalStyle();

                    if (textBlockItem is TextBlockElement textElement)
                        ApplyInheritedAndGlobalTexStyle(textElement.Element, documentDefaultTextStyle);
                }

                return;
            }

            if (content is DynamicHost dynamicHost)
                dynamicHost.TextStyle = dynamicHost.TextStyle.ApplyInheritedStyle(documentDefaultTextStyle);

            if (content is DefaultTextStyle defaultTextStyleElement)
               documentDefaultTextStyle = defaultTextStyleElement.TextStyle.ApplyInheritedStyle(documentDefaultTextStyle);

            foreach (var child in content.GetChildren())
                ApplyInheritedAndGlobalTexStyle(child, documentDefaultTextStyle);
        }

    }
}
