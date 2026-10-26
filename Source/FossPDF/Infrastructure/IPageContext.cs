using System.Collections.Generic;
using FossPDF.Drawing;

namespace FossPDF.Infrastructure
{
    internal class DocumentLocation
    {
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
        DocumentSpecificFontManager FontManager { get; }
        bool CheckIfAllTextGlyphsAreAvailable { get; }
    }
}
