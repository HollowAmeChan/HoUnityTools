# PMX parser validation

This standalone .NET harness compiles the exact pure C# reader used by the Unity
importer. It does not load Unity, create Unity assets, or write model files.
The harness uses the installed .NET 10 SDK; the parser sources are additionally
compiled with C# 8 language rules.

Run the fourteen synthetic assertions without requiring any model assets:

```powershell
dotnet run --project Tools~/PmxImport/ParserValidation/ParserValidation.csproj
```

Validate a local PMX directory recursively and save a summary:

```powershell
dotnet run --project Tools~/PmxImport/ParserValidation/ParserValidation.csproj -- --root 'E:\BaiduSyncdisk\大师\_完整场景' --output 'Temp~/PmxParserValidation.json'
```

Use repeated `--file` arguments for a small set of models. Synthetic assertions
always run first. Failures return process exit code 1. The summary contains counts,
warnings, file failures and source length/mtime checks, never model payloads.

Synthetic coverage: PMX 2.0/2.1; UTF-16 and UTF-8; long texture paths; extended header;
four additional UV channels; SDEF/QDEF; all eleven morph layouts; optional bone
fields and IK limits; all six joint layouts; soft-body anchors/pins; invalid
signature, oversized allocation count, invalid vertex index and truncation.

Actual-model parsing checks binary data and references. It does not establish
Unity mesh, material or rendering correctness; those have separate Unity tests.
