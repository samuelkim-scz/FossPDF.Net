#if NET9_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.FontSubset.Glue;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    public class FontSubsettingTests
    {
        [Test]
        public void Callback_PreservesEveryFontStyle_AndEnumeratesRequestsOnce()
        {
            var fontManager = FontManager.MakeDocumentSpecific();

            try
            {
                var weights = new[] { FontWeight.Thin, FontWeight.Light, FontWeight.Normal, FontWeight.Bold, FontWeight.Black };
                var styles = fontManager.StyleSets.Keys.Where(family => family.StartsWith("Lato"))
                    .SelectMany(family => weights.SelectMany(weight => new[] { false, true }.Select(italic =>
                        TextStyle.LibraryDefault with { FontFamily = family, FontWeight = weight, IsItalic = italic })));
                // The document font manager owns the fonts and typefaces used by this test.
                #pragma warning disable IDISP004
                var requests = styles.Select(style => (Style: style, Typeface: fontManager.ToFont(style).Typeface))
                    .DistinctBy(font => font.Typeface.Handle)
                    .Select(font => new FontToBeSubset
                {
                    Typeface = font.Typeface,
                    ShaperFont = fontManager.ToShaperFont(font.Style),
                    Glyphs = new HashSet<uint> { 0, 1, 2 }
                }).ToArray();
                #pragma warning restore IDISP004
                var expectedStyles = requests.Select(request =>
                {
                    using var style = request.Typeface.FontStyle;
                    return (Family: request.Typeface.FamilyName, style.Weight, style.Width, style.Slant,
                        Names: ReadTypographicNames(request.Typeface));
                }).ToArray();
                var enumerations = 0;

                Assert.That(requests.Length, Is.GreaterThanOrEqualTo(10));

                IEnumerable<FontToBeSubset> EnumerateRequestsOnce()
                {
                    Assert.That(++enumerations, Is.EqualTo(1));

                    foreach (var request in requests)
                        yield return request;
                }

                BuiltInFontSubsetting.Callback(fontManager, EnumerateRequestsOnce());

                Assert.That(enumerations, Is.EqualTo(1));

                foreach (var style in expectedStyles)
                {
                    using var targetStyle = new SKFontStyle(style.Weight, style.Width, style.Slant);
                    #pragma warning disable IDISP001
                    var typeface = fontManager.StyleSets[style.Family].Match(targetStyle);
                    #pragma warning restore IDISP001

                    Assert.That(typeface, Is.Not.Null);
                    using var actualStyle = typeface.FontStyle;
                    Assert.That(actualStyle.Weight, Is.EqualTo(targetStyle.Weight));
                    Assert.That(actualStyle.Slant, Is.EqualTo(targetStyle.Slant));
                    Assert.That(ReadTypographicNames(typeface), Is.EquivalentTo(style.Names),
                        "Subsetting must retain typographic family/subfamily names for consistent font selection across platforms.");
                }
            }
            finally
            {
                fontManager.DisposeDocumentSpecificObjects();
            }
        }

        private static Dictionary<ushort, string> ReadTypographicNames(SKTypeface typeface)
        {
            // OpenType 'name' table: inspect the English Unicode records retained by HarfBuzz.
            var data = typeface.GetTableData(0x6e616d65);
            var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2));
            var stringsOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
            var names = new Dictionary<ushort, string>();
            for (var index = 0; index < count; index++)
            {
                var record = data.AsSpan(6 + index * 12, 12);
                var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
                var language = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(4, 2));
                var nameId = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(6, 2));
                if (platform != 3 || language != 0x0409 || (nameId != 16 && nameId != 17))
                    continue;

                var length = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(8, 2));
                var offset = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(10, 2));
                names[nameId] = Encoding.BigEndianUnicode.GetString(data, stringsOffset + offset, length);
            }
            return names;
        }
    }
}
#endif
