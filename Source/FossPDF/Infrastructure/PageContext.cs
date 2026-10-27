using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;

namespace FossPDF.Infrastructure
{
    internal class PageContext : IPageContext
    {
        public const string DocumentLocation = "document";

        private List<DocumentLocation> Locations { get; } = new();
        public int CurrentPage { get; private set; }
        public int? CurrentDocumentId { get; private set; }

        public bool IsInitialRenderingPhase { get; set; } = true;
        public int DocumentLength { get; set; }

        public DocumentSpecificFontManager FontManager { get; set; } = null!; // Set by DocumentGenerator.
        public bool CheckIfAllTextGlyphsAreAvailable { get; set; }

        internal void SetDocumentId(int documentId)
        {
            CurrentDocumentId = documentId;
        }

        internal void SetPageNumber(int number)
        {
            CurrentPage = number;
            SetSectionPage(DocumentLocation);
        }

        public void SetSectionPage(string name)
        {
            var location = GetLocation(name);

            if (location == null)
            {
                location = new DocumentLocation
                {
                    DocumentId = CurrentDocumentId,
                    Name = name,
                    PageStart = CurrentPage,
                    PageEnd = CurrentPage
                };

                Locations.Add(location);
            }

            if (location.PageEnd < CurrentPage)
                location.PageEnd = CurrentPage;
        }

        public DocumentLocation? GetLocation(string name)
        {
            return Locations.Find(x => x.DocumentId == CurrentDocumentId && x.Name == name);
        }

        public string GetDocumentLocationName(string locationName)
        {
            return CurrentDocumentId.HasValue
                ? $"{CurrentDocumentId}:{locationName}"
                : locationName;
        }

        public int GetPageNumbers()
        {
            return Locations.Count == 0 ? 0 : Locations.Max(x => x.PageEnd);
        }
    }
}
