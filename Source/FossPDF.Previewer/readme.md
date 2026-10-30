Install nuget locally (in directory where nupkg file is located)

```
dotnet tool install --global --add-source . FossPDF.Previewer --global
```

Run on default port

```
FossPDF-previewer
```

Run on custom port

```
FossPDF-previewer 12500
```

The updated tool accepts optional version 1 inspection data alongside legacy picture-only updates.
Enable it with `document.ShowInPreviewer(new PreviewerOptions { EnableInspection = true })` and
annotate source boundaries with `.Inspect("unique-instance-id")`. See the
[inspection protocol and ownership guide](../../docs/feature-parity/PREVIEW_INSPECTION.md).

Remove nuget locally 

```
dotnet tool uninstall FossPDF.Previewer --global
```
