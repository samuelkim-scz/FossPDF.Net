Interactive hierarchy inspection, measurement, search, magnification, source navigation and
diagnostics are documented in [the tools guide](../../docs/feature-parity/PREVIEWER_TOOLS.md).
Use the previewer sample with `--inspect` to enable source/text metadata.

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

Remove nuget locally 

```
dotnet tool uninstall FossPDF.Previewer --global
```
