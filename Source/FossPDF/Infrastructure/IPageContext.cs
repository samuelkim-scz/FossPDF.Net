using System.Collections.Generic;
using FossPDF.Drawing;

namespace FossPDF.Infrastructure
{
    internal class DocumentLocation
    {
        public int? DocumentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PageStart { get; set; }
        public int PageEnd { get; set; }
        public int Length => PageEnd - PageStart + 1;
    }

    internal interface IPageContext
    {
        int CurrentPage { get; }

        /// <summary>True until the first layout pass has produced the document length.</summary>
        bool IsInitialRenderingPhase { get; }

        /// <summary>Page count reported by the most recently completed render pass.</summary>
        int DocumentLength { get; }

        void SetSectionPage(string name);
        DocumentLocation? GetLocation(string name);

        /// <summary>Returns the canvas destination name, scoped to the current source document when merged.</summary>
        string GetDocumentLocationName(string locationName);

        void CaptureContentPosition(PageElementLocation location);
        ICollection<PageElementLocation> GetContentCapturedPositions(string id);
        DocumentSpecificFontManager FontManager { get; }
        bool CheckIfAllTextGlyphsAreAvailable { get; }

        /// <summary>Default target DPI for images without a per-image override.</summary>
        int? ImageRasterDpi { get; }

        /// <summary>Default lossy quality for images without a per-image override.</summary>
        int? ImageCompressionQuality { get; }
    }
}
