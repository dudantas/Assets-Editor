using System;
using System.Linq;
using Assets_Editor;
using Tibia.Protobuf.Appearances;

public static class SpriteLayerLayoutTests {
    public static void Run() {
        RemovingLayersPreservesEveryPatternAndAnimationSlot();
        AddingLayersInsertsBlankInsideEverySlot();
        LayerChangesPreserveTheRetainedPrefix();
        StaticLayoutsAndZeroBlankIdsAreSupported();
        EmptyAnimationsUseOneStaticPhaseWithoutChangingMetadata();
        UnchangedLayersPreserveTheEntireMessage();
        InvalidLayoutsAreRejectedWithoutMutation();
        SpriteCountRejectsOverflowWithoutAllocation();
    }

    private static void RemovingLayersPreservesEveryPatternAndAnimationSlot() {
        SpriteInfo info = CreateLayout(4, 3, 2, 2, 5);
        SpriteInfo original = info.Clone();
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 1, 999, out string error), error);
        Assert(info.SpriteId.Count == 120, "Every direction, addon, mount and phase must remain present.");
        for (int slot = 0; slot < 120; slot++) {
            Assert(info.SpriteId[slot] == original.SpriteId[slot * 2], $"Base sprite changed at slot {slot}.");
        }

        original.Layers = info.Layers;
        original.SpriteId.Clear();
        original.SpriteId.Add(info.SpriteId);
        Assert(original.Equals(info), "Removing layers changed unrelated sprite or animation fields.");
    }

    private static void AddingLayersInsertsBlankInsideEverySlot() {
        SpriteInfo info = CreateLayout(4, 2, 2, 1, 3);
        uint[] originalIds = info.SpriteId.ToArray();
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 2, 777, out string error), error);
        Assert(info.SpriteId.Count == originalIds.Length * 2, "Adding a layer must retain every pattern slot.");
        for (int slot = 0; slot < originalIds.Length; slot++) {
            Assert(info.SpriteId[slot * 2] == originalIds[slot], $"Base sprite changed at slot {slot}.");
            Assert(info.SpriteId[slot * 2 + 1] == 777, $"New layer is not blank at slot {slot}.");
        }
    }

    private static void LayerChangesPreserveTheRetainedPrefix() {
        SpriteInfo info = CreateLayout(2, 2, 1, 3, 2);
        uint[] originalIds = info.SpriteId.ToArray();
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 2, 0, out string error), error);
        for (int slot = 0; slot < 8; slot++) {
            Assert(info.SpriteId[slot * 2] == originalIds[slot * 3], "First retained layer changed.");
            Assert(info.SpriteId[slot * 2 + 1] == originalIds[slot * 3 + 1], "Second retained layer changed.");
        }

        uint[] retainedIds = info.SpriteId.ToArray();
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 4, 555, out error), error);
        for (int slot = 0; slot < 8; slot++) {
            Assert(info.SpriteId[slot * 4] == retainedIds[slot * 2], "First expanded layer changed.");
            Assert(info.SpriteId[slot * 4 + 1] == retainedIds[slot * 2 + 1], "Second expanded layer changed.");
            Assert(info.SpriteId[slot * 4 + 2] == 555 && info.SpriteId[slot * 4 + 3] == 555,
                "Expanded layers must contain the requested blank sprite.");
        }
    }

    private static void StaticLayoutsAndZeroBlankIdsAreSupported() {
        SpriteInfo info = CreateLayout(2, 1, 1, 1, 1);
        info.Animation = null;
        Assert(AppearanceSpriteLayout.TryGetSpriteCount(info, 1, out int count, out string error), error);
        Assert(count == 2, "A layout without animation must use exactly one phase.");
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 2, 0, out error), error);
        Assert(info.SpriteId.SequenceEqual(new uint[] { 100, 0, 101, 0 }), "Zero blank IDs must be inserted per slot.");
        Assert(info.Animation == null, "Adding layers must not create an animation.");
    }

    private static void EmptyAnimationsUseOneStaticPhaseWithoutChangingMetadata() {
        SpriteInfo info = CreateLayout(2, 1, 1, 1, 1);
        info.Animation.SpritePhase.Clear();
        SpriteInfo original = info.Clone();
        SpriteAnimation animation = info.Animation;
        Assert(AppearanceSpriteLayout.TryGetSpriteCount(info, 1, out int count, out string error), error);
        Assert(count == 2, "An empty animation must use one static phase.");
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 1, 777, out error), error);
        Assert(original.Equals(info) && ReferenceEquals(animation, info.Animation), "An empty animation no-op changed metadata.");
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 2, 777, out error), error);
        Assert(info.SpriteId.SequenceEqual(new uint[] { 100, 777, 101, 777 }), "An empty animation must expand each static slot.");
        Assert(ReferenceEquals(animation, info.Animation) && info.Animation.SpritePhase.Count == 0,
            "Expanding layers must preserve the existing empty animation.");
        original.Layers = 2;
        original.SpriteId.Clear();
        original.SpriteId.Add(info.SpriteId);
        Assert(original.Equals(info), "Expanding an empty animation changed unrelated protobuf fields.");
    }

    private static void UnchangedLayersPreserveTheEntireMessage() {
        SpriteInfo info = CreateLayout(4, 1, 1, 2, 2);
        SpriteInfo original = info.Clone();
        SpriteAnimation animation = info.Animation;
        int capacity = info.SpriteId.Capacity;
        Assert(AppearanceSpriteLayout.TrySetLayers(info, 2, 555, out string error), error);
        Assert(original.Equals(info), "An unchanged layer count must preserve the entire message.");
        Assert(ReferenceEquals(animation, info.Animation), "A no-op must preserve the animation object.");
        Assert(info.SpriteId.Capacity == capacity, "A no-op must not resize the sprite collection.");
    }

    private static void InvalidLayoutsAreRejectedWithoutMutation() {
        Assert(!AppearanceSpriteLayout.TrySetLayers(null, 1, 0, out string error) && error.Length > 0,
            "Missing sprite information must be rejected with an explanation.");

        SpriteInfo invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.PatternWidth = 0;
        AssertRejectedWithoutMutation(invalid, 2);
        invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.PatternHeight = 0;
        AssertRejectedWithoutMutation(invalid, 2);
        invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.PatternDepth = 0;
        AssertRejectedWithoutMutation(invalid, 2);
        invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.Layers = 0;
        AssertRejectedWithoutMutation(invalid, 2);

        invalid = CreateLayout(1, 1, 1, 1, 1);
        AssertRejectedWithoutMutation(invalid, 0);
        invalid = CreateLayout(2, 1, 1, 2, 2);
        invalid.SpriteId.RemoveAt(0);
        AssertRejectedWithoutMutation(invalid, 1);
        AssertRejectedWithoutMutation(invalid, 2);
        invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.SpriteId.Add(999);
        AssertRejectedWithoutMutation(invalid, 2);
        invalid = CreateLayout(1, 1, 1, 1, 1);
        invalid.SpriteId.Clear();
        AssertRejectedWithoutMutation(invalid, 2);
    }

    private static void SpriteCountRejectsOverflowWithoutAllocation() {
        SpriteInfo info = CreateLayout(1, 1, 1, 1, 1);
        AssertRejectedWithoutMutation(info, uint.MaxValue);
        info.PatternWidth = uint.MaxValue;
        AssertRejectedWithoutMutation(info, 1);
        Assert(!AppearanceSpriteLayout.TryGetSpriteCount(info, 1, out int count, out string error) && count == 0,
            "An overflowing dimension must be rejected without returning a partial count.");
        Assert(error.Length > 0, "An overflowing dimension must provide an explanation.");
        info.PatternWidth = 65536;
        info.PatternHeight = 65536;
        AssertRejectedWithoutMutation(info, 1);
        info.PatternWidth = (uint)Array.MaxLength;
        info.PatternHeight = 1;
        Assert(AppearanceSpriteLayout.TryGetSpriteCount(info, 1, out count, out error), error);
        Assert(count == Array.MaxLength, "The collection limit must be computed without allocating sprites.");
        Assert(!AppearanceSpriteLayout.TryGetSpriteCount(info, 2, out count, out error) && count == 0,
            "Multiplication across the collection limit must be rejected.");
    }

    private static SpriteInfo CreateLayout(uint width, uint height, uint depth, uint layers, int phases) {
        SpriteInfo info = new() {
            PatternWidth = width,
            PatternHeight = height,
            PatternDepth = depth,
            Layers = layers,
            BoundingSquare = 64,
            IsOpaque = false,
            PatternSize = 32,
            PatternLayers = 7,
            PatternX = 8,
            PatternY = 9,
            PatternZ = 10,
            PatternFrames = 11,
            IsAnimation = true,
            Animation = new SpriteAnimation {
                DefaultStartPhase = 0,
                Synchronized = true,
                RandomStartPhase = true,
                LoopCount = 3
            }
        };
        info.BoundingBoxPerDirection.Add(new Box { X = 2, Y = 3, Width = 17, Height = 19 });
        for (int phase = 0; phase < phases; phase++) {
            info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = (uint)(100 + phase), DurationMax = (uint)(200 + phase) });
        }
        int count = (int)(width * height * depth * layers) * phases;
        for (int index = 0; index < count; index++) {
            info.SpriteId.Add((uint)(100 + index));
        }
        return info;
    }

    private static void AssertRejectedWithoutMutation(SpriteInfo info, uint newLayers) {
        SpriteInfo original = info.Clone();
        Assert(!AppearanceSpriteLayout.TrySetLayers(info, newLayers, 999, out string error), "Invalid sprite layout was accepted.");
        Assert(error.Length > 0, "Rejected edits must provide an explanation.");
        Assert(original.Equals(info), "Rejected edits must preserve the entire sprite message.");
    }

    private static void Assert(bool condition, string message) {
        if (!condition) {
            throw new InvalidOperationException(message);
        }
    }
}
