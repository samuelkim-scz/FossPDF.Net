using System;
using System.Collections.Generic;
using System.IO;
using FossPDF.PdfOperations;
using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Performs operations on existing PDF documents: load, select, reorder and merge pages, then
    /// save the result. Pages are imported into a new document, so they keep their original page
    /// boxes, content streams and resources instead of being rasterized.
    /// </summary>
    /// <remarks>
    /// Operations are recorded in order and executed by <see cref="Save(string)"/>,
    /// <see cref="Save(Stream)"/> or <see cref="SaveToBytes"/>. Loading reads the whole input into
    /// memory immediately; borrowed input streams are read from their current position and never
    /// closed. Output is staged in memory before it is written, so a failed operation does not leave
    /// a partially written file or stream. The class targets <c>net9.0</c> and belongs to the
    /// optional <c>FossPDF.PdfOperations</c> package, which the core package does not reference.
    /// </remarks>
    public sealed class DocumentOperation
    {
        private const string DefaultSelector = "1-z";

        private readonly List<InputSource> _sources = new List<InputSource>();
        private readonly List<PageSelection> _selections = new List<PageSelection>();
        private readonly List<LayerOperation> _underlays = new List<LayerOperation>();
        private readonly List<LayerOperation> _overlays = new List<LayerOperation>();
        private readonly List<AttachmentOperation> _attachments = new List<AttachmentOperation>();
        private readonly List<string> _metadataExtensions = new List<string>();

        private EncryptionSettings? _encryption;

        private DocumentOperation()
        {
        }

        /// <summary>
        /// Base settings shared by all password-based encryption strengths.
        /// </summary>
        public class EncryptionBase
        {
            /// <summary>
            /// The password that allows opening the encrypted document. When it is
            /// <see langword="null"/> or empty, readers can open the document without a password,
            /// but the configured permissions still apply.
            /// </summary>
            public string? UserPassword { get; set; }

            /// <summary>
            /// The password that grants full access and bypasses the configured permissions. It
            /// must not be empty. Using the same value for both passwords is insecure.
            /// </summary>
            public string OwnerPassword { get; set; } = null!;
        }

        /// <summary>
        /// Legacy 40-bit RC4 encryption, kept for compatibility with old readers. Prefer
        /// <see cref="Encryption256Bit"/> for new documents.
        /// </summary>
        public sealed class Encryption40Bit : EncryptionBase
        {
            /// <summary>Allows adding and modifying annotations. Defaults to <see langword="true"/>.</summary>
            public bool AllowAnnotation { get; set; } = true;

            /// <summary>Allows copying text and graphics. Defaults to <see langword="true"/>.</summary>
            public bool AllowContentExtraction { get; set; } = true;

            /// <summary>Allows modifying document content. Defaults to <see langword="true"/>.</summary>
            public bool AllowModification { get; set; } = true;

            /// <summary>Allows printing. Defaults to <see langword="true"/>.</summary>
            public bool AllowPrinting { get; set; } = true;
        }

        /// <summary>
        /// 128-bit encryption. The PDF standard security handler uses the RC4 crypt filter for this
        /// strength, matching the pinned target; use <see cref="Encryption256Bit"/> for AES.
        /// </summary>
        public sealed class Encryption128Bit : EncryptionBase
        {
            /// <summary>Allows adding and modifying annotations. Defaults to <see langword="true"/>.</summary>
            public bool AllowAnnotation { get; set; } = true;

            /// <summary>Allows inserting, rotating or deleting pages and adding bookmarks. Defaults to <see langword="true"/>.</summary>
            public bool AllowAssembly { get; set; } = true;

            /// <summary>Allows copying text and graphics. Defaults to <see langword="true"/>.</summary>
            public bool AllowContentExtraction { get; set; } = true;

            /// <summary>Allows filling form fields. Defaults to <see langword="true"/>.</summary>
            public bool AllowFillingForms { get; set; } = true;

            /// <summary>Allows modifying document content. Defaults to <see langword="true"/>.</summary>
            public bool AllowModification { get; set; } = true;

            /// <summary>Allows printing. Defaults to <see langword="true"/>.</summary>
            public bool AllowPrinting { get; set; } = true;

            /// <summary>Encrypts metadata streams as well. Defaults to <see langword="true"/>.</summary>
            public bool EncryptMetadata { get; set; } = true;
        }

        /// <summary>
        /// 256-bit AES encryption, the recommended strength for new documents. The PDF standard
        /// security handler uses the AESV3 crypt filter and revision 6.
        /// </summary>
        public sealed class Encryption256Bit : EncryptionBase
        {
            /// <summary>Allows adding and modifying annotations. Defaults to <see langword="true"/>.</summary>
            public bool AllowAnnotation { get; set; } = true;

            /// <summary>Allows inserting, rotating or deleting pages and adding bookmarks. Defaults to <see langword="true"/>.</summary>
            public bool AllowAssembly { get; set; } = true;

            /// <summary>Allows copying text and graphics. Defaults to <see langword="true"/>.</summary>
            public bool AllowContentExtraction { get; set; } = true;

            /// <summary>Allows filling form fields. Defaults to <see langword="true"/>.</summary>
            public bool AllowFillingForms { get; set; } = true;

            /// <summary>Allows modifying document content. Defaults to <see langword="true"/>.</summary>
            public bool AllowModification { get; set; } = true;

            /// <summary>Allows printing. Defaults to <see langword="true"/>.</summary>
            public bool AllowPrinting { get; set; } = true;

            /// <summary>Encrypts metadata streams as well. Defaults to <see langword="true"/>.</summary>
            public bool EncryptMetadata { get; set; } = true;
        }

        /// <summary>
        /// Describes one overlay or underlay source: the layer PDF file and how its pages are
        /// mapped onto the output pages.
        /// </summary>
        /// <remarks>
        /// <see cref="TargetPages"/> selects output pages and defaults to all of them.
        /// <see cref="SourcePages"/> selects the layer pages used for the first output pages and
        /// defaults to all layer pages. <see cref="RepeatSourcePages"/> cycles after the initial
        /// source pages are exhausted; when it is supplied without <see cref="SourcePages"/>, the
        /// repetition starts with the first target page.
        /// </remarks>
        public sealed class LayerConfiguration
        {
            /// <summary>
            /// The path of the overlay or underlay PDF file. The file is read immediately.
            /// </summary>
            public string FilePath { get; set; } = string.Empty;

            /// <summary>
            /// The output pages that receive the layer. Defaults to all output pages.
            /// </summary>
            public string? TargetPages { get; set; }

            /// <summary>
            /// The layer pages used first, in selector order. Defaults to all layer pages.
            /// </summary>
            public string? SourcePages { get; set; }

            /// <summary>
            /// Layer pages that repeat after <see cref="SourcePages"/> is exhausted. No repetition
            /// happens when this is not set.
            /// </summary>
            public string? RepeatSourcePages { get; set; }
        }

        /// <summary>
        /// The role of an attached file relative to the PDF document, written as the file
        /// specification's <c>/AFRelationship</c> entry when the attachment is also listed in the
        /// document's associated-files array.
        /// </summary>
        public enum DocumentAttachmentRelationship
        {
            /// <summary>The attachment contains data used by the document.</summary>
            Data,

            /// <summary>The attachment is a source document from which the PDF was produced.</summary>
            Source,

            /// <summary>The attachment is an alternative representation of the document's content.</summary>
            Alternative,

            /// <summary>The attachment supplements the document.</summary>
            Supplement,

            /// <summary>The relationship is not specified. This is the default.</summary>
            Unspecified
        }

        /// <summary>
        /// Describes a file embedded in the output document.
        /// </summary>
        /// <remarks>
        /// <see cref="FilePath"/> is read when <see cref="AddAttachment"/> is called, so the file is
        /// not locked afterwards and later changes to the file do not affect the operation. The
        /// attachment is stored with its exact bytes; it is not re-encoded.
        /// </remarks>
        public sealed class DocumentAttachment
        {
            /// <summary>
            /// The unique key of the attachment in the embedded-files name tree. Defaults to
            /// <see cref="AttachmentName"/>.
            /// </summary>
            public string? Key { get; set; }

            /// <summary>
            /// The path of the file to embed. Required.
            /// </summary>
            public string FilePath { get; set; } = string.Empty;

            /// <summary>
            /// The file name shown to readers. Defaults to the file name of <see cref="FilePath"/>.
            /// Unicode names are supported and written as a Unicode text string.
            /// </summary>
            public string? AttachmentName { get; set; }

            /// <summary>The creation date of the attached file. Omitted when not set.</summary>
            public DateTime? CreationDate { get; set; }

            /// <summary>The modification date of the attached file. Omitted when not set.</summary>
            public DateTime? ModificationDate { get; set; }

            /// <summary>
            /// The media type of the attachment, such as <c>text/plain</c>, written as the embedded
            /// file stream's subtype. Defaults to <c>application/octet-stream</c>.
            /// </summary>
            public string? MimeType { get; set; }

            /// <summary>A description of the attachment. Omitted when not set.</summary>
            public string? Description { get; set; }

            /// <summary>
            /// Replaces an attachment that already uses the same key. When this is
            /// <see langword="false"/> and the key is already present, saving throws
            /// <see cref="PdfOperationException"/> instead of losing either attachment.
            /// </summary>
            public bool Replace { get; set; }

            /// <summary>The relationship of the attachment to the document.</summary>
            public DocumentAttachmentRelationship Relationship { get; set; } = DocumentAttachmentRelationship.Unspecified;
        }

        /// <summary>
        /// Loads an unprotected PDF file.
        /// </summary>
        public static DocumentOperation LoadFile(string filePath)
        {
            return Load(ReadFile(filePath, nameof(filePath)), filePath);
        }

        /// <summary>
        /// Loads a PDF file, using the supplied password when the file is protected.
        /// </summary>
        public static DocumentOperation LoadFile(string filePath, string? password)
        {
            return Load(ReadFile(filePath, nameof(filePath)), filePath, password);
        }

        /// <summary>
        /// Loads a PDF from a byte array. The array is read-only to the operation and is not modified.
        /// </summary>
        public static DocumentOperation Load(byte[] data)
        {
            return Load(ValidateBytes(data), "(byte array)");
        }

        /// <summary>
        /// Loads a PDF from a byte array, using the supplied password when the document is protected.
        /// </summary>
        public static DocumentOperation Load(byte[] data, string? password)
        {
            return Load(ValidateBytes(data), "(byte array)", password);
        }

        /// <summary>
        /// Loads a PDF from a borrowed stream. The stream is read from its current position and is
        /// left open; it can be disposed as soon as this call returns.
        /// </summary>
        public static DocumentOperation Load(Stream stream)
        {
            return Load(ReadStream(stream, nameof(stream)), "(stream)");
        }

        /// <summary>
        /// Loads a PDF from a borrowed stream, using the supplied password when the document is protected.
        /// </summary>
        public static DocumentOperation Load(Stream stream, string? password)
        {
            return Load(ReadStream(stream, nameof(stream)), "(stream)", password);
        }

        /// <summary>
        /// Selects pages from the loaded document using a compact page selector:
        /// <c>1</c>, <c>z</c> (last page), <c>r2</c> (second to last), <c>1,6,4</c>,
        /// <c>3-7</c>, <c>7-3</c>, <c>1-20:even</c>, <c>1-10,x3-4</c>.
        /// Calling this method more than once appends the selections in order.
        /// </summary>
        public DocumentOperation TakePages(string pageSelector)
        {
            if (pageSelector == null)
                throw new ArgumentNullException(nameof(pageSelector));

            if (string.IsNullOrWhiteSpace(pageSelector))
                throw new ArgumentException("A page selector must not be empty.", nameof(pageSelector));

            _selections.Add(new PageSelection(0, pageSelector));
            return this;
        }

        /// <summary>
        /// Merges every page of another PDF file. When no selection was made for the loaded document
        /// yet, all of its pages are kept before the merged file.
        /// </summary>
        public DocumentOperation MergeFile(string filePath)
        {
            return MergeFile(filePath, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF file, in selector order.
        /// </summary>
        public DocumentOperation MergeFile(string filePath, string? pageSelector)
        {
            var source = AddSource(ReadFile(filePath, nameof(filePath)), filePath);
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Merges pages of another PDF supplied as a byte array.
        /// </summary>
        public DocumentOperation Merge(byte[] data)
        {
            return Merge(data, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF supplied as a byte array.
        /// </summary>
        public DocumentOperation Merge(byte[] data, string? pageSelector)
        {
            var source = AddSource(ValidateBytes(data), "(byte array)");
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Merges pages of another PDF supplied as a borrowed stream. The stream is read from its
        /// current position and left open.
        /// </summary>
        public DocumentOperation Merge(Stream stream)
        {
            return Merge(stream, null);
        }

        /// <summary>
        /// Merges selected pages of another PDF supplied as a borrowed stream.
        /// </summary>
        public DocumentOperation Merge(Stream stream, string? pageSelector)
        {
            var source = AddSource(ReadStream(stream, nameof(stream)), "(stream)");
            AppendMerge(source, pageSelector);
            return this;
        }

        /// <summary>
        /// Applies a PDF file beneath the selected output pages. Every underlay is drawn in the
        /// order the methods were called, before the page's own content.
        /// </summary>
        public DocumentOperation UnderlayFile(LayerConfiguration configuration)
        {
            AddLayer(configuration, _underlays);
            return this;
        }

        /// <summary>
        /// Applies a PDF file above the selected output pages. Every overlay is drawn in the order
        /// the methods were called, after the page's own content.
        /// </summary>
        public DocumentOperation OverlayFile(LayerConfiguration configuration)
        {
            AddLayer(configuration, _overlays);
            return this;
        }

        /// <summary>
        /// Embeds a file in the output document and lists it in the embedded-files name tree and in
        /// the document's associated-files array. The attachment plan is copied now, including the
        /// file bytes, so later changes to the configuration object or to the file do not affect the
        /// operation. Attachments carried by the primary loaded document are kept as well.
        /// </summary>
        public DocumentOperation AddAttachment(DocumentAttachment attachment)
        {
            if (attachment == null)
                throw new ArgumentNullException(nameof(attachment));

            if (string.IsNullOrWhiteSpace(attachment.FilePath))
                throw new ArgumentException("An attachment file path must not be empty.", nameof(attachment));

            var name = string.IsNullOrWhiteSpace(attachment.AttachmentName)
                ? Path.GetFileName(attachment.FilePath)
                : attachment.AttachmentName!;

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(
                    "An attachment name could not be derived from the file path.", nameof(attachment));

            var key = string.IsNullOrWhiteSpace(attachment.Key) ? name : attachment.Key!;
            var mimeType = ValidateMimeType(attachment.MimeType);

            var data = ReadAttachment(attachment.FilePath);

            _attachments.Add(new AttachmentOperation(
                key,
                data,
                name,
                attachment.CreationDate,
                attachment.ModificationDate,
                mimeType,
                string.IsNullOrWhiteSpace(attachment.Description) ? null : attachment.Description,
                attachment.Replace,
                attachment.Relationship));

            return this;
        }

        /// <summary>
        /// Adds a well-formed XML fragment to the output document's XMP metadata. The fragment is
        /// appended, in call order, inside the metadata packet's first <c>rdf:Description</c>
        /// element. When the primary loaded document has XMP metadata, that packet is extended and
        /// kept; otherwise the metadata generated for the output is extended.
        /// </summary>
        /// <remarks>
        /// The fragment must contain at least one XML element and declare every namespace prefix it
        /// uses. Extending metadata writes a small PDF incremental update after the document is
        /// saved, so it cannot be combined with <see cref="Encrypt(Encryption256Bit)"/>.
        /// </remarks>
        public DocumentOperation ExtendMetadata(string metadata)
        {
            PdfXmp.Validate(metadata);
            _metadataExtensions.Add(metadata);
            return this;
        }

        /// <summary>
        /// Encrypts the output with 40-bit RC4. The encryption plan is copied now, so later changes
        /// to the configuration object do not affect the operation. Encryption can be set only once.
        /// </summary>
        public DocumentOperation Encrypt(Encryption40Bit encryption)
        {
            return SetEncryption(encryption, EncryptionStrength.Rc4_40Bit);
        }

        /// <summary>
        /// Encrypts the output with 128-bit RC4. The encryption plan is copied now, so later changes
        /// to the configuration object do not affect the operation. Encryption can be set only once.
        /// </summary>
        public DocumentOperation Encrypt(Encryption128Bit encryption)
        {
            return SetEncryption(encryption, EncryptionStrength.Rc4_128Bit);
        }

        /// <summary>
        /// Encrypts the output with 256-bit AES. This is the recommended strength. The encryption
        /// plan is copied now, so later changes to the configuration object do not affect the
        /// operation. Encryption can be set only once.
        /// </summary>
        public DocumentOperation Encrypt(Encryption256Bit encryption)
        {
            return SetEncryption(encryption, EncryptionStrength.Aes_256Bit);
        }

        /// <summary>
        /// Produces an unencrypted output. An encrypted input is decrypted with the password
        /// supplied when it was loaded. Calling this method also clears an encryption plan recorded
        /// earlier, so the last of <see cref="Encrypt(Encryption256Bit)"/> and
        /// <see cref="Decrypt"/> wins.
        /// </summary>
        public DocumentOperation Decrypt()
        {
            _encryption = null;
            return this;
        }

        /// <summary>
        /// Produces an unencrypted output and removes restrictions attached to digital signatures.
        /// The output document is always rebuilt from its pages, so certification signatures,
        /// approval signatures and their restrictions are not carried over; their visual
        /// appearances remain page content. Calling this method also clears an encryption plan
        /// recorded earlier.
        /// </summary>
        public DocumentOperation RemoveRestrictions()
        {
            _encryption = null;
            return this;
        }

        /// <summary>
        /// Executes the recorded operations and writes the result to a file. The file is only
        /// created after the operation has completed successfully.
        /// </summary>
        public void Save(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("An output file path must not be empty.", nameof(filePath));

            var output = SaveToBytes();
            File.WriteAllBytes(filePath, output);
        }

        /// <summary>
        /// Executes the recorded operations and writes the result to a borrowed stream. The stream is
        /// left open and remains writable.
        /// </summary>
        public void Save(Stream output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            if (!output.CanWrite)
                throw new ArgumentException("The output stream must be writable.", nameof(output));

            var bytes = SaveToBytes();
            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Executes the recorded operations and returns the resulting PDF bytes.
        /// </summary>
        public byte[] SaveToBytes()
        {
            if (_metadataExtensions.Count > 0 && _encryption != null)
            {
                throw new InvalidOperationException(
                    "Metadata extensions cannot be combined with encryption. " +
                    "Extending XMP metadata writes an incremental update after the document is saved.");
            }

            return PdfDocumentMerger.Execute(
                _sources,
                _selections,
                _underlays,
                _overlays,
                _attachments,
                _metadataExtensions,
                _encryption);
        }

        private DocumentOperation SetEncryption(EncryptionBase encryption, EncryptionStrength strength)
        {
            if (encryption == null)
                throw new ArgumentNullException(nameof(encryption));

            if (_encryption != null)
                throw new InvalidOperationException("Encryption process can be set only once");

            if (string.IsNullOrEmpty(encryption.OwnerPassword))
                throw new ArgumentException(
                    "An owner password is required when encrypting a PDF document.", nameof(encryption));

            var userPassword = encryption.UserPassword ?? string.Empty;
            var ownerPassword = encryption.OwnerPassword;

            switch (encryption)
            {
                case Encryption40Bit options:
                    _encryption = new EncryptionSettings(
                        strength,
                        userPassword,
                        ownerPassword,
                        options.AllowAnnotation,
                        allowAssembly: true,
                        options.AllowContentExtraction,
                        allowFillingForms: true,
                        options.AllowModification,
                        options.AllowPrinting,
                        encryptMetadata: true);
                    break;

                case Encryption128Bit options:
                    _encryption = new EncryptionSettings(
                        strength,
                        userPassword,
                        ownerPassword,
                        options.AllowAnnotation,
                        options.AllowAssembly,
                        options.AllowContentExtraction,
                        options.AllowFillingForms,
                        options.AllowModification,
                        options.AllowPrinting,
                        options.EncryptMetadata);
                    break;

                case Encryption256Bit options:
                    _encryption = new EncryptionSettings(
                        strength,
                        userPassword,
                        ownerPassword,
                        options.AllowAnnotation,
                        options.AllowAssembly,
                        options.AllowContentExtraction,
                        options.AllowFillingForms,
                        options.AllowModification,
                        options.AllowPrinting,
                        options.EncryptMetadata);
                    break;

                default:
                    throw new ArgumentException("Unsupported encryption configuration.", nameof(encryption));
            }

            return this;
        }

        private void AddLayer(LayerConfiguration configuration, List<LayerOperation> layers)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var targetPages = ValidateSelector(configuration.TargetPages, nameof(configuration.TargetPages));
            var sourcePages = ValidateSelector(configuration.SourcePages, nameof(configuration.SourcePages));
            var repeatSourcePages = ValidateSelector(configuration.RepeatSourcePages, nameof(configuration.RepeatSourcePages));

            var data = ReadFile(configuration.FilePath, nameof(configuration.FilePath));

            layers.Add(new LayerOperation(
                data,
                configuration.FilePath,
                targetPages,
                sourcePages,
                repeatSourcePages));
        }

        private static string ValidateMimeType(string? mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
                return "application/octet-stream";

            var value = mimeType;
            var separator = value.IndexOf('/');

            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new ArgumentException(
                    $"'{mimeType}' is not a valid media type. Use a type and subtype such as 'text/plain'.",
                    nameof(DocumentAttachment.MimeType));
            }

            foreach (var character in value)
            {
                // The media type is written as a PDF name, so only printable ASCII without spaces or
                // delimiters is accepted. Parameters such as "; charset=utf-8" are not supported.
                if (character < 0x21 || character > 0x7E)
                {
                    throw new ArgumentException(
                        $"'{mimeType}' is not a valid media type. Only printable ASCII without spaces is supported.",
                        nameof(DocumentAttachment.MimeType));
                }
            }

            return value;
        }

        private static byte[] ReadAttachment(string filePath)
        {
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new PdfOperationException(
                    $"The attachment file '{filePath}' could not be read.", exception);
            }
        }

        private static string? ValidateSelector(string? selector, string parameterName)
        {
            if (selector == null)
                return null;

            if (string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("A page selector must not be empty.", parameterName);

            return selector;
        }

        private static DocumentOperation Load(byte[] data, string name, string? password = null)
        {
            var operation = new DocumentOperation();
            operation.AddSource(data, name, password);
            return operation;
        }

        private int AddSource(byte[] data, string name, string? password = null)
        {
            _sources.Add(new InputSource(data, name, password));
            return _sources.Count - 1;
        }

        private void AppendMerge(int sourceIndex, string? pageSelector)
        {
            if (_selections.Count == 0)
                _selections.Add(new PageSelection(0, DefaultSelector));

            _selections.Add(new PageSelection(sourceIndex, pageSelector ?? DefaultSelector));
        }

        private static byte[] ReadFile(string filePath, string parameterName)
        {
            if (filePath == null)
                throw new ArgumentNullException(parameterName);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("An input file path must not be empty.", parameterName);

            return File.ReadAllBytes(filePath);
        }

        private static byte[] ReadStream(Stream stream, string parameterName)
        {
            if (stream == null)
                throw new ArgumentNullException(parameterName);

            if (!stream.CanRead)
                throw new ArgumentException("The input stream must be readable.", parameterName);

            using var buffer = new MemoryStream();

            try
            {
                stream.CopyTo(buffer);
            }
            catch (IOException exception)
            {
                throw new PdfOperationException("The input PDF stream could not be read.", exception);
            }

            if (buffer.Length == 0)
                throw new PdfOperationException("The input PDF stream is empty.");

            return buffer.ToArray();
        }

        private static byte[] ValidateBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length == 0)
                throw new PdfOperationException("The input PDF byte array is empty.");

            return data;
        }

        internal sealed class InputSource
        {
            public InputSource(byte[] data, string name, string? password)
            {
                Data = data;
                Name = name;
                Password = password;
            }

            public byte[] Data { get; }
            public string Name { get; }
            public string? Password { get; }
        }

        internal readonly struct PageSelection
        {
            public PageSelection(int sourceIndex, string selector)
            {
                SourceIndex = sourceIndex;
                Selector = selector;
            }

            public int SourceIndex { get; }
            public string Selector { get; }
        }

        internal enum EncryptionStrength
        {
            Rc4_40Bit,
            Rc4_128Bit,
            Aes_256Bit
        }

        internal sealed class EncryptionSettings
        {
            public EncryptionSettings(
                EncryptionStrength strength,
                string userPassword,
                string ownerPassword,
                bool allowAnnotation,
                bool allowAssembly,
                bool allowContentExtraction,
                bool allowFillingForms,
                bool allowModification,
                bool allowPrinting,
                bool encryptMetadata)
            {
                Strength = strength;
                UserPassword = userPassword;
                OwnerPassword = ownerPassword;
                AllowAnnotation = allowAnnotation;
                AllowAssembly = allowAssembly;
                AllowContentExtraction = allowContentExtraction;
                AllowFillingForms = allowFillingForms;
                AllowModification = allowModification;
                AllowPrinting = allowPrinting;
                EncryptMetadata = encryptMetadata;
            }

            public EncryptionStrength Strength { get; }
            public string UserPassword { get; }
            public string OwnerPassword { get; }
            public bool AllowAnnotation { get; }
            public bool AllowAssembly { get; }
            public bool AllowContentExtraction { get; }
            public bool AllowFillingForms { get; }
            public bool AllowModification { get; }
            public bool AllowPrinting { get; }
            public bool EncryptMetadata { get; }
        }

        internal sealed class LayerOperation
        {
            public LayerOperation(
                byte[] data,
                string name,
                string? targetPages,
                string? sourcePages,
                string? repeatSourcePages)
            {
                Data = data;
                Name = name;
                TargetPages = targetPages;
                SourcePages = sourcePages;
                RepeatSourcePages = repeatSourcePages;
            }

            public byte[] Data { get; }
            public string Name { get; }
            public string? TargetPages { get; }
            public string? SourcePages { get; }
            public string? RepeatSourcePages { get; }
        }

        internal sealed class AttachmentOperation
        {
            public AttachmentOperation(
                string key,
                byte[] data,
                string name,
                DateTime? creationDate,
                DateTime? modificationDate,
                string mimeType,
                string? description,
                bool replace,
                DocumentAttachmentRelationship relationship)
            {
                Key = key;
                Data = data;
                Name = name;
                CreationDate = creationDate;
                ModificationDate = modificationDate;
                MimeType = mimeType;
                Description = description;
                Replace = replace;
                Relationship = relationship;
            }

            public string Key { get; }
            public byte[] Data { get; }
            public string Name { get; }
            public DateTime? CreationDate { get; }
            public DateTime? ModificationDate { get; }
            public string MimeType { get; }
            public string? Description { get; }
            public bool Replace { get; }
            public DocumentAttachmentRelationship Relationship { get; }
        }
    }

    /// <summary>
    /// Executes a recorded operation plan against PDFsharp. Kept separate from the fluent surface so
    /// the parsing and import behavior can be tested and reasoned about independently.
    /// </summary>
    internal static class PdfDocumentMerger
    {
        public static byte[] Execute(
            IReadOnlyList<DocumentOperation.InputSource> sources,
            IReadOnlyList<DocumentOperation.PageSelection> selections,
            IReadOnlyList<DocumentOperation.LayerOperation> underlays,
            IReadOnlyList<DocumentOperation.LayerOperation> overlays,
            IReadOnlyList<DocumentOperation.AttachmentOperation> attachments,
            IReadOnlyList<string> metadataExtensions,
            DocumentOperation.EncryptionSettings? encryption)
        {
            if (sources.Count == 0)
                throw new InvalidOperationException("Load a PDF document before saving an operation.");

            var opened = new List<PdfDocument>(sources.Count);
            // PDFsharp may read from the stream while the imported pages are used, so the streams
            // must stay alive for as long as the source documents are open.
            var buffers = new List<MemoryStream>(sources.Count);

            try
            {
                foreach (var source in sources)
                    opened.Add(Open(source, buffers));

                var effective = selections.Count == 0
                    ? new[] { new DocumentOperation.PageSelection(0, "1-z") }
                    : selections;

                using var destination = new PdfDocument();

                foreach (var selection in effective)
                {
                    var source = opened[selection.SourceIndex];
                    var pages = PageRangeSelector.Resolve(selection.Selector, source.PageCount, sources[selection.SourceIndex].Name);

                    foreach (var page in pages)
                        destination.AddPage(source.Pages[page]);
                }

                if (destination.PageCount == 0)
                    throw new PdfOperationException("The page selection produced no pages.");

                CopyMetadata(opened[0], destination);

                PdfAttachments.Apply(destination, opened[0], attachments);
                PdfLayers.Apply(destination, underlays, overlays);
                PdfSecurity.Apply(destination, encryption);

                // The base XMP packet is read from the primary input before the source documents are
                // disposed. PDFsharp regenerates the output metadata when the destination is saved,
                // so preserving an input packet has to happen in the appended update.
                var baseMetadata = metadataExtensions.Count == 0
                    ? null
                    : PdfXmp.Read(opened[0], sources[0].Name);

                using var buffer = new MemoryStream();
                destination.Save(buffer, false);

                var result = buffer.ToArray();

                return metadataExtensions.Count == 0
                    ? result
                    : PdfXmp.Apply(result, sources[0].Name, baseMetadata, metadataExtensions);
            }
            catch (PdfSharpException exception)
            {
                throw new PdfOperationException(
                    "The input PDF could not be read. It may be malformed, truncated or protected by an unsupported password.", exception);
            }
            finally
            {
                foreach (var document in opened)
                    document.Dispose();

                foreach (var buffer in buffers)
                    buffer.Dispose();
            }
        }

        private static PdfDocument Open(DocumentOperation.InputSource source, List<MemoryStream> buffers)
        {
            var buffer = new MemoryStream(source.Data, writable: false);
            buffers.Add(buffer);

            try
            {
                return source.Password == null
                    ? PdfReader.Open(buffer, PdfDocumentOpenMode.Import)
                    : PdfReader.Open(buffer, source.Password, PdfDocumentOpenMode.Import, new PdfReaderOptions());
            }
            catch (Exception exception) when (!(exception is PdfOperationException))
            {
                // PDFsharp reports malformed and truncated files with several different exception
                // types, including plain System.Exception. The message is inspected only to give
                // security failures a precise description; it never includes the supplied password.
                var message = exception.Message ?? string.Empty;

                if (message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new PdfOperationException(
                        $"'{source.Name}' is protected with a security handler that is not supported. " +
                        "Only password-based standard security is supported.", exception);
                }

                if (message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new PdfOperationException(
                        $"'{source.Name}' could not be opened because the password is missing or incorrect.", exception);
                }

                throw new PdfOperationException(
                    $"'{source.Name}' could not be opened. It may be malformed or truncated.", exception);
            }
        }

        private static void CopyMetadata(PdfDocument source, PdfDocument destination)
        {
            var info = source.Info;
            var target = destination.Info;

            target.Title = Copy(info.Title, target.Title);
            target.Author = Copy(info.Author, target.Author);
            target.Subject = Copy(info.Subject, target.Subject);
            target.Keywords = Copy(info.Keywords, target.Keywords);
            target.Creator = Copy(info.Creator, target.Creator);

            var creationDate = info.CreationDate.ToLocalTime();

            if (creationDate.Year > 1)
                target.CreationDate = creationDate;

            var modificationDate = info.ModificationDate.ToLocalTime();

            if (modificationDate.Year > 1)
                target.ModificationDate = modificationDate;
        }

        private static string Copy(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
