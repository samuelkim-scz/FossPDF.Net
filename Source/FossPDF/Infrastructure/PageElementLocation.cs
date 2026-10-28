using System.Collections.Generic;
using System.Numerics;

namespace FossPDF.Infrastructure
{
    /// <summary>
    /// One captured occurrence of an element. Coordinates and dimensions are in the output canvas
    /// coordinate space: points for PDF/SVG/previewer output, or pixels for raster page export at
    /// the selected DPI. X and Y refer to the top-left corner of the page.
    /// </summary>
    public sealed class PageElementLocation
    {
        public string Id { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>Canvas transformation applied to the element when it was drawn.</summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    }

    internal interface IContentPositionSink
    {
        void SetContentPositions(IReadOnlyCollection<PageElementLocation> positions);
    }
}
