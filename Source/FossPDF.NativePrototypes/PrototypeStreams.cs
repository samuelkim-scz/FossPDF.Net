namespace FossPDF.NativePrototypes;

// Proof boundary: backend readers receive an owned seekable copy; backend writers stage output.
// A production API must choose its own limits and document stream-position semantics.
internal static class PrototypeStreams
{
    internal static MemoryStream CopyInput(Stream input)
    {
        if (!input.CanRead)
            throw new ArgumentException("Input must be readable.", nameof(input));
        var copy = new MemoryStream();
        try
        {
            var buffer = new byte[8192];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (copy.Length + count > 32 * 1024 * 1024)
                    throw new InvalidDataException("Prototype input exceeds 32 MiB.");
                copy.Write(buffer, 0, count);
            }
            copy.Position = 0;
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    internal static void Save(PdfSharp.Pdf.PdfDocument document, Stream output)
    {
        if (!output.CanWrite)
            throw new ArgumentException("Output must be writable.", nameof(output));
        using var staged = new MemoryStream();
        document.Save(staged, false);
        staged.Position = 0;
        staged.CopyTo(output);
    }
}
