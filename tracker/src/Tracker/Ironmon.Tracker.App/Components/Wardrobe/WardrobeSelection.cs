using System.Globalization;
using Ironmon.Tracker.Protocol.Wardrobe;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Selects a cosmetic or restores its worn slot when a selection is toggled off.
/// </summary>
internal static class WardrobeSelection
{
    /// <summary>
    /// Builds a draft while retaining edits in unrelated slots.
    /// </summary>
    /// <param name="draft">The current preview appearance.</param>
    /// <param name="worn">The equipped appearance acknowledged by the game.</param>
    /// <param name="item">The clicked catalogue item.</param>
    /// <param name="slot">The accessory slot currently being edited.</param>
    /// <returns>The selected appearance, or the worn item and dye for the toggled slot.</returns>
    internal static WardrobeAppearancePayload Select(WardrobeAppearancePayload draft, WardrobeAppearancePayload worn, WardrobeCatalogItemPayload item, string slot)
    {
        return item.Category switch
        {
            WardrobeProtocol.Clothes when item.Id == draft.Clothes || item.Id == worn.Clothes => draft with { Clothes = worn.Clothes, ClothesColor = worn.ClothesColor },
            WardrobeProtocol.Clothes => draft with { Clothes = item.Id },
            WardrobeProtocol.Hair when item.Id == HairStyle(draft.Hair) || item.Id == HairStyle(worn.Hair) => draft with { Hair = worn.Hair, HairColor = worn.HairColor },
            WardrobeProtocol.Hair => draft with { Hair = HairVariant(draft.Hair, item) },
            WardrobeProtocol.Hat when slot == WardrobeProtocol.Hat && (item.Id == draft.Hat || item.Id == worn.Hat) => draft with { Hat = worn.Hat, HatColor = worn.HatColor },
            WardrobeProtocol.Hat when slot == WardrobeProtocol.Hat => draft with { Hat = item.Id },
            WardrobeProtocol.Hat when slot == WardrobeProtocol.Hat2 && (item.Id == draft.Hat2 || item.Id == worn.Hat2) => draft with { Hat2 = worn.Hat2, Hat2Color = worn.Hat2Color },
            WardrobeProtocol.Hat when slot == WardrobeProtocol.Hat2 => draft with { Hat2 = item.Id },
            _ => throw new ArgumentException("Invalid wardrobe item category or accessory slot.", nameof(item))
        };
    }

    /// <summary>
    /// Reads a native hairstyle ID without its base-color prefix.
    /// </summary>
    /// <param name="hair">The native version-and-style value.</param>
    /// <returns>The style ID.</returns>
    private static string HairStyle(string hair)
        => hair[(hair.IndexOf('_') + 1)..];

    /// <summary>
    /// Retains the current base color when the newly selected hairstyle supports it.
    /// </summary>
    /// <param name="hair">The current native hairstyle.</param>
    /// <param name="item">The newly selected installed hairstyle.</param>
    /// <returns>The installed version-and-style value.</returns>
    private static string HairVariant(string hair, WardrobeCatalogItemPayload item)
    {
        _ = int.TryParse(hair.AsSpan(0, Math.Max(0, hair.IndexOf('_'))), out int version);
        int selected = item.Variants.Contains(version) ? version : item.Variants[0];
        return selected.ToString(CultureInfo.InvariantCulture) + '_' + item.Id;
    }
}
