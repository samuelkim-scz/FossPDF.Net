# Packaged runtime consumers

This non-packable .NET 9 console fixture is the executable for FP-503 / RUN-503.
See the [runtime deployment guide](../../docs/feature-parity/RUNTIME_SUPPORT.md) for the support
matrix, exact scope, compiler prerequisites and reproducible package-based qualification commands.

`RuntimeFeature=Core|Fonts|Svg|Pdf` selects the dependency graph.
`QualificationMode=jit|trimmed|aot` selects project-local publish settings. Keep those settings
local: passing `PublishTrimmed` globally to the source graph also applies it to netstandard2.1.
The Python driver publishes each profile sequentially and executes the published native launcher.
