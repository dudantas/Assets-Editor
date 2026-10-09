using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Assets_Editor;
using Google.Protobuf;
using Tibia.Protobuf.Appearances;

public static class OutfitMaskValidatorTests {
    public static void Run() {
        MatchingMasksAndPartiallyVisibleBasesAreAccepted();
        EveryPrimaryColorOutsideTheBaseIsRejected();
        NonPrimaryAndPartiallyTransparentMaskPixelsAreIgnored();
        OnePixelDiagonalEdgesAreAcceptedAndTwoPixelEdgesAreRejected();
        EveryAnimationAndPatternSlotIsChecked();
        AdditionalLayersDoNotBypassTheColorMaskCheck();
        EmptyMaskFramesAndSingleLayerGroupsDoNotRequireDecoding();
        ZeroBaseIdsRemainTransparent();
        RepeatedPairsAreDecodedOnceAndDisposed();
        InvalidModelsAndSpriteCountsAreRejected();
        MissingSpritesDecodeErrorsAndDifferentDimensionsAreRejected();
        PendingAlphaChecksTheResultWithoutChangingSourcePixels();
        PendingAlphaPreservesMagentaAndTransparentBlack();
        NegativeStrideBitmapsAreHandled();
        DecodeSpriteDetachesPngAndPreservesAllPixelChannels();
        CorruptSpriteBytesAreContainedByValidation();
        EmbeddedSpritesUseSlotOrderInsteadOfGlobalIds();
        EmbeddedValidationChecksEveryPayloadWithoutChangingTheOriginal();
        InvalidEmbeddedPayloadsAndCountsAreRejected();
    }

    private static void MatchingMasksAndPartiallyVisibleBasesAreAccepted() {
        using Bitmap body = PixelBitmap(Color.FromArgb(1, 70, 80, 90), 2, 2);
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        Assert(Validate(PairOutfit(), body, mask, out string error), error);
    }

    private static void EveryPrimaryColorOutsideTheBaseIsRejected() {
        using Bitmap body = TransparentBitmap();
        foreach (Color color in new[] { Color.Yellow, Color.Red, Color.Lime, Color.Blue }) {
            using Bitmap mask = PixelBitmap(color, 2, 2);
            Assert(!Validate(PairOutfit(), body, mask, out string error), "An unmatched primary color mask was accepted.");
            AssertContains(error, "Outfit 42", "base sprite 1", "mask sprite 2");
        }
    }

    private static void NonPrimaryAndPartiallyTransparentMaskPixelsAreIgnored() {
        using Bitmap body = TransparentBitmap();
        foreach (Color color in new[] {
            Color.FromArgb(254, 255, 0, 0), Color.FromArgb(0, 255, 255, 0),
            Color.FromArgb(255, 254, 0, 0), Color.White, Color.Black, Color.Magenta
        }) {
            using Bitmap mask = PixelBitmap(color, 2, 2);
            Assert(Validate(PairOutfit(), body, mask, out string error), error);
        }
    }

    private static void OnePixelDiagonalEdgesAreAcceptedAndTwoPixelEdgesAreRejected() {
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap adjacent = PixelBitmap(Color.Blue, 1, 1);
        Assert(Validate(PairOutfit(), body, adjacent, out string error), error);
        using Bitmap distant = PixelBitmap(Color.Blue, 0, 0);
        Assert(!Validate(PairOutfit(), body, distant, out error), "A mask two pixels outside the silhouette was accepted.");
        AssertContains(error, "(0, 0)");
        using Bitmap edgeBody = PixelBitmap(Color.Gray, 1, 1);
        using Bitmap edgeMask = PixelBitmap(Color.Yellow, 0, 0);
        Assert(Validate(PairOutfit(), edgeBody, edgeMask, out error), error);
    }

    private static void EveryAnimationAndPatternSlotIsChecked() {
        Appearance outfit = CreateOutfit(42, 2, 2, 2, 2, 2);
        SpriteInfo info = outfit.FrameGroup[0].SpriteInfo;
        info.SpriteId[info.SpriteId.Count - 1] = 3;
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap matching = PixelBitmap(Color.Red, 2, 2);
        using Bitmap outside = PixelBitmap(Color.Red, 0, 0);
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, id => id switch {
            1 => Clone(body), 2 => Clone(matching), 3 => Clone(outside), _ => throw new InvalidOperationException("Unknown fixture sprite.")
        }, out string error), "The last animated pattern slot was not checked.");
        AssertContains(error, "Outfit 42", "group 0", "phase 1", "direction 1", "addon 1", "mount 1", "mask sprite 3");
    }

    private static void AdditionalLayersDoNotBypassTheColorMaskCheck() {
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap matching = PixelBitmap(Color.Red, 2, 2);
        using Bitmap outside = PixelBitmap(Color.Red, 0, 0);
        foreach (uint layers in new uint[] { 3, 4 }) {
            Appearance outfit = CreateOutfit(42, 1, 1, 1, layers, 2);
            Bitmap Resolve(uint id) => id switch {
                1 => Clone(body), 2 => Clone(matching), 5 => Clone(outside),
                _ => throw new InvalidOperationException("Extra layers must not be interpreted as color masks.")
            };
            Assert(OutfitMaskValidator.TryValidate(new[] { outfit }, Resolve, out string error), error);
            outfit.FrameGroup[0].SpriteInfo.SpriteId[(int)layers + 1] = 5;
            Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, Resolve, out error),
                "Additional layers bypassed the color mask check in a later phase.");
            AssertContains(error, "Outfit 42", "phase 1", "base sprite 1", "mask sprite 5");
        }
    }

    private static void EmptyMaskFramesAndSingleLayerGroupsDoNotRequireDecoding() {
        Appearance singleLayer = CreateOutfit(42, 1, 1, 1, 1, 1);
        Assert(OutfitMaskValidator.TryValidate(new[] { singleLayer }, _ => throw new InvalidOperationException("Single-layer sprites must not be decoded."),
            out string error), error);

        Appearance outfit = CreateOutfit(42, 1, 1, 1, 2, 2);
        outfit.FrameGroup[0].SpriteInfo.SpriteId[3] = 0;
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        outfit.FrameGroup.Insert(0, singleLayer.FrameGroup[0].Clone());
        Assert(Validate(outfit, body, mask, out error), error);

        outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.SpriteId[1] = 0;
        Assert(OutfitMaskValidator.TryValidate(new[] { outfit }, _ => throw new InvalidOperationException("A blank mask requires no decoder."),
            out error), error);
    }

    private static void ZeroBaseIdsRemainTransparent() {
        Appearance outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.SpriteId[0] = 0;
        using Bitmap mask = PixelBitmap(Color.Lime, 2, 2);
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, id => {
            Assert(id == 2, "Sprite ID zero must never reach the decoder.");
            return Clone(mask);
        }, out string error), "A color mask over a logical blank base was accepted.");
        AssertContains(error, "base sprite 0", "mask sprite 2");
    }

    private static void RepeatedPairsAreDecodedOnceAndDisposed() {
        Appearance outfit = CreateOutfit(42, 4, 2, 2, 2, 3);
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        var decoded = new List<Bitmap>();
        var calls = new Dictionary<uint, int>();
        Assert(OutfitMaskValidator.TryValidate(new[] { outfit }, id => {
            calls[id] = calls.TryGetValue(id, out int count) ? count + 1 : 1;
            Bitmap bitmap = id == 1 ? Clone(body) : Clone(mask);
            decoded.Add(bitmap);
            return bitmap;
        }, out string error), error);
        Assert(calls.Count == 2 && calls[1] == 1 && calls[2] == 1, "A repeated sprite pair must be decoded only once per validation.");
        foreach (Bitmap bitmap in decoded) {
            AssertDisposed(bitmap);
        }

        decoded.Clear();
        using Bitmap unmatched = PixelBitmap(Color.Blue, 0, 0);
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, id => {
            Bitmap bitmap = id == 1 ? Clone(body) : Clone(unmatched);
            decoded.Add(bitmap);
            return bitmap;
        }, out error), "An unmatched mask was accepted.");
        foreach (Bitmap bitmap in decoded) {
            AssertDisposed(bitmap);
        }
    }

    private static void InvalidModelsAndSpriteCountsAreRejected() {
        Func<uint, Bitmap> unused = _ => throw new InvalidOperationException("Invalid layouts must not decode sprites.");
        Assert(!OutfitMaskValidator.TryValidate(null, unused, out string error) && error.Length > 0, "Missing outfits were accepted.");
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, null, out error) && error.Length > 0, "A missing decoder was accepted.");
        Assert(!OutfitMaskValidator.TryValidate(new Appearance[] { null }, unused, out error) && error.Length > 0, "A null outfit was accepted.");
        Assert(!OutfitMaskValidator.TryValidate(new[] { new Appearance { Id = 42 } }, unused, out error), "An outfit without frame groups was accepted.");
        AssertContains(error, "Outfit 42");

        Appearance outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo = null;
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, unused, out error), "Missing sprite information was accepted.");
        AssertContains(error, "Outfit 42", "group 0");
        outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.PatternDepth = 0;
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, unused, out error), "A zero dimension was accepted.");
        outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.SpriteId.RemoveAt(1);
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, unused, out error), "A truncated sprite list was accepted.");
        outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.SpriteId.Add(2);
        Assert(!OutfitMaskValidator.TryValidate(new[] { outfit }, unused, out error), "An oversized sprite list was accepted.");
    }

    private static void MissingSpritesDecodeErrorsAndDifferentDimensionsAreRejected() {
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, _ => null, out string error), "A missing mask was accepted.");
        AssertContains(error, "Outfit 42", "mask sprite 2");
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, id => id == 2 ? Clone(mask) : null, out error), "A missing base was accepted.");
        AssertContains(error, "Outfit 42", "base sprite 1");
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, _ => throw new InvalidDataException("Invalid fixture image."), out error),
            "A decoding exception escaped validation.");
        AssertContains(error, "Outfit 42", "mask sprite 2");
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2, 4, 4);
        Assert(!Validate(PairOutfit(), body, mask, out error), "Different base and mask dimensions were accepted.");
    }

    private static void PendingAlphaChecksTheResultWithoutChangingSourcePixels() {
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        var transparentBody = new Dictionary<uint, byte> { [1] = 0 };
        Assert(!Validate(PairOutfit(), body, mask, out string error, transparentBody), "A pending edit that removes the base silhouette was accepted.");
        AssertContains(error, "Outfit 42", "base sprite 1");
        Assert(body.GetPixel(2, 2).A == 255 && mask.GetPixel(2, 2).A == 255, "Validation changed the original bitmap pixels.");

        using Bitmap outside = PixelBitmap(Color.Red, 0, 0);
        Assert(Validate(PairOutfit(), body, outside, out error, new Dictionary<uint, byte> { [2] = 127 }), error);
        Assert(outside.GetPixel(0, 0).A == 255, "Pending mask alpha changed the source image.");
        using Bitmap partialMask = PixelBitmap(Color.FromArgb(127, 255, 0, 0), 0, 0);
        Assert(!Validate(PairOutfit(), body, partialMask, out error, new Dictionary<uint, byte> { [2] = 255 }),
            "A pending edit that creates an opaque unmatched mask was accepted.");

        using Bitmap transparentColoredBody = PixelBitmap(Color.FromArgb(0, 70, 80, 90), 2, 2);
        Assert(Validate(PairOutfit(), transparentColoredBody, mask, out error, new Dictionary<uint, byte> { [1] = 255 }), error);
        Assert(transparentColoredBody.GetPixel(2, 2).A == 0, "Pending body alpha changed the source image.");
    }

    private static void PendingAlphaPreservesMagentaAndTransparentBlack() {
        using Bitmap mask = PixelBitmap(Color.Red, 2, 2);
        using Bitmap transparentBlack = TransparentBitmap();
        Assert(!Validate(PairOutfit(), transparentBlack, mask, out string error, new Dictionary<uint, byte> { [1] = 255 }),
            "Pending alpha must not make transparent black into a base silhouette.");
        using Bitmap transparentMagenta = PixelBitmap(Color.FromArgb(0, 255, 0, 255), 2, 2);
        Assert(!Validate(PairOutfit(), transparentMagenta, mask, out error, new Dictionary<uint, byte> { [1] = 255 }),
            "Pending alpha must preserve transparent magenta.");
        using Bitmap opaqueMagenta = PixelBitmap(Color.Magenta, 2, 2);
        Assert(Validate(PairOutfit(), opaqueMagenta, mask, out error, new Dictionary<uint, byte> { [1] = 0 }), error);
        using Bitmap opaqueBlack = PixelBitmap(Color.Black, 2, 2);
        Assert(!Validate(PairOutfit(), opaqueBlack, mask, out error, new Dictionary<uint, byte> { [1] = 0 }),
            "Pending alpha must still change opaque black.");
    }

    private static void NegativeStrideBitmapsAreHandled() {
        const int width = 5;
        const int height = 5;
        const int stride = width * 4;
        IntPtr buffer = Marshal.AllocHGlobal(stride * height);
        try {
            Marshal.Copy(new byte[stride * height], 0, buffer, stride * height);
            using Bitmap body = new(width, height, -stride, PixelFormat.Format32bppArgb, IntPtr.Add(buffer, stride * (height - 1)));
            body.SetPixel(2, 0, Color.Gray);
            using Bitmap mask = PixelBitmap(Color.Red, 2, 0);
            Assert(OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, id => id == 1 ? body : Clone(mask), out string error), error);
        } finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void DecodeSpriteDetachesPngAndPreservesAllPixelChannels() {
        Color[] expected = {
            Color.Yellow, Color.Red, Color.Lime, Color.Blue,
            Color.FromArgb(127, 17, 34, 51), Color.FromArgb(0, 70, 80, 90),
            Color.FromArgb(0, 255, 0, 255), Color.FromArgb(0, 0, 0, 0)
        };
        byte[] png;
        using (Bitmap source = TransparentBitmap(4, 2)) {
            for (int i = 0; i < expected.Length; i++) {
                source.SetPixel(i % 4, i / 4, expected[i]);
            }
            using var stream = new MemoryStream();
            source.Save(stream, ImageFormat.Png);
            png = stream.ToArray();
        }

        using Bitmap decoded = OutfitMaskValidator.DecodeSprite(png);
        Array.Clear(png, 0, png.Length);
        Assert(decoded.Width == 4 && decoded.Height == 2, "Decoded sprite dimensions changed.");
        for (int i = 0; i < expected.Length; i++) {
            Assert(decoded.GetPixel(i % 4, i / 4).ToArgb() == expected[i].ToArgb(),
                $"Detached PNG changed RGB or alpha channels at pixel {i}.");
        }
        using var saved = new MemoryStream();
        decoded.Save(saved, ImageFormat.Png);
        Assert(saved.Length > 0, "The detached bitmap must remain usable after its decoding stream is disposed.");
    }

    private static void CorruptSpriteBytesAreContainedByValidation() {
        byte[] corrupt = { 1, 2, 3, 4 };
        bool rejected = false;
        try {
            using Bitmap decoded = OutfitMaskValidator.DecodeSprite(corrupt);
        } catch (Exception) {
            rejected = true;
        }
        Assert(rejected, "Corrupt PNG bytes must fail at the local decoding boundary.");
        Assert(!OutfitMaskValidator.TryValidate(new[] { PairOutfit() }, _ => OutfitMaskValidator.DecodeSprite(corrupt), out string error),
            "A corrupt sprite decoding exception escaped validation.");
        AssertContains(error, "Outfit 42", "mask sprite 2");
    }

    private static void EmbeddedSpritesUseSlotOrderInsteadOfGlobalIds() {
        Appearance outfit = PairOutfit();
        outfit.FrameGroup[0].SpriteInfo.SpriteId[0] = 0;
        outfit.FrameGroup[0].SpriteInfo.SpriteId[1] = 900;
        FrameGroup second = outfit.FrameGroup[0].Clone();
        second.SpriteInfo.SpriteId[0] = 200;
        second.SpriteInfo.SpriteId[1] = 0;
        outfit.FrameGroup.Add(second);
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap mask = PixelBitmap(Color.Blue, 2, 2);
        AddEmbedded(outfit, body, mask, body, mask);
        Appearance original = outfit.Clone();
        Assert(OutfitMaskValidator.TryValidateEmbedded(new[] { outfit }, out string error), error);
        Assert(original.Equals(outfit), "Embedded validation changed global IDs or payloads.");
    }

    private static void EmbeddedValidationChecksEveryPayloadWithoutChangingTheOriginal() {
        Appearance outfit = CreateOutfit(42, 1, 1, 1, 2, 2);
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap matching = PixelBitmap(Color.Red, 2, 2);
        using Bitmap outside = PixelBitmap(Color.Red, 0, 0);
        AddEmbedded(outfit, body, matching, body, outside);
        Appearance original = outfit.Clone();
        Assert(!OutfitMaskValidator.TryValidateEmbedded(new[] { outfit }, out string error), "An unmatched mask in a later embedded frame was accepted.");
        AssertContains(error, "Outfit 42", "phase 1");
        Assert(original.Equals(outfit), "Rejected embedded validation changed the original appearance.");
    }

    private static void InvalidEmbeddedPayloadsAndCountsAreRejected() {
        Assert(!OutfitMaskValidator.TryValidateEmbedded(null, out string error) && error.Length > 0, "Missing embedded outfits were accepted.");
        Assert(!OutfitMaskValidator.TryValidateEmbedded(new Appearance[] { null }, out error) && error.Length > 0, "A null embedded outfit was accepted.");
        Appearance outfit = PairOutfit();
        using Bitmap body = PixelBitmap(Color.Gray, 2, 2);
        using Bitmap mask = PixelBitmap(Color.Blue, 2, 2);
        AddEmbedded(outfit, body);
        Assert(!OutfitMaskValidator.TryValidateEmbedded(new[] { outfit }, out error), "Missing embedded payloads were accepted.");
        AssertContains(error, "Outfit 42");
        AddEmbedded(outfit, mask, mask);
        Assert(!OutfitMaskValidator.TryValidateEmbedded(new[] { outfit }, out error), "Extra embedded payloads were accepted.");
        outfit.SpriteData.RemoveAt(2);
        outfit.SpriteData[1] = ByteString.CopyFrom(new byte[] { 1, 2, 3, 4 });
        Assert(!OutfitMaskValidator.TryValidateEmbedded(new[] { outfit }, out error) && error.Length > 0, "Malformed embedded image bytes were accepted.");
    }

    private static bool Validate(Appearance outfit, Bitmap body, Bitmap mask, out string error,
        IReadOnlyDictionary<uint, byte> pendingAlpha = null) {
        return OutfitMaskValidator.TryValidate(new[] { outfit }, id => id switch {
            1 => Clone(body), 2 => Clone(mask), _ => throw new InvalidOperationException("Unknown fixture sprite.")
        }, out error, pendingAlpha);
    }

    private static Appearance PairOutfit() => CreateOutfit(42, 1, 1, 1, 2, 1);

    private static Appearance CreateOutfit(uint id, uint width, uint height, uint depth, uint layers, int phases) {
        Appearance outfit = new() { Id = id };
        SpriteInfo info = new() { PatternWidth = width, PatternHeight = height, PatternDepth = depth, Layers = layers };
        if (phases > 1) {
            info.Animation = new SpriteAnimation();
            for (int phase = 0; phase < phases; phase++) {
                info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
            }
        }
        int slots = (int)(width * height * depth) * phases;
        for (int slot = 0; slot < slots; slot++) {
            for (uint layer = 0; layer < layers; layer++) {
                info.SpriteId.Add(layer + 1);
            }
        }
        outfit.FrameGroup.Add(new FrameGroup { SpriteInfo = info });
        return outfit;
    }

    private static Bitmap TransparentBitmap(int width = 5, int height = 5) => new(width, height, PixelFormat.Format32bppArgb);

    private static Bitmap PixelBitmap(Color color, int x, int y, int width = 5, int height = 5) {
        Bitmap bitmap = TransparentBitmap(width, height);
        bitmap.SetPixel(x, y, color);
        return bitmap;
    }

    private static Bitmap Clone(Bitmap bitmap) => bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);

    private static void AddEmbedded(Appearance outfit, params Bitmap[] sprites) {
        foreach (Bitmap sprite in sprites) {
            using var stream = new MemoryStream();
            sprite.Save(stream, ImageFormat.Png);
            outfit.SpriteData.Add(ByteString.CopyFrom(stream.ToArray()));
        }
    }

    private static void AssertDisposed(Bitmap bitmap) {
        try {
            bitmap.GetPixel(0, 0);
        } catch (ArgumentException) {
            return;
        }
        throw new InvalidOperationException("Decoded sprites must be disposed after validation.");
    }

    private static void AssertContains(string actual, params string[] expected) {
        foreach (string text in expected) {
            Assert(actual.Contains(text, StringComparison.Ordinal), $"Validation error must contain '{text}', but was '{actual}'.");
        }
    }

    private static void Assert(bool condition, string message) {
        if (!condition) {
            throw new InvalidOperationException(message);
        }
    }
}
