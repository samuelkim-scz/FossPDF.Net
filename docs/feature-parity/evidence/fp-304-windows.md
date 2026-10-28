# FP-304 Windows validation

Recorded October 28, 2026, for [FP-304 / issue #23](https://github.com/samuelkim-scz/FossPDF.Net/issues/23), on `feature/fp-304-pdf-encryption` based on `feature/fp-303-pdf-overlays` at commit `363422e`.

Environment: Windows x64, .NET SDK 9.0.305 (SDKs 8.0.319/9.0.102/9.0.305/10.0.301 installed), .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin). Independent inspection used an isolated Python 3.13 virtual environment with pypdf 6.21.3 and cryptography 50.0.2, run with `-I` from a separate script directory.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 463 passed, 0 failed, 13 skipped (9 planned contracts + 4 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 563 passed, 0 failed, 9 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 32 passed / 9 skipped on .NET 9 |
| `PDF-304` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| `PdfSecurityTests` | 21 passed on .NET 9 |
| PDF security examples | 2 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; qpdf and veraPDF not installed, so those conformance checks were skipped |
| Independent pypdf inspection | V5/R6/256-bit/AESV3, `/P -2072`; user and owner passwords decrypt, empty/wrong passwords do not; decrypted content equals the source |

## Verified behavior

- `Encrypt(Encryption40Bit/128Bit/256Bit)` records one encryption plan on a `DocumentOperation`; the values are copied at call time, so mutating the configuration object afterwards has no effect. A second `Encrypt` call throws `InvalidOperationException`, matching the pinned target.
- The owner password is required and must not be empty; a missing or empty value throws `ArgumentException`. The user password is optional: `null`/empty produces a document that opens without a password while the permission flags still apply. Neither password is written to the output, and exception messages never contain a password.
- `Encryption256Bit` writes V5/R6, `/Length 256`, `/CFM /AESV3`, a `%PDF-2.0` header and `/P -2072` when printing and copying are denied. `Encryption128Bit` writes V4/R4, `/Length 128`, `/CFM /V2` (RC4) and `%PDF-1.7`. `Encryption40Bit` writes V1/R2, `/Length 40`, no crypt-filter dictionary and `%PDF-1.7`.
- Permission mapping: print bit 3 and, for 128/256, high-quality print bit 12 both follow `AllowPrinting`; modification bit 4, extraction bit 5, annotations bit 6, form filling bit 9 and assembly bit 11 follow their properties. Accessibility bit 10 and reserved bits 7–8 stay set. 40-bit revision 2 exposes only the print/modify/copy/annotation properties; assembly and form filling remain allowed.
- `EncryptMetadata = false` writes `/EncryptMetadata false` for 128/256-bit encryption and leaves the XMP metadata stream unencrypted; 40-bit always encrypts metadata.
- Loading an encrypted input with the user or owner password and saving without `Encrypt` produces an unencrypted document. `Decrypt()` and `RemoveRestrictions()` state that intent and clear a previously recorded plan; the last call wins. The rebuilt output carries no encryption dictionary and no signature restrictions, while signature appearances remain page content.
- Missing/wrong input passwords throw `PdfOperationException` with a source-name message that distinguishes a password problem from malformed input; saving before executing still stages output in memory, so no partial file is written. A security handler the reader does not support throws `PdfOperationException` containing "not supported".
- The input byte array is not modified.

## Independent validation

The `PDF-304` artifacts were inspected with pypdf, a separate implementation from PDFsharp:

```text
source: encrypted=False pages=2
encrypted: encrypted=True
encrypted: V=5 R=6 Length=256 CFM=/AESV3 P=-2072 EncryptMetadata=default
encrypted: decrypt('')=0
encrypted: decrypt('wrong-password')=0
encrypted: decrypt(user)=1 pages=2
encrypted: decrypt(owner)=2 pages=2
decrypted: encrypted=False pages=2
decrypted: dimensions=[(120.0, 100.0), (120.0, 100.0)]
decrypted: page content equal to source=True
```

pypdf reports the user password as the user role (`decrypt=1`) and the owner password as the owner role (`decrypt=2`); empty and wrong passwords are rejected. The decoded page content of the decrypted document equals the source, independently confirming the parity runner's comparison.

## Artifacts

`PDF-304` writes beneath
`Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-304/`: `source.pdf`,
`encrypted.pdf` and `decrypted.pdf`.

The examples write beneath
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-security/` (`invoice.pdf`,
`invoice-protected.pdf`, `invoice-decrypted.pdf`) and
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-security-40-bit/`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore -f net9.0 --filter FullyQualifiedName~PdfSecurityTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfSecurityExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

The pypdf inspection was run outside the repository against the generated artifact directory, with the script and virtual environment in separate temporary directories and the paths passed as arguments.

## Boundaries

- The companion package targets `net9.0` only and the core package does not reference it, per ADR-0001.
- Only password-based standard security is written. Certificate/public-key security, custom handlers and password recovery are out of scope. AES-128 output is not offered; the 128-bit mode is RC4 to match the pinned target.
- Permission flags are advisory for conforming readers and are not enforced by this operation on imported inputs. They are not DRM.
- An encrypted PDF cannot be PDF/A conforming. PDF/A output is not implemented yet (FP-402); its validation must reject encrypted output when it is added.
- qpdf and veraPDF were unavailable, so no independent PDF/A or profile conformance claim is made. Independent inspection is limited to pypdf's reading of the standard security handler, permissions and page content.
