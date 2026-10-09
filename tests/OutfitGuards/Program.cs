using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using Assets_Editor;
using Tibia.Protobuf.Appearances;

internal static class Program {
    private static int Main(string[] args) {
        try {
            SpriteLayerLayoutTests.Run();
            Console.WriteLine("PASS sprite layer layout scenarios");
            OutfitMaskValidatorTests.Run();
            Console.WriteLine("PASS outfit mask validation scenarios");
            if (args.Length == 5 && args[0] == "--check-ui") {
                UiCompilationCheck.Run(args[1], args[2], args[3], args[4]);
                BoundaryGuardTests.Run(args[1], args[2], args[3], Path.Combine(AppContext.BaseDirectory, "boundary-fixtures"));
            } else if (args.Length == 2) {
                VerifySpritePairs(args[0], args[1]);
            } else if (args.Length != 0) {
                throw new ArgumentException("Optional arguments: pair-manifest.json sprite-png-directory; or --check-ui editor-directory core-reference-directory desktop-reference-directory baseline-ui-source");
            }
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    // Optional private diagnostic fixtures stay outside the source repository.
    private static void VerifySpritePairs(string manifestPath, string spriteDirectory) {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        int pairs = 0;
        foreach (JsonElement pair in manifest.RootElement.EnumerateArray()) {
            var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 2 };
            info.SpriteId.Add(pair.GetProperty("base").GetUInt32());
            info.SpriteId.Add(pair.GetProperty("mask").GetUInt32());
            var outfit = new Appearance { Id = pair.GetProperty("outfit").GetUInt32() };
            outfit.FrameGroup.Add(new FrameGroup { SpriteInfo = info });
            Bitmap Resolve(uint id) => new Bitmap(Path.Combine(spriteDirectory, $"{id}.png"));
            bool actual = OutfitMaskValidator.TryValidate(new[] { outfit }, Resolve, out string error);
            bool expected = pair.GetProperty("valid").GetBoolean();
            if (actual != expected) {
                throw new InvalidOperationException($"Pair {pairs}: expected valid={expected}, got {actual}: {error}");
            }
            pairs++;
        }
        Console.WriteLine($"PASS {pairs} external sprite pairs");
    }
}
