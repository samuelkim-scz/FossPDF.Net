using System;
using FossPDF.Infrastructure;

namespace FossPDF.Drawing
{
    public class DocumentMetadata
    {
        public int ImageQuality { get; set; } = 101;
        public int RasterDpi { get; set; } = 72;
        public bool PdfA { get; set; }

        public string? Title { get; set; }
        /// <summary>
        /// The document's natural language as a BCP 47 tag (for example <c>en-US</c>). It is written
        /// to the catalog of tagged PDFs and inherited by structure elements.
        /// </summary>
        public string? Language { get; set; }
        public string? Author { get; set; }
        public string? Subject { get; set; }
        public string? Keywords { get; set; }
        public string? Creator { get; set; }
        public string? Producer { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        [Obsolete("This API has been moved since version 2022.9. Please use the FossPDF.Settings.DocumentLayoutExceptionThreshold static property.")]
        public int DocumentLayoutExceptionThreshold
        {
            get => Settings.DocumentLayoutExceptionThreshold;
            set => Settings.DocumentLayoutExceptionThreshold = value;
        }

        [Obsolete("This API has been moved since version 2022.9. Please use the FossPDF.Settings.EnableCaching static property.")]
        public bool ApplyCaching
        {
            get => Settings.EnableCaching;
            set => Settings.EnableCaching = value;
        }

        [Obsolete("This API has been moved since version 2022.9. Please use the FossPDF.Settings.EnableDebugging static property.")]
        public bool ApplyDebugging
        {
            get => Settings.EnableDebugging;
            set => Settings.EnableDebugging = value;
        }

        public static DocumentMetadata Default => new DocumentMetadata();

        internal DocumentMetadata Snapshot() => (DocumentMetadata)MemberwiseClone();
    }
}
