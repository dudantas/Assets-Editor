using System;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor;

public static class AppearanceSpriteLayout {
    public static bool TryGetSpriteCount(SpriteInfo info, uint layers, out int count, out string error) {
        count = 0;
        error = string.Empty;
        if (info == null) {
            error = "Sprite information is missing.";
            return false;
        }

        if (info.PatternWidth == 0 || info.PatternHeight == 0 || info.PatternDepth == 0 || layers == 0) {
            error = "Sprite dimensions and layer count must be greater than zero.";
            return false;
        }

        int phases = Math.Max(1, info.Animation?.SpritePhase.Count ?? 1);

        int total = phases;
        if (!TryMultiply(ref total, info.PatternWidth) ||
            !TryMultiply(ref total, info.PatternHeight) ||
            !TryMultiply(ref total, info.PatternDepth) ||
            !TryMultiply(ref total, layers)) {
            error = "Sprite count exceeds the supported collection size.";
            return false;
        }

        count = total;
        return true;
    }

    public static bool TrySetLayers(SpriteInfo info, uint newLayers, uint blankSpriteId, out string error) {
        if (!TryGetSpriteCount(info, info?.Layers ?? 0, out int oldCount, out error) ||
            !TryGetSpriteCount(info, newLayers, out int newCount, out error)) {
            return false;
        }

        if (info.SpriteId.Count != oldCount) {
            error = $"Sprite list contains {info.SpriteId.Count} entries; the current layout requires {oldCount}.";
            return false;
        }

        if (info.Layers == newLayers) {
            return true;
        }

        int oldLayers = (int)info.Layers;
        int targetLayers = (int)newLayers;
        int retainedLayers = Math.Min(oldLayers, targetLayers);
        uint[] remapped = new uint[newCount];
        if (blankSpriteId != 0) {
            Array.Fill(remapped, blankSpriteId);
        }

        // Layers are interleaved inside each direction/addon/mount/animation slot.
        for (int oldOffset = 0, newOffset = 0; oldOffset < oldCount; oldOffset += oldLayers, newOffset += targetLayers) {
            for (int layer = 0; layer < retainedLayers; layer++) {
                remapped[newOffset + layer] = info.SpriteId[oldOffset + layer];
            }
        }

        // Allocate before clearing so a rejected edit never loses existing sprite IDs.
        if (info.SpriteId.Capacity < newCount) {
            info.SpriteId.Capacity = newCount;
        }
        info.SpriteId.Clear();
        info.SpriteId.Add(remapped);
        info.Layers = newLayers;
        return true;
    }

    private static bool TryMultiply(ref int count, uint factor) {
        if (factor > (uint)(Array.MaxLength / count)) {
            return false;
        }

        count *= (int)factor;
        return true;
    }
}
