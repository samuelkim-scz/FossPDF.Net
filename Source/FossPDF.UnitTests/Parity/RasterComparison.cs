using System;
using SkiaSharp;

namespace FossPDF.UnitTests.Parity
{
    internal static class RasterComparison
    {
        internal static double ChangedPixelFraction(SKBitmap expected, SKBitmap actual, int channelTolerance)
        {
            if (expected.Width != actual.Width || expected.Height != actual.Height)
                throw new ArgumentException("Reference and actual raster dimensions must match.");
            if (channelTolerance < 0 || channelTolerance > 255)
                throw new ArgumentOutOfRangeException(nameof(channelTolerance));

            long changed = 0;
            for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                if (ChannelDifference(expected.GetPixel(x, y), actual.GetPixel(x, y)) > channelTolerance)
                    changed++;
            }

            return changed / (double)((long)expected.Width * expected.Height);
        }

        private static int ChannelDifference(SKColor a, SKColor b) =>
            Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
    }
}
