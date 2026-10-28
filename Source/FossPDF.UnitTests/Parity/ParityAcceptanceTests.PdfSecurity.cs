#if NET9_0
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-304 acceptance: encrypt the generated source with 256-bit AES, check the standard
    /// security handler and permission flags, verify that the configured passwords are required,
    /// and verify that decrypting preserves the pages and their content. The operations companion
    /// targets net9.0 only, so this runner is compiled for that framework; the fixture declares
    /// frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-304")]
        private static void RunPdfSecurity(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var sourceId = fixture.Input.GetProperty("source").GetString();
            var sourceFixture = catalog.Fixtures.Single(x => x.Id == sourceId);
            var source = ExplicitPagination(sourceFixture).GeneratePdf();

            var userPassword = fixture.Input.GetProperty("user_password").GetString()!;
            var ownerPassword = fixture.Input.GetProperty("owner_password").GetString()!;
            var permissions = fixture.Input.GetProperty("permissions");
            var allowPrinting = permissions.GetProperty("print").GetBoolean();
            var allowCopying = permissions.GetProperty("copy").GetBoolean();

            var sourceSnapshot = source.ToArray();
            SaveArtifact(fixture.Id, "source.pdf", source);

            var encrypted = DocumentOperation
                .Load(source)
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = userPassword,
                    OwnerPassword = ownerPassword,
                    AllowPrinting = allowPrinting,
                    AllowContentExtraction = allowCopying
                })
                .SaveToBytes();

            Assert.That(Encoding.ASCII.GetString(encrypted, 0, 5), Is.EqualTo("%PDF-"),
                "PDF-304: the result is a PDF document.");
            SaveArtifact(fixture.Id, "encrypted.pdf", encrypted);

            // Keep all three supported handlers available to the independent reader, without
            // writing passwords into reports, filenames or command-line arguments.
            SaveArtifact(fixture.Id, "encrypted-40.pdf", DocumentOperation.Load(source)
                .Encrypt(new DocumentOperation.Encryption40Bit
                {
                    UserPassword = userPassword,
                    OwnerPassword = ownerPassword,
                    AllowPrinting = allowPrinting,
                    AllowContentExtraction = allowCopying
                }).SaveToBytes());
            SaveArtifact(fixture.Id, "encrypted-128.pdf", DocumentOperation.Load(source)
                .Encrypt(new DocumentOperation.Encryption128Bit
                {
                    UserPassword = userPassword,
                    OwnerPassword = ownerPassword,
                    AllowPrinting = allowPrinting,
                    AllowContentExtraction = allowCopying
                }).SaveToBytes());

            var dictionary = ReadSecurityDictionary(encrypted);

            Assert.That(ReadSecurityInteger(dictionary, "/V"), Is.EqualTo(5),
                "PDF-304: the output uses the 256-bit encryption version.");
            Assert.That(ReadSecurityInteger(dictionary, "/R"), Is.EqualTo(6),
                "PDF-304: the output uses revision 6.");
            Assert.That(ReadSecurityLength(dictionary), Is.EqualTo(256),
                "PDF-304: the key length is 256 bits.");
            Assert.That(dictionary, Does.Contain("/AESV3"),
                "PDF-304: the crypt filter is AESV3.");
            Assert.That(ReadSecurityInteger(dictionary, "/P"), Is.EqualTo(-2072),
                "PDF-304: printing, high-quality printing and copying are denied; the other permissions remain available.");

            var text = Encoding.Latin1.GetString(encrypted);
            Assert.That(text, Does.Not.Contain(userPassword),
                "PDF-304: the user password is not written to the output.");
            Assert.That(text, Does.Not.Contain(ownerPassword),
                "PDF-304: the owner password is not written to the output.");
            Assert.That(source, Is.EqualTo(sourceSnapshot),
                "PDF-304: the input byte array is not modified.");

            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(encrypted).SaveToBytes(),
                "PDF-304: the user password is required.");
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(encrypted, "wrong-password").SaveToBytes(),
                "PDF-304: a wrong password fails without producing a document.");

            var decryptedWithUser = DocumentOperation
                .Load(encrypted, userPassword)
                .SaveToBytes();

            SaveArtifact(fixture.Id, "decrypted.pdf", decryptedWithUser);
            Assert.That(ReadSecurityDictionary(decryptedWithUser), Is.Empty,
                "PDF-304: saving without Encrypt produces an unencrypted document.");

            var sourcePages = ReadPageFacts(source);
            var decryptedPages = ReadPageFacts(decryptedWithUser);

            Assert.That(decryptedPages, Has.Length.EqualTo(sourcePages.Length),
                "PDF-304: decryption preserves the page count.");
            Assert.That(sourcePages, Has.Length.EqualTo(2),
                "PDF-304: the generated source has two pages.");

            for (var pageIndex = 0; pageIndex < sourcePages.Length; pageIndex++)
            {
                Assert.That(decryptedPages[pageIndex].Content, Is.EqualTo(sourcePages[pageIndex].Content),
                    $"PDF-304: page {pageIndex + 1} content is preserved after decryption.");
                Assert.That(decryptedPages[pageIndex].Width, Is.EqualTo(sourcePages[pageIndex].Width).Within(0.01),
                    $"PDF-304: page {pageIndex + 1} width is preserved.");
                Assert.That(decryptedPages[pageIndex].Height, Is.EqualTo(sourcePages[pageIndex].Height).Within(0.01),
                    $"PDF-304: page {pageIndex + 1} height is preserved.");
            }

            var decryptedWithOwner = DocumentOperation
                .Load(encrypted, ownerPassword)
                .Decrypt()
                .SaveToBytes();

            Assert.That(ReadSecurityDictionary(decryptedWithOwner), Is.Empty,
                "PDF-304: the owner password can remove the encryption.");
            Assert.That(ReadPageFacts(decryptedWithOwner).Select(x => x.Content),
                Is.EqualTo(sourcePages.Select(x => x.Content)),
                "PDF-304: the owner password decodes the same page content.");
        }

        private static string ReadSecurityDictionary(byte[] pdf)
        {
            var text = Encoding.Latin1.GetString(pdf);
            var filter = Regex.Match(text, @"/Filter\s*/Standard");

            if (!filter.Success)
                return string.Empty;

            var objectStart = text.LastIndexOf(" obj", filter.Index, StringComparison.Ordinal);
            var dictionaryStart = objectStart < 0
                ? filter.Index
                : text.IndexOf("<<", objectStart, StringComparison.Ordinal);

            if (dictionaryStart < 0)
                dictionaryStart = filter.Index;

            var end = text.IndexOf("endobj", filter.Index, StringComparison.Ordinal);
            var length = end < 0 ? text.Length - dictionaryStart : end - dictionaryStart;

            return text.Substring(dictionaryStart, length);
        }

        private static int ReadSecurityInteger(string dictionary, string key)
        {
            var match = Regex.Match(dictionary, Regex.Escape(key) + @"\s+(-?\d+)");
            Assert.That(match.Success, Is.True,
                $"PDF-304: the security dictionary contains {key}.");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static int ReadSecurityLength(string dictionary)
        {
            var filter = Regex.Match(dictionary, @"/Filter\s*/Standard");
            var afterFilter = filter.Success
                ? dictionary.Substring(filter.Index)
                : dictionary;

            var match = Regex.Match(afterFilter, @"/Length\s+(\d+)");

            if (!match.Success)
                match = Regex.Match(dictionary, @"/Length\s+(\d+)");

            Assert.That(match.Success, Is.True,
                "PDF-304: the security dictionary contains the key length.");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
#endif
