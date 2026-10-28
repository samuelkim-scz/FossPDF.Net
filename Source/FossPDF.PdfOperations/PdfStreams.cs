using System.IO;
using System.IO.Compression;
using PdfSharp.Pdf;

namespace FossPDF.PdfOperations
{
    internal static class PdfStreams
    {
        internal static void CreateCompressed(PdfDictionary dictionary, byte[] data)
        {
            // PDFsharp 6.2.4's Flate encoder omits the RFC 1950 Adler-32 trailer.
            // Let the framework write the complete zlib envelope for streams we create.
            using var output = new MemoryStream();
            using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                compressor.Write(data, 0, data.Length);
            dictionary.CreateStream(output.ToArray());
            dictionary.Elements.SetName("/Filter", "/FlateDecode");
        }
    }
}
