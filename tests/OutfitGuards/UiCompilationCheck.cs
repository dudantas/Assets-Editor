using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class UiCompilationCheck {
    // Type-check the changed UI file against maintained dependencies, without building the WPF app.
    public static void Run(string editorDirectory, string coreReferences, string desktopReferences, string baselineSource) {
        string[] sources = {
            "DatEditor.xaml.cs", "obj/Debug/net10.0-windows7.0/DatEditor.g.cs",
            "AppearanceSpriteLayout.cs", "OutfitMaskValidator.cs", "ImageExporter.cs"
        };
        var trees = sources.Select(path => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(editorDirectory, path)), path: path)).ToArray();
        string bin = Path.Combine(editorDirectory, "bin/Debug/net10.0-windows7.0");
        var files = Directory.GetFiles(desktopReferences, "*.dll")
            .Concat(Directory.GetFiles(coreReferences, "*.dll"))
            .Concat(Directory.GetFiles(bin, "*.dll"));
        var references = files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => MetadataReference.CreateFromFile(group.First()));
        var compilation = CSharpCompilation.Create("OutfitGuardUiCheck", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var baseline = compilation.ReplaceSyntaxTree(trees[0], CSharpSyntaxTree.ParseText(File.ReadAllText(baselineSource), path: sources[0]));
        var baselineErrors = baseline.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()).ToHashSet();
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
            !baselineErrors.Contains(diagnostic.Id + ":" + diagnostic.GetMessage())).ToArray();
        if (errors.Length > 0) {
            throw new InvalidOperationException($"{errors.Length} compilation errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Take(20).Select(error => error.ToString())));
        }
        Console.WriteLine($"PASS no new UI compilation errors ({baselineErrors.Count} baseline diagnostics from cached UI/assembly identity)");
    }
}
