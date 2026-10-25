using Ironmon.Tracker.App.Components.Wardrobe;
using Ironmon.Tracker.Protocol.Wardrobe;

namespace Ironmon.Tracker.App.Tests.Wardrobe;

/// <summary>
/// Verifies toggling a preview restores its worn slot without discarding other edits.
/// </summary>
public sealed class WardrobeSelectionTests
{
    private const string _outfit = "worn-outfit";
    private const string _previewOutfit = "preview-outfit";
    private const string _hair = "3_worn_hair";
    private const string _previewHair = "2_preview_hair";
    private const string _previewStyle = "preview_hair";
    private const string _hat = "glasses";
    private const string _previewHat = "flower";

    /// <summary>
    /// Restores the worn outfit and its dye while retaining an edited hairstyle and accessories.
    /// </summary>
    [Fact]
    public void ClickingPreviewedOutfitAgainRestoresOnlyItsWornSlot()
    {
        WardrobeAppearancePayload worn = Worn();
        WardrobeAppearancePayload draft = worn with { Clothes = _previewOutfit, ClothesColor = 240, Hair = _previewHair, HairColor = 137, Hat2 = _previewHat };
        WardrobeCatalogItemPayload clicked = new() { Category = WardrobeProtocol.Clothes, Id = _previewOutfit };
        Assert.Equal(draft with { Clothes = worn.Clothes, ClothesColor = worn.ClothesColor }, WardrobeSelection.Select(draft, worn, clicked, WardrobeProtocol.Hat));
        Assert.Equal(worn, WardrobeSelection.Select(worn, worn, new() { Category = WardrobeProtocol.Clothes, Id = _outfit }, WardrobeProtocol.Hat));
    }

    /// <summary>
    /// Restores the worn hair's exact base and dye after selecting another hairstyle.
    /// </summary>
    [Fact]
    public void HairToggleRestoresWornBaseAndDyeWithoutChangingOutfit()
    {
        WardrobeAppearancePayload worn = Worn();
        WardrobeAppearancePayload draft = worn with { Clothes = _previewOutfit, HairColor = 137 };
        WardrobeCatalogItemPayload clicked = new() { Category = WardrobeProtocol.Hair, Id = _previewStyle, Variants = [2, 5] };
        WardrobeAppearancePayload selected = WardrobeSelection.Select(draft, worn, clicked, WardrobeProtocol.Hat);
        Assert.Equal(_previewHair, selected.Hair);
        Assert.Equal(draft with { Hair = worn.Hair, HairColor = worn.HairColor }, WardrobeSelection.Select(selected, worn, clicked, WardrobeProtocol.Hat));
    }

    /// <summary>
    /// Restores an empty or occupied worn accessory slot without changing the other layer.
    /// </summary>
    /// <param name="slot">The accessory layer to toggle.</param>
    [Theory]
    [InlineData(WardrobeProtocol.Hat)]
    [InlineData(WardrobeProtocol.Hat2)]
    public void AccessoryToggleRestoresOnlyTheActiveWornLayer(string slot)
    {
        WardrobeAppearancePayload worn = Worn();
        WardrobeAppearancePayload draft = worn with { Hat = _previewHat, Hat2 = _previewHat, HatColor = 120, Hat2Color = 200, Clothes = _previewOutfit };
        WardrobeCatalogItemPayload clicked = new() { Category = WardrobeProtocol.Hat, Id = _previewHat };
        WardrobeAppearancePayload expected = slot == WardrobeProtocol.Hat ? draft with { Hat = worn.Hat, HatColor = worn.HatColor } : draft with { Hat2 = worn.Hat2, Hat2Color = worn.Hat2Color };
        Assert.Equal(expected, WardrobeSelection.Select(draft, worn, clicked, slot));
    }

    /// <summary>
    /// Creates a worn outfit with an occupied first accessory and an empty second layer.
    /// </summary>
    /// <returns>The acknowledged worn appearance.</returns>
    private static WardrobeAppearancePayload Worn()
        => new() { Clothes = _outfit, Hair = _hair, Hat = _hat, ClothesColor = 40, HairColor = 70, HatColor = 30, Hat2Color = 10, SkinTone = 3 };
}
