using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class BoundaryGuardTests {
    public static void Run(string editorDirectory, string coreReferences, string desktopReferences, string outputDirectory) {
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(editorDirectory, "DatEditor.xaml.cs")));
        string[] methods = { "TryGetValidatedSnapshot", "ReadValidationSpriteBytes", "ResolveValidationSprite",
            "GetExportSpriteData", "Compile_Click", "UpdateAppearanceObject", "GetSpriteIndex" };
        string actualMethods = string.Join(Environment.NewLine, source.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Where(method => methods.Contains(method.Identifier.Text)));
        string fixture = Fixture.Replace("/* ACTUAL METHODS */", actualMethods);
        var trees = new[] { CSharpSyntaxTree.ParseText(fixture) }.Concat(new[] {
            "AppearanceSpriteLayout.cs", "OutfitMaskValidator.cs"
        }.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(editorDirectory, path)))));
        string bin = Path.Combine(editorDirectory, "bin/Debug/net10.0-windows7.0");
        var references = Directory.GetFiles(desktopReferences, "*.dll")
            .Concat(Directory.GetFiles(coreReferences, "*.dll")).Concat(Directory.GetFiles(bin, "*.dll"))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => MetadataReference.CreateFromFile(group.First()));
        var compilation = CSharpCompilation.Create("OutfitGuardBoundaryTests", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var emitted = new MemoryStream();
        var result = compilation.Emit(emitted);
        if (!result.Success) {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(10)));
        }
        Assembly Resolve(AssemblyLoadContext context, AssemblyName name) {
            string path = Path.Combine(bin, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try {
            emitted.Position = 0;
            Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(emitted);
            assembly.GetType("Assets_Editor.DatEditor").GetMethod("Run").Invoke(null,
                new object[] { Path.Combine(outputDirectory, "run-" + Guid.NewGuid().ToString("N")) });
        } finally {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
        Console.WriteLine("PASS actual compile boundary, frozen pixels, zero sprite exports and legacy remap");
    }

    // Only the environment is stubbed; methods under test are extracted unchanged from source.
    private const string Fixture = """
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using Size = System.Drawing.Size;
using System.Windows;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Tibia.Protobuf.Appearances;
namespace Assets_Editor {
public static class MainWindow {
    public static Appearances appearances;
    public static ConcurrentDictionary<int, MemoryStream> SprLists = new();
    public static string _assetsPath, _datPath;
    public static List<object> catalog = new();
    public static Action AfterValidation;
    public static MemoryStream getSpriteStream(int id) => SprLists[id];
    public static void Log(string error) => throw new Exception(error);
}
public static class ErrorManager {
    public static string LastError;
    public static void ShowError(string error) => LastError = error;
}
public class DatEditor {
    private class AlphaChange { public List<uint> SpriteIds = new(); public byte AlphaValue; }
    private class Queue { public void Enqueue(params object[] arguments) {} }
    private class Bar { public Queue MessageQueue = new(); }
    private class LowercaseContractResolver : DefaultContractResolver {}
    private List<AlphaChange> transparentSheets = new();
    private Bar StatusBar = new();
    private void ProcessTransparentSheets() => MainWindow.AfterValidation?.Invoke();
    /* ACTUAL METHODS */
    private static void Check(bool valid, string error) { if (!valid) throw new Exception(error); }
    private static byte[] Png(int x, int y, Color color) {
        using var bitmap = new Bitmap(6, 6, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(x, y, color);
        using var data = new MemoryStream(); bitmap.Save(data, ImageFormat.Png); return data.ToArray();
    }
    private static Appearance Outfit() {
        var info = new SpriteInfo { PatternWidth=1, PatternHeight=1, PatternDepth=1, Layers=2 };
        info.SpriteId.Add(new uint[] { 1, 2 });
        var appearance = new Appearance { Id=42 };
        appearance.FrameGroup.Add(new FrameGroup { SpriteInfo=info }); return appearance;
    }
    public static void Run(string output) {
        Directory.CreateDirectory(output);
        MainWindow._assetsPath = output;
        MainWindow._datPath = Path.Combine(output, "input.dat");
        string catalogPath = Path.Combine(output, "catalog-content.json");
        File.WriteAllText(catalogPath, "[]"); File.WriteAllBytes(MainWindow._datPath, new byte[] { 7, 8, 9 });
        MainWindow.SprLists[1] = new MemoryStream(Png(2,2,Color.Gray));
        MainWindow.SprLists[2] = new MemoryStream(Png(0,0,Color.Red));
        MainWindow.SprLists[0] = new MemoryStream(Png(0,0,Color.Red));
        MainWindow.appearances = new Appearances(); MainWindow.appearances.Outfit.Add(Outfit());
        var editor = new DatEditor(); editor.Compile_Click(null, null);
        Check(ErrorManager.LastError?.Contains("Outfit 42") == true, "Invalid mask was not reported.");
        Check(File.ReadAllBytes(MainWindow._datPath).SequenceEqual(new byte[] { 7,8,9 }), "Rejected compile changed DAT.");
        Check(File.ReadAllText(catalogPath) == "[]", "Rejected compile changed catalog.");
        Check(!File.Exists(MainWindow._datPath + "-bak"), "Rejected compile wrote backup files.");
        MainWindow.SprLists[2] = new MemoryStream(Png(2,2,Color.Red));
        Appearances expected = MainWindow.appearances.Clone();
        MainWindow.AfterValidation = () => MainWindow.appearances.Outfit[0].FrameGroup[0].SpriteInfo.PatternWidth = 9;
        editor.Compile_Click(null, null);
        Check(Appearances.Parser.ParseFrom(File.ReadAllBytes(MainWindow._datPath)).Equals(expected), "Compile serialized a changed live index.");
        MainWindow.appearances = expected.Clone(); MainWindow.AfterValidation = null;
        var captured = new Dictionary<uint, byte[]>();
        Check(editor.TryGetValidatedSnapshot(MainWindow.appearances, out var snapshot, capturedSprites: captured), ErrorManager.LastError);
        MainWindow.SprLists[2] = new MemoryStream(Png(0,0,Color.Red));
        Check(OutfitMaskValidator.TryValidate(snapshot.Outfit, id => OutfitMaskValidator.DecodeSprite(captured[id]), out string error), error);
        Check(!OutfitMaskValidator.TryValidate(snapshot.Outfit, ResolveValidationSprite, out error), "Fixture cache edit was not detected.");
        foreach (var size in new[] { new Size(32,32), new Size(64,64) }) {
            using var blank = OutfitMaskValidator.DecodeSprite(GetExportSpriteData(0, size).ToByteArray());
            Check(blank.Size == size, "Empty AEC sprite changed dimensions.");
            for (int y=0;y<blank.Height;y++) for(int x=0;x<blank.Width;x++) Check(blank.GetPixel(x,y).A==0, "Physical sprite zero leaked into AEC.");
        }
        Appearance legacy = Outfit(); var info = legacy.FrameGroup[0].SpriteInfo;
        info.BoundingBoxPerDirection.Add(new Box { Width=64, Height=64 });
        info.Animation = new SpriteAnimation(); info.Animation.SpritePhase.Add(new SpritePhase()); info.Animation.SpritePhase.Add(new SpritePhase());
        info.SpriteId.Clear(); info.SpriteId.Add(new uint[] { 77,0,0,0 });
        var offsets = new ConcurrentDictionary<string,uint>();
        for(int i=0;i<4;i++) { offsets[$"0_{i}"]=0; offsets[$"77_{i}"]=(uint)(100+i); }
        var types = new ConcurrentDictionary<uint,int>(); types[77]=3;
        editor.UpdateAppearanceObject(legacy, offsets, types);
        Check(info.SpriteId.SequenceEqual(new uint[] {103,102,101,100,0,0,0,0,0,0,0,0,0,0,0,0}), "Legacy conversion lost zero sprite semantics.");
        Check(info.PatternWidth==2 && info.PatternHeight==2 && info.PatternFrames==2, "Legacy layout changed incorrectly.");
        var group = Outfit().FrameGroup[0]; group.SpriteInfo.Animation = new SpriteAnimation();
        Check(GetSpriteIndex(group,1,0,0,0,0)==1, "Empty animation did not use one static phase.");
    }
}
}
""";
}
