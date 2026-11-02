# Unreleased: QuestPDF 2026.9.1 parity work

The implementation targets the accepted QuestPDF 2026.9.1 contracts. **Final release acceptance
is pending native previewer qualification**, tracked by #31 and #33. Package projects currently
retain version 2026.5.1; these notes do not announce a new NuGet publication.

## Added capabilities

- Paragraph justification, logical alignment, first-line indentation, word spacing, line clamping,
  OpenType controls and richer text decorations; complex-script/CJK qualification.
- Multi-column flow/balancing, page-aware pagination helpers, lazy/cached composition,
  independent-axis shrinking, stacking, inline alignment and content-position capture.
- Optional vector SVG input/preloading, per-image optimization, JPEG/WebP page export,
  rounded containers, border alignment, gradients, line styling and shadows.
- Generated-document merging and an optional .NET 9 PDF operations companion for existing-page
  selection/merge, overlays/underlays, security, attachments, XMP and qpdf optimization.
- Semantic/tagged PDF, six validated PDF/A-2/3 profiles, PDF/UA-1 and validated ZUGFeRD/Factur-X
  packaging using application-deployed validators and externally authored invoice XML.
- Optional preview inspection transport and native hierarchy/search, source navigation,
  measurement, magnification and diagnostics tools.
- Package-based .NET 9 JIT, full-trimming and NativeAOT qualification on five native RIDs.

## Compatibility and fixes

Core continues to target `netstandard2.1` and `net9.0`; .NET 8 and .NET 9 test suites remain.
Existing document generation, metadata, PNG export, picture-only preview and legacy settings
workflows are retained. New per-document settings isolate request overrides from process defaults.
The maximum-page boundary permits a complete document exactly at its configured limit.

Trimmed layout diagnostics preserve their public configuration properties. Both font managers
add explicit resource-assembly registration for AOT applications; the managed caller-discovery
API remains. Preview inspection fixes include deferred geometry retention, queued-frame picture
lifetime, render-pass-safe scrollbar publication and asynchronous shutdown cleanup.
The previewer's Documentation button now opens the repository's tool guide instead of the
unresolvable `www.fosspdf.com` address.

## Deployment and remaining boundaries

The SVG/PDF operations companions and interactive previewer target .NET 9. SVG/PDF libraries
remain optional, with exact reviewed dependency pins and required notices. Deploy native Skia/
HarfBuzz assets and any required qpdf/Java/veraPDF/Mustang tools explicitly.

The 60-profile deployment corpus covers Windows x64/ARM64, Linux x64/ARM64 and macOS x64 for
its declared features. It does not qualify every API, the previewer UI or external validators for
AOT. Other RIDs/OS versions need execution evidence. Linux/macOS native previewer smoke checks
remain pending; no full-parity release declaration is made.

See the [migration guide](docs/feature-parity/MIGRATION.md),
[audit and remaining gates](docs/feature-parity/PARITY_AUDIT.md),
[runtime matrix](docs/feature-parity/RUNTIME_SUPPORT.md) and
[dependency policy](docs/feature-parity/NATIVE_DEPENDENCIES.md). A next parity target remains
unselected and requires an explicit separately versioned rebaseline.
