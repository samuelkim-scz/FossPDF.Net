using System;
using System.IO;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows how an existing PDF is protected with a user/owner password and permission flags, and
    /// how a protected input is decrypted again. All examples write their artifacts under the test
    /// directory.
    /// </summary>
    public class PdfSecurityExamples
    {
        [Test]
        public void EncryptWithPasswordsAndRestrictedPermissions()
        {
            var directory = CreateOutputDirectory("pdf-operations-security");
            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var protectedPath = Path.Combine(directory, "invoice-protected.pdf");
            var decryptedPath = Path.Combine(directory, "invoice-decrypted.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice());

            // 256-bit AES is the recommended strength. The owner password is mandatory; the user
            // password is the one a reader asks for. Printing and copying are denied.
            DocumentOperation
                .LoadFile(sourcePath)
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "reader-password",
                    OwnerPassword = "owner-password",
                    AllowPrinting = false,
                    AllowContentExtraction = false
                })
                .Save(protectedPath);

            var protectedBytes = File.ReadAllBytes(protectedPath);
            var protectedText = Encoding.Latin1.GetString(protectedBytes);

            Assert.That(protectedText, Does.Contain("/Encrypt"));
            Assert.That(protectedText, Does.Not.Contain("reader-password"));
            Assert.That(protectedText, Does.Not.Contain("owner-password"));

            // Opening without the password fails; the user password opens it for reading.
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(protectedBytes).SaveToBytes());
            Assert.That(DocumentOperation.Load(protectedBytes, "reader-password").SaveToBytes(), Is.Not.Empty);

            // The owner password can remove the protection altogether.
            DocumentOperation
                .LoadFile(protectedPath, "owner-password")
                .Decrypt()
                .Save(decryptedPath);

            var decryptedBytes = File.ReadAllBytes(decryptedPath);
            Assert.That(Encoding.Latin1.GetString(decryptedBytes), Does.Not.Contain("/Encrypt"));

            // Reloading proves the decrypted result is an ordinary usable PDF.
            Assert.That(DocumentOperation.Load(decryptedBytes).SaveToBytes(), Is.Not.Empty);
        }

        [Test]
        public void EncryptWithLegacyFortyBitEncryption()
        {
            var directory = CreateOutputDirectory("pdf-operations-security-40-bit");
            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var protectedPath = Path.Combine(directory, "invoice-40-bit.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice());

            // 40-bit RC4 exists for compatibility with old readers. It only exposes the four
            // revision-2 permissions.
            DocumentOperation
                .LoadFile(sourcePath)
                .Encrypt(new DocumentOperation.Encryption40Bit
                {
                    UserPassword = "reader-password",
                    OwnerPassword = "owner-password",
                    AllowPrinting = false,
                    AllowAnnotation = false,
                    AllowContentExtraction = true,
                    AllowModification = false
                })
                .Save(protectedPath);

            var protectedBytes = File.ReadAllBytes(protectedPath);
            Assert.That(DocumentOperation.Load(protectedBytes, "reader-password").SaveToBytes(), Is.Not.Empty);
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(protectedBytes).SaveToBytes());
        }

        private static byte[] CreateInvoice()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(20);

                    page.Content().Column(column =>
                    {
                        column.Item().Text("Invoice 2026-002").FontSize(20).Bold();
                        column.Item().PaddingTop(10).Text("Consulting services");
                        column.Item().PaddingTop(20).Text("Total: 1,200.00 EUR").Bold();
                    });
                });
            }).GeneratePdf();
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
