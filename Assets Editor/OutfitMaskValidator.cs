using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor;

public static class OutfitMaskValidator {
    // The resolver transfers ownership of each decoded bitmap to this operation.
    public static bool TryValidate(IEnumerable<Appearance> outfits, Func<uint, Bitmap> resolveSprite,
        out string error, IReadOnlyDictionary<uint, byte> pendingAlpha = null) {
        error = string.Empty;
        string context = "Outfit validation";
        try {
            if (outfits == null || resolveSprite == null) {
                error = "Outfits and a sprite resolver are required.";
                return false;
            }

            var checkedPairs = new Dictionary<(uint, uint), string>();
            foreach (Appearance outfit in outfits) {
                context = $"Outfit {outfit?.Id}";
                if (outfit == null || outfit.FrameGroup.Count == 0) {
                    error = $"{context}: frame groups are missing.";
                    return false;
                }

                for (int groupIndex = 0; groupIndex < outfit.FrameGroup.Count; groupIndex++) {
                    FrameGroup group = outfit.FrameGroup[groupIndex];
                    SpriteInfo info = group.SpriteInfo;
                    context = $"Outfit {outfit.Id}, group {groupIndex} ({group.FixedFrameGroup})";
                    if (!AppearanceSpriteLayout.TryGetSpriteCount(info, info?.Layers ?? 0, out int count, out string layoutError)) {
                        error = $"{context}: {layoutError}";
                        return false;
                    }
                    if (count != info.SpriteId.Count) {
                        error = $"{context}: layout requires {count} sprites, but contains {info.SpriteId.Count}.";
                        return false;
                    }
                    if (info.Layers < 2) {
                        continue;
                    }

                    int layers = (int)info.Layers;
                    for (int offset = 0; offset < count; offset += layers) {
                        uint baseId = info.SpriteId[offset];
                        uint maskId = info.SpriteId[offset + 1];
                        if (maskId == 0) {
                            continue;
                        }
                        int slot = offset / layers;
                        int direction = slot % (int)info.PatternWidth;
                        slot /= (int)info.PatternWidth;
                        int addon = slot % (int)info.PatternHeight;
                        slot /= (int)info.PatternHeight;
                        int mount = slot % (int)info.PatternDepth;
                        int phase = slot / (int)info.PatternDepth;
                        context = $"Outfit {outfit.Id}, group {groupIndex} ({group.FixedFrameGroup}), phase {phase}, " +
                            $"direction {direction}, addon {addon}, mount {mount}, base sprite {baseId}, mask sprite {maskId}";

                        if (!checkedPairs.TryGetValue((baseId, maskId), out string pairError)) {
                            pairError = CheckPair(baseId, maskId, resolveSprite, pendingAlpha);
                            checkedPairs.Add((baseId, maskId), pairError);
                        }
                        if (pairError.Length > 0) {
                            error = $"{context}: {pairError}";
                            return false;
                        }
                    }
                }
            }
            return true;
        } catch (Exception exception) {
            error = $"{context}: could not validate sprites ({exception.Message}).";
            return false;
        }
    }

    public static bool TryValidateEmbedded(IEnumerable<Appearance> outfits, out string error) {
        error = string.Empty;
        try {
            foreach (Appearance outfit in outfits) {
                Appearance copy = outfit.Clone();
                int spriteIndex = 0;
                foreach (FrameGroup group in copy.FrameGroup) {
                    if (group.SpriteInfo == null) {
                        error = $"Outfit {copy.Id}: sprite information is missing.";
                        return false;
                    }
                    for (int i = 0; i < group.SpriteInfo.SpriteId.Count; i++) {
                        // AEC IDs start at zero and may be offset by other objects. Payloads are per slot.
                        group.SpriteInfo.SpriteId[i] = (uint)++spriteIndex;
                    }
                }
                if (spriteIndex != copy.SpriteData.Count) {
                    error = $"Outfit {copy.Id}: embedded sprite count does not match the layout.";
                    return false;
                }
                Bitmap Resolve(uint id) {
                    return DecodeSprite(copy.SpriteData[(int)id - 1].ToByteArray());
                }
                if (!TryValidate(new[] { copy }, Resolve, out error)) {
                    return false;
                }
            }
            return true;
        } catch (Exception exception) {
            error = $"Could not validate embedded outfit sprites ({exception.Message}).";
            return false;
        }
    }

    public static Bitmap DecodeSprite(byte[] bytes) {
        using var stream = new MemoryStream(bytes);
        using var decoded = new Bitmap(stream);
        byte[] pixels = ReadPixels(decoded, 0, null);
        var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
        try {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try {
                int rowBytes = checked(bitmap.Width * 4);
                for (int y = 0; y < bitmap.Height; y++) {
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(data.Scan0, checked(y * data.Stride)), rowBytes);
                }
            } finally {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        } catch {
            bitmap.Dispose();
            throw;
        }
    }

    private static string CheckPair(uint baseId, uint maskId, Func<uint, Bitmap> resolveSprite,
        IReadOnlyDictionary<uint, byte> pendingAlpha) {
        using Bitmap mask = resolveSprite(maskId);
        if (mask == null) {
            return "Mask sprite is missing.";
        }
        using Bitmap baseSprite = baseId == 0 ? null : resolveSprite(baseId);
        if (baseId != 0 && baseSprite == null) {
            return "Base sprite is missing.";
        }
        if (baseSprite != null && baseSprite.Size != mask.Size) {
            return "Base and color mask dimensions differ.";
        }

        byte[] maskPixels = ReadPixels(mask, maskId, pendingAlpha);
        byte[] basePixels = baseSprite == null ? null : ReadPixels(baseSprite, baseId, pendingAlpha);
        int width = mask.Width;
        int height = mask.Height;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int index = (y * width + x) * 4;
                if (!IsColorMaskPixel(maskPixels, index)) {
                    continue;
                }
                bool covered = false;
                // Official sprites can have a one-pixel mask edge beyond the base silhouette.
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1) && !covered; ny++) {
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++) {
                        if (basePixels != null && basePixels[(ny * width + nx) * 4 + 3] != 0) {
                            covered = true;
                            break;
                        }
                    }
                }
                if (!covered) {
                    return $"Color mask extends outside the base silhouette at ({x}, {y}). " +
                        "Use a matching mask for this frame, or remove the color layer if the outfit is already colored.";
                }
            }
        }
        return string.Empty;
    }

    private static bool IsColorMaskPixel(byte[] pixels, int index) {
        if (pixels[index + 3] != 255) {
            return false;
        }
        byte blue = pixels[index];
        byte green = pixels[index + 1];
        byte red = pixels[index + 2];
        return (red == 255 && green == 255 && blue == 0) ||
            (red == 255 && green == 0 && blue == 0) ||
            (red == 0 && green == 255 && blue == 0) ||
            (red == 0 && green == 0 && blue == 255);
    }

    private static byte[] ReadPixels(Bitmap bitmap, uint id, IReadOnlyDictionary<uint, byte> pendingAlpha) {
        using Bitmap normalized = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        BitmapData data = normalized.LockBits(new Rectangle(0, 0, normalized.Width, normalized.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        try {
            int rowBytes = checked(normalized.Width * 4);
            pixels = new byte[checked(rowBytes * normalized.Height)];
            for (int y = 0; y < normalized.Height; y++) {
                Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), pixels, y * rowBytes, rowBytes);
            }
        } finally {
            normalized.UnlockBits(data);
        }

        if (pendingAlpha != null && pendingAlpha.TryGetValue(id, out byte alpha)) {
            // Match ChangeAlphaInRegion without mutating the sprite cache or source files.
            for (int i = 0; i < pixels.Length; i += 4) {
                if (!(pixels[i + 2] == 255 && pixels[i + 1] == 0 && pixels[i] == 255) &&
                    !(pixels[i + 2] == 0 && pixels[i + 1] == 0 && pixels[i] == 0 && pixels[i + 3] == 0)) {
                    pixels[i + 3] = alpha;
                }
            }
        }
        return pixels;
    }
}
