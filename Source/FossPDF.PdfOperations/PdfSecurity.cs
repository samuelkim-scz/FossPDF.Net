using System;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Security;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Applies the password and permission plan recorded by <see cref="Fluent.DocumentOperation"/>
    /// to the destination document. PDFsharp writes the corresponding standard security handler
    /// dictionary when the document is saved.
    /// </summary>
    internal static class PdfSecurity
    {
        public static void Apply(PdfDocument document, DocumentOperation.EncryptionSettings? encryption)
        {
            if (encryption == null)
                return;

            try
            {
                var settings = document.SecuritySettings;
                settings.UserPassword = encryption.UserPassword;
                settings.OwnerPassword = encryption.OwnerPassword;

                switch (encryption.Strength)
                {
                    case DocumentOperation.EncryptionStrength.Rc4_40Bit:
                        // Revision 2 only defines the print, modify, copy and annotation bits. The
                        // remaining permission bits are reserved and must stay set, so they are not
                        // written from the configurable values.
                        settings.PermitPrint = encryption.AllowPrinting;
                        settings.PermitModifyDocument = encryption.AllowModification;
                        settings.PermitExtractContent = encryption.AllowContentExtraction;
                        settings.PermitAnnotations = encryption.AllowAnnotation;
                        document.SecurityHandler.SetEncryptionToV1();
                        break;

                    case DocumentOperation.EncryptionStrength.Rc4_128Bit:
                        ApplyAllPermissions(settings, encryption);
                        document.SecurityHandler.SetEncryptionToV4UsingRC4(encryption.EncryptMetadata);
                        break;

                    case DocumentOperation.EncryptionStrength.Aes_256Bit:
                        ApplyAllPermissions(settings, encryption);
                        document.SecurityHandler.SetEncryptionToV5(encryption.EncryptMetadata);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(encryption), encryption.Strength, null);
                }
            }
            catch (Exception exception) when (!(exception is PdfOperationException))
            {
                // Never include passwords or configuration values in the reported message.
                throw new PdfOperationException(
                    "The output PDF could not be encrypted with the requested settings.", exception);
            }
        }

        private static void ApplyAllPermissions(
            PdfSecuritySettings settings,
            DocumentOperation.EncryptionSettings encryption)
        {
            settings.PermitPrint = encryption.AllowPrinting;
            settings.PermitFullQualityPrint = encryption.AllowPrinting;
            settings.PermitExtractContent = encryption.AllowContentExtraction;
            settings.PermitAnnotations = encryption.AllowAnnotation;
            settings.PermitModifyDocument = encryption.AllowModification;
            settings.PermitAssembleDocument = encryption.AllowAssembly;
            settings.PermitFormsFill = encryption.AllowFillingForms;
        }
    }
}
