#if NET9_0
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-304: encryption, decryption and permission flags for existing-PDF operations. The
    /// operations companion targets net9.0, so the whole fixture is compiled only for that
    /// framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PdfSecurityTests
    {
        private const string Marker = "FossPDF-security-marker";

        [Test]
        public void Encrypt256Bit_WritesTheAes256StandardSecurityHandler()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            var dictionary = ReadEncryptionDictionary(result);

            Encoding.Latin1.GetString(result).Should().StartWith("%PDF-2.0");
            ReadInteger(dictionary, "/V").Should().Be(5);
            ReadInteger(dictionary, "/R").Should().Be(6);
            ReadEncryptionLength(dictionary).Should().Be(256);
            dictionary.Should().Contain("/AESV3");

            using var user = Open(result, "userpw");
            user.PageCount.Should().Be(1);

            using var owner = Open(result, "ownerpw");
            owner.PageCount.Should().Be(1);
        }

        [Test]
        public void Encrypt128Bit_WritesTheRc4StandardSecurityHandler()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption128Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            var dictionary = ReadEncryptionDictionary(result);

            ReadInteger(dictionary, "/V").Should().Be(4);
            ReadInteger(dictionary, "/R").Should().Be(4);
            ReadEncryptionLength(dictionary).Should().Be(128);
            dictionary.Should().Contain("/V2");
            dictionary.Should().NotContain("/AESV2");

            using var user = Open(result, "userpw");
            user.PageCount.Should().Be(1);
        }

        [Test]
        public void Encrypt40Bit_WritesTheLegacyStandardSecurityHandler()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption40Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            var dictionary = ReadEncryptionDictionary(result);

            ReadInteger(dictionary, "/V").Should().Be(1);
            ReadInteger(dictionary, "/R").Should().Be(2);
            ReadEncryptionLength(dictionary).Should().Be(40);
            dictionary.Should().NotContain("/CF");

            using var user = Open(result, "userpw");
            user.PageCount.Should().Be(1);
        }

        [Test]
        public void Encryption_RequiresTheConfiguredPassword()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            var missing = Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(result).SaveToBytes());
            var wrong = Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(result, "not-the-password").SaveToBytes());

            missing!.Message.Should().Contain("password");
            wrong!.Message.Should().Contain("password");
            missing.Message.Should().NotContain("userpw");
            wrong.Message.Should().NotContain("not-the-password");
        }

        [Test]
        public void EmptyUserPassword_AllowsOpeningWithoutPassword()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = string.Empty,
                OwnerPassword = "ownerpw"
            });

            using var withoutPassword = Open(result, null);
            using var withOwnerPassword = Open(result, "ownerpw");

            withoutPassword.PageCount.Should().Be(1);
            withOwnerPassword.PageCount.Should().Be(1);
        }

        [Test]
        public void Encrypt_RejectsAMissingOwnerPassword()
        {
            var operation = DocumentOperation.Load(CreateSource());

            var nullOwner = Assert.Throws<ArgumentException>(() =>
                operation.Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "userpw"
                }));

            var emptyOwner = Assert.Throws<ArgumentException>(() =>
                operation.Encrypt(new DocumentOperation.Encryption40Bit
                {
                    UserPassword = "userpw",
                    OwnerPassword = string.Empty
                }));

            nullOwner!.Message.Should().Contain("owner password");
            emptyOwner!.Message.Should().Contain("owner password");
        }

        [Test]
        public void Encrypt_CanOnlyBeSetOnce()
        {
            var operation = DocumentOperation
                .Load(CreateSource())
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "userpw",
                    OwnerPassword = "ownerpw"
                });

            Assert.Throws<InvalidOperationException>(() =>
                operation.Encrypt(new DocumentOperation.Encryption128Bit
                {
                    UserPassword = "other-user",
                    OwnerPassword = "other-owner"
                }));
        }

        [Test]
        public void Encrypt_CopiesTheConfigurationWhenItIsAdded()
        {
            var options = new DocumentOperation.Encryption256Bit
            {
                UserPassword = "first-user",
                OwnerPassword = "first-owner",
                AllowPrinting = false
            };

            var operation = DocumentOperation.Load(CreateSource()).Encrypt(options);

            options.UserPassword = "second-user";
            options.OwnerPassword = "second-owner";
            options.AllowPrinting = true;

            var result = operation.SaveToBytes();

            using var first = Open(result, "first-user");
            first.PageCount.Should().Be(1);

            Assert.Throws<PdfReaderException>(() => Open(result, "second-user").Dispose());
            ReadInteger(ReadEncryptionDictionary(result), "/P").Should().Be(-2056);
        }

        [Test]
        public void Permissions_AreWrittenForEveryRestrictedOption()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw",
                AllowAnnotation = false,
                AllowAssembly = false,
                AllowContentExtraction = false,
                AllowFillingForms = false,
                AllowModification = false,
                AllowPrinting = false
            });

            // Bits 3-6, 9, 11 and 12 of the permission value are cleared. Bits 1-2 and 7-8 stay
            // set, as required by the PDF specification.
            ReadInteger(ReadEncryptionDictionary(result), "/P").Should().Be(-3392);
        }

        [Test]
        public void PrintingAndCopyingRestrictions_MapToTheExpectedPermissionBits()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw",
                AllowPrinting = false,
                AllowContentExtraction = false
            });

            // Print (bit 3), copy (bit 5) and high-quality print (bit 12) are cleared.
            ReadInteger(ReadEncryptionDictionary(result), "/P").Should().Be(-2072);
        }

        [Test]
        public void FortyBitPermissions_KeepTheReservedRevisionTwoBitsSet()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption40Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw",
                AllowPrinting = false,
                AllowContentExtraction = false
            });

            // Revision 2 only defines print (bit 3) and copy (bit 5) here. The high-quality print
            // bit does not exist at this revision and must stay set.
            ReadInteger(ReadEncryptionDictionary(result), "/P").Should().Be(-24);
        }

        [Test]
        public void EncryptMetadataFalse_IsWrittenFor128And256Bit()
        {
            var aes = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw",
                EncryptMetadata = false
            });

            var rc4 = Encrypt(CreateSource(), new DocumentOperation.Encryption128Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw",
                EncryptMetadata = false
            });

            ReadEncryptionDictionary(aes).Should().Contain("/EncryptMetadata false");
            ReadEncryptionDictionary(rc4).Should().Contain("/EncryptMetadata false");
        }

        [Test]
        public void EncryptedInput_IsDecryptedAndKeepsItsContentAndMetadata()
        {
            var encrypted = CreateEncryptedSource(handler => handler.SetEncryptionToV5(true));

            var result = DocumentOperation
                .Load(encrypted, "userpw")
                .SaveToBytes();

            ReadEncryptionDictionary(result).Should().BeEmpty();
            using var document = Open(result);
            document.PageCount.Should().Be(1);
            document.Info.Title.Should().Be(Marker);
            ReadStreams(result, 0).Should().Contain(x => x.Contains("1 0 0 rg"));
        }

        [Test]
        public void EncryptedInputs_WithEverySupportedStrengthAreRead()
        {
            var strengths = new Action<PdfStandardSecurityHandler>[]
            {
                handler => handler.SetEncryptionToV1(),
                handler => handler.SetEncryptionToV4UsingRC4(true),
                handler => handler.SetEncryptionToV5(true)
            };

            foreach (var strength in strengths)
            {
                var encrypted = CreateEncryptedSource(strength);
                var result = DocumentOperation.Load(encrypted, "userpw").SaveToBytes();

                ReadEncryptionDictionary(result).Should().BeEmpty();
                ReadStreams(result, 0).Should().Contain(x => x.Contains("1 0 0 rg"));
            }
        }

        [Test]
        public void WrongPassword_FailsWithoutWritingAnOutputFile()
        {
            var encrypted = CreateEncryptedSource(handler => handler.SetEncryptionToV5(true));
            var path = TemporaryPath();

            try
            {
                Assert.Throws<PdfOperationException>(() =>
                    DocumentOperation.Load(encrypted, "not-the-password").Save(path));

                File.Exists(path).Should().BeFalse("a failed operation must not leave a partial file");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void UnsupportedSecurityHandler_FailsClearly()
        {
            var encrypted = CreateEncryptedSource(handler => handler.SetEncryptionToV5(true));
            var unsupported = Encoding.Latin1
                .GetBytes(Encoding.Latin1.GetString(encrypted).Replace("/Filter/Standard", "/Filter/Adobe.Pu"));

            var exception = Assert.Throws<PdfOperationException>(
                () => DocumentOperation.Load(unsupported, "userpw").SaveToBytes());

            exception!.Message.Should().Contain("not supported");
            exception.Message.Should().NotContain("userpw");
        }

        [Test]
        public void EncryptedOutput_DoesNotContainPlaintextContent()
        {
            var source = CreateSource();
            var result = Encrypt(source, new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            var sourceText = Encoding.Latin1.GetString(source);
            var resultText = Encoding.Latin1.GetString(result);

            sourceText.Should().Contain(Marker);
            sourceText.Should().Contain("1 0 0 rg");
            resultText.Should().NotContain(Marker);
            resultText.Should().NotContain("1 0 0 rg");

            ReadStreams(
                DocumentOperation.Load(result, "userpw").SaveToBytes(),
                0).Should().Contain(x => x.Contains("1 0 0 rg"));
        }

        [Test]
        public void OwnerAndUserPasswords_AreNotWrittenToTheOutput()
        {
            var result = Encrypt(CreateSource(), new DocumentOperation.Encryption256Bit
            {
                UserPassword = "user-secret-4f6a",
                OwnerPassword = "owner-secret-9c1e"
            });

            var text = Encoding.Latin1.GetString(result);

            text.Should().NotContain("user-secret-4f6a");
            text.Should().NotContain("owner-secret-9c1e");
        }

        [Test]
        public void DecryptAndRemoveRestrictions_ClearAPendingEncryption()
        {
            var decrypted = DocumentOperation
                .Load(CreateSource())
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "userpw",
                    OwnerPassword = "ownerpw"
                })
                .Decrypt()
                .SaveToBytes();

            var unrestricted = DocumentOperation
                .Load(CreateSource())
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "userpw",
                    OwnerPassword = "ownerpw"
                })
                .RemoveRestrictions()
                .SaveToBytes();

            ReadEncryptionDictionary(decrypted).Should().BeEmpty();
            ReadEncryptionDictionary(unrestricted).Should().BeEmpty();
        }

        [Test]
        public void OverlaysAndEncryption_AreCombined()
        {
            var layer = CreateLayer();
            var layerPath = WriteTemporaryFile(layer);

            try
            {
                var result = DocumentOperation
                    .Load(CreateSource())
                    .OverlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .Encrypt(new DocumentOperation.Encryption256Bit
                    {
                        UserPassword = "userpw",
                        OwnerPassword = "ownerpw"
                    })
                    .SaveToBytes();

                var decrypted = DocumentOperation.Load(result, "userpw").SaveToBytes();
                var streams = ReadStreams(decrypted, 0);

                streams.Should().HaveCount(4, "original painting is isolated before the overlay");
                streams[0].Trim().Should().Be("q");
                streams[2].Trim().Should().Be("Q");
                streams[^1].Should().Contain("/L0 Do");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void Encrypting_DoesNotModifyTheInputBytes()
        {
            var source = CreateSource();
            var original = source.ToArray();

            Encrypt(source, new DocumentOperation.Encryption256Bit
            {
                UserPassword = "userpw",
                OwnerPassword = "ownerpw"
            });

            source.Should().Equal(original);
        }

        private static byte[] Encrypt(byte[] source, DocumentOperation.EncryptionBase encryption)
        {
            var operation = DocumentOperation.Load(source);

            switch (encryption)
            {
                case DocumentOperation.Encryption40Bit options:
                    return operation.Encrypt(options).SaveToBytes();
                case DocumentOperation.Encryption128Bit options:
                    return operation.Encrypt(options).SaveToBytes();
                case DocumentOperation.Encryption256Bit options:
                    return operation.Encrypt(options).SaveToBytes();
                default:
                    throw new ArgumentException("Unsupported encryption type.", nameof(encryption));
            }
        }

        private static byte[] CreateSource()
        {
            using var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            document.Info.Title = Marker;

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);

            using (var graphics = XGraphics.FromPdfPage(page))
            {
                graphics.DrawRectangle(XBrushes.Red, 0, 0, 120, 100);
            }

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateLayer()
        {
            using var document = new PdfDocument();

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);

            using (var graphics = XGraphics.FromPdfPage(page))
            {
                graphics.DrawRectangle(XBrushes.Blue, 0, 0, 120, 100);
            }

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateEncryptedSource(Action<PdfStandardSecurityHandler> configure)
        {
            using var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            document.Info.Title = Marker;

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);

            using (var graphics = XGraphics.FromPdfPage(page))
            {
                graphics.DrawRectangle(XBrushes.Red, 0, 0, 120, 100);
            }

            document.SecuritySettings.UserPassword = "userpw";
            document.SecuritySettings.OwnerPassword = "ownerpw";
            configure(document.SecurityHandler);

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static string ReadEncryptionDictionary(byte[] pdf)
        {
            var text = Encoding.Latin1.GetString(pdf);
            var filter = Regex.Match(text, @"/Filter\s*/Standard");

            if (!filter.Success)
                return string.Empty;

            // The encryption dictionary starts before the filter entry; /P, /CF and other keys may
            // precede it.
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

        private static int ReadInteger(string dictionary, string key)
        {
            var match = Regex.Match(dictionary, Regex.Escape(key) + @"\s+(-?\d+)");
            match.Success.Should().BeTrue($"the encryption dictionary should contain {key}");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static int ReadEncryptionLength(string dictionary)
        {
            // A /CF crypt filter also has a /Length key, expressed in bytes. The document key
            // length follows /Filter/Standard for revision 3 and 4 dictionaries.
            var filter = Regex.Match(dictionary, @"/Filter\s*/Standard");
            var afterFilter = filter.Success
                ? dictionary.Substring(filter.Index)
                : dictionary;

            var match = Regex.Match(afterFilter, @"/Length\s+(\d+)");

            if (!match.Success)
                match = Regex.Match(dictionary, @"/Length\s+(\d+)");

            match.Success.Should().BeTrue("the encryption dictionary should contain the key length");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static PdfDocument Open(byte[] pdf, string password = null)
        {
            var stream = new MemoryStream(pdf, writable: false);

            return password == null
                ? PdfReader.Open(stream, PdfDocumentOpenMode.Import)
                : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import, new PdfReaderOptions());
        }

        private static string[] ReadStreams(byte[] pdf, int pageIndex)
        {
            using var document = Open(pdf);

            return document.Pages[pageIndex].Contents.Elements
                .Select(Resolve)
                .Where(x => x?.Stream?.Value != null)
                .Select(x => Decode(x!.Stream!.Value))
                .ToArray();
        }

        private static PdfDictionary Resolve(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string Decode(byte[] bytes)
        {
            try
            {
                using var compressed = new MemoryStream(bytes, false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return Encoding.Latin1.GetString(output.ToArray());
            }
            catch (InvalidDataException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        private static string TemporaryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "fosspdf-security-" + Guid.NewGuid().ToString("N") + ".pdf");
        }

        private static string WriteTemporaryFile(byte[] content)
        {
            var path = TemporaryPath();
            File.WriteAllBytes(path, content);
            return path;
        }
    }
}
#endif
