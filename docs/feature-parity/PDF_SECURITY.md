# Encryption, decryption and permissions

[FP-304 / issue #23](https://github.com/samuelkim-scz/FossPDF.Net/issues/23) adds password-based
encryption and decryption to [existing-PDF operations](PDF_OPERATIONS.md). It writes the PDF
standard security handler, so the result opens in ordinary readers with the configured passwords
instead of in a FossPDF-specific format.

```csharp
using FossPDF.Fluent;

DocumentOperation
    .LoadFile("report.pdf")
    .Encrypt(new DocumentOperation.Encryption256Bit
    {
        UserPassword = "reader-password",
        OwnerPassword = "owner-password",
        AllowPrinting = false,
        AllowContentExtraction = false
    })
    .Save("report-protected.pdf");
```

The encryption plan is copied when `Encrypt` is called, so changing the configuration object
afterwards does not affect the operation. Output is staged in memory like every other operation, so
a failed encryption leaves no partial file. Passwords are never written to the output or included
in exception messages.

The API lives in the optional `FossPDF.PdfOperations` companion package, which targets `net9.0`
only and is not referenced by the core package, following
[ADR-0001](decisions/ADR-0001-native-backends.md). Input and output ownership is unchanged from
[PDF_OPERATIONS.md](PDF_OPERATIONS.md): borrowed input streams are read when loaded and left open,
caller byte arrays are not modified, and `Save(Stream)` leaves the output stream open.

## Strengths

`Encrypt` accepts one of three configuration types. All inherit `UserPassword` and
`OwnerPassword` from `EncryptionBase`.

| Configuration | Encryption dictionary | Algorithm | PDF header |
| --- | --- | --- | --- |
| `Encryption40Bit` | V1, R2, `/Length 40` | 40-bit RC4 | `%PDF-1.7` |
| `Encryption128Bit` | V4, R4, `/Length 128`, `/CFM /V2` | 128-bit RC4 | `%PDF-1.7` |
| `Encryption256Bit` | V5, R6, `/Length 256`, `/CFM /AESV3` | 256-bit AES | `%PDF-2.0` |

`Encryption256Bit` is the recommended choice. `Encryption40Bit` exists for compatibility with old
readers and is not suitable for new documents. AES-128 is not offered by the public API; the
128-bit mode deliberately matches the pinned target's RC4 output. The reader side accepts the
standard security handler's RC4 and AES crypt filters that the underlying PDF reader supports, so
AES-encrypted inputs can be loaded and decrypted.

## Passwords

- `OwnerPassword` is **required and must not be empty**. It grants full access and bypasses the
  permission flags. An empty or missing value throws `ArgumentException` when `Encrypt` is called.
  (An empty owner password would make readers treat the user password as the owner password.)
- `UserPassword` is optional. When it is `null` or empty, readers open the document without asking
  for a password, but the configured permission flags still apply.
- Use different values for the two passwords. Using the same value for both is insecure.
- Only one encryption plan can be recorded per operation. A second `Encrypt` call throws
  `InvalidOperationException` ("Encryption process can be set only once").
- `Decrypt()` and `RemoveRestrictions()` clear a previously recorded plan, so the last call wins
  before the operation is saved.

Passwords are passed through to the security handler unchanged; FossPDF performs no
normalization. Avoid trailing whitespace and use characters that your target readers handle
consistently.

## Permissions

The permission flags are written into the encryption dictionary's `/P` value. A denied flag clears
its bit; every flag defaults to `true`.

| Property | PDF permission | `/P` bit |
| --- | --- | --- |
| `AllowPrinting` | Printing | 3 (value 4) |
| `AllowModification` | Changing document content | 4 (8) |
| `AllowContentExtraction` | Copying text and graphics | 5 (16) |
| `AllowAnnotation` | Adding/modifying annotations | 6 (32) |
| `AllowFillingForms` | Filling form fields (128/256 only) | 9 (256) |
| — | Accessibility extraction (always allowed) | 10 (512) |
| `AllowAssembly` | Assembling pages and bookmarks (128/256 only) | 11 (1024) |
| `AllowPrinting` | High-quality printing (128/256 only) | 12 (2048) |

Bits 7–8 are reserved and stay set. `AllowPrinting` controls both the print bit and, for
128/256-bit encryption, the high-quality-print bit, so denying it also denies full-quality output.

`Encryption40Bit` is revision 2, which defines only the print, modify, copy and annotation bits.
It exposes only those four properties; the assembly and form-filling permissions are not
configurable and remain allowed, and metadata is always encrypted.

For example, denying printing and copying with 128- or 256-bit encryption produces `/P -2072`;
denying every configurable permission produces `/P -3392`; denying printing and copying with
40-bit encryption produces `/P -24`.

### Permissions are advisory

The permission flags are a request to conforming readers, not copy protection. A cooperating
reader hides or disables the corresponding operations, but any tool that ignores the flags (or
that has the user password) can still extract content. Encryption protects a document from casual
inspection; it does not provide DRM. Do not rely on permissions to protect confidential data from
someone who is allowed to open the file.

The operation does **not** enforce permission flags found on an input document. Loading an input
whose permissions deny copying does not prevent its pages from being imported, merged or layered.
Only the password needed to open the input is checked.

## Decryption

An encrypted input is opened with the password passed to `LoadFile`/`Load`:

```csharp
DocumentOperation
    .LoadFile("report-protected.pdf", "reader-password")
    .Save("report-plain.pdf");
```

Saving without calling `Encrypt` produces an unencrypted document. The password can be the user
password or the owner password; the owner password additionally gains full access. The output is
rebuilt from the imported pages, so the encryption dictionary, the original permission flags and
any signature restrictions are not carried over.

`Decrypt()` states the intent explicitly and is equivalent to saving without an encryption plan:

```csharp
DocumentOperation
    .LoadFile("report-protected.pdf", "owner-password")
    .Decrypt()
    .Save("report-plain.pdf");
```

`RemoveRestrictions()` also produces an unencrypted output and documents that certification or
approval signature restrictions are removed. Because every operation rebuilds the document from
its pages, those restrictions are removed in either case; a signature's visual appearance remains
ordinary page content, but the digital signature itself is not valid in the rebuilt document.

## Metadata

For 128- and 256-bit encryption, `EncryptMetadata = false` writes `/EncryptMetadata false` and
leaves the XMP metadata stream unencrypted; the document content is still encrypted. The default
is `true`. 40-bit encryption has no such switch and always encrypts metadata.

Primary-input XMP and extensions are preserved and protected according to this setting. See [ATTACHMENTS_XMP.md](ATTACHMENTS_XMP.md) for the supported packaging and metadata workflow.

## Failure behavior

| Condition | Result |
| --- | --- |
| `null` encryption configuration | `ArgumentNullException` |
| Missing or empty owner password | `ArgumentException` |
| Second `Encrypt` call on the same operation | `InvalidOperationException` |
| Input is protected and no password is supplied | `PdfOperationException` |
| Input password is wrong | `PdfOperationException` |
| Input uses an unsupported security handler | `PdfOperationException` |
| Malformed or truncated input | `PdfOperationException` |
| Encryption fails while saving | `PdfOperationException` without passwords or settings in the message |

A failed operation does not create or modify the output file (or stream). Wrong-password and
missing-password messages identify the source by name, never by the attempted password.

## Boundaries

- Only password-based standard security is written. Certificate/public-key security, document
  identifiers as keys and custom handlers are not supported.
- The output is not linearized, and encrypted content is not optimized for fast web viewing
  (linearization is FP-306).
- An encrypted PDF cannot be PDF/A conforming. Do not combine `Encrypt` with a PDF/A profile
  (FP-402); the conformance validation will reject the result.
- Signature validity is not preserved: rebuilding the document invalidates existing signatures,
  and encrypted output cannot be incrementally signed by this API.
- Permission flags do not restrict FossPDF's own operation, as described above.
- Password recovery, key derivation options and certificate-based decryption are out of scope.

## Acceptance and example

`PDF-304` is the executable acceptance fixture. On `net9.0` it encrypts the generated PAG-001
document with 256-bit AES, the fixture's user/owner passwords and printing/copying denied. It
checks the V5/R6/AESV3 encryption dictionary and `/P -2072`, asserts that opening without the user
password or with a wrong password throws `PdfOperationException`, verifies that neither password
appears in the output, and decrypts with both the user and owner passwords to confirm that the two
pages keep their dimensions and identical decoded content.

See [Source/FossPDF.Examples/PdfSecurityExamples.cs](../../Source/FossPDF.Examples/PdfSecurityExamples.cs)
for runnable examples and [evidence/fp-304-windows.md](evidence/fp-304-windows.md) for the recorded
Windows validation, including independent inspection with `pypdf`.

### Reproduce independent inspection

The parity fixture also emits 40-bit and 128-bit compatibility outputs. The CI jobs run the independent reader for all three strengths:

```powershell
python -m pip install -r FossPDF.UnitTests/Parity/requirements-security-validation.txt
python FossPDF.UnitTests/Parity/verify_pdf_security.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-304
```

The script reads the public fixture passwords directly from the acceptance catalog; it never prints them, passes them as process arguments or writes them to its JSON report. It checks standard security handlers, crypt filters, key lengths, permissions, empty/wrong password rejection, user/owner authentication, exact decoded page content and removal of encryption. Tool versions, checks and input hashes are uploaded with the parity artifacts. The Python reader and AES provider are validation dependencies only and are not packaged with FossPDF.