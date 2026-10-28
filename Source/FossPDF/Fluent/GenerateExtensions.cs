using System;
using System.Collections.Generic;
using System.IO;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class GenerateExtensions
    {
        #region PDF
        
        public static byte[] GeneratePdf(this IDocument document)
        {
            return document.GeneratePdf(out var _);
        }

        public static byte[] GeneratePdf(this IDocument document, out int totalPages)
        {
            using var stream = new MemoryStream();
            totalPages = DocumentGenerator.GeneratePdf(stream, document);
            return stream.ToArray();
        }
        
        public static void GeneratePdf(this IDocument document, string filePath)
        {
            var data = document.GeneratePdf();
            
            if (File.Exists(filePath))
                File.Delete(filePath);
            
            File.WriteAllBytes(filePath, data);
        }

        public static void GeneratePdf(this IDocument document, Stream stream)
        {
            var data = document.GeneratePdf();
            stream.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Renders the document into semantic paint batches for the tagged-PDF backend. The rendered
        /// fragments are assembled and tagged by the optional <c>FossPDF.PdfOperations</c> package
        /// (<c>GenerateTaggedPdf</c>). The core package never references a PDF library.
        /// </summary>
        public static TaggedDocumentContent GenerateTaggedContent(this IDocument document)
        {
            return DocumentGenerator.GenerateTaggedContent(document);
        }

        #endregion

        #region XPS
        
        public static byte[] GenerateXps(this IDocument document)
        {
            using var stream = new MemoryStream();
            document.GenerateXps(stream);
            return stream.ToArray();
        }
        
        public static void GenerateXps(this IDocument document, string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            document.GenerateXps(stream);
        }

        public static void GenerateXps(this IDocument document, Stream stream)
        {
            DocumentGenerator.GenerateXps(stream, document);
        }
        
        #endregion

        #region Images

        public static IEnumerable<byte[]> GenerateImages(this IDocument document)
        {
            return DocumentGenerator.GenerateImages(document);
        }

        public static IEnumerable<byte[]> GenerateImages(this IDocument document, ImageGenerationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            return DocumentGenerator.GenerateImages(document, settings);
        }
        
        /// <summary>Exports every page as a PNG image using the document's resolved DPI.</summary>
        /// <param name="document">The document to export.</param>
        /// <param name="filePath">Returns the output path for the zero-based page index.</param>
        public static void GenerateImages(this IDocument document, Func<int, string> filePath)
        {
            document.GenerateImages(filePath, ImageGenerationSettings.Default);
        }

        /// <summary>Exports all pages with one output configuration. File indices start at zero.</summary>
        public static void GenerateImages(this IDocument document, Func<int, string> filePath, ImageGenerationSettings settings)
        {
            var index = 0;
            
            foreach (var imageData in document.GenerateImages(settings))
            {
                var path = filePath(index);
                
                if (File.Exists(path))
                    File.Delete(path);
                
                File.WriteAllBytes(path, imageData);
                index++;
            }
        }

        /// <summary>Writes pages to caller-owned writable streams, without seeking or closing them.</summary>
        public static void GenerateImagesToStreams(this IDocument document, Func<int, Stream> streamSelector)
        {
            document.GenerateImagesToStreams(streamSelector, ImageGenerationSettings.Default);
        }

        public static void GenerateImagesToStreams(this IDocument document, Func<int, Stream> streamSelector, ImageGenerationSettings settings)
        {
            if (streamSelector == null)
                throw new ArgumentNullException(nameof(streamSelector));

            var index = 0;
            foreach (var data in document.GenerateImages(settings))
            {
                var stream = streamSelector(index++)
                    ?? throw new ArgumentException("The stream selector returned null.", nameof(streamSelector));
                if (!stream.CanWrite)
                    throw new ArgumentException("The selected stream must be writable.", nameof(streamSelector));
                stream.Write(data, 0, data.Length);
            }
        }

        #endregion

        #region SVG

        public static IEnumerable<string> GenerateSvg(this IDocument document)
        {
            return DocumentGenerator.GenerateSvg(document);
        }

        /// <summary>Exports every page as an SVG document.</summary>
        /// <param name="document">The document to export.</param>
        /// <param name="filePath">Returns the output path for the zero-based page index.</param>
        public static void GenerateSvg(this IDocument document, Func<int, string> filePath)
        {
            var index = 0;

            foreach (var svgString in document.GenerateSvg())
            {
                var path = filePath(index);

                if (File.Exists(path))
                    File.Delete(path);

                File.WriteAllText(path, svgString);
                index++;
            }
        }

        #endregion
    }
}
