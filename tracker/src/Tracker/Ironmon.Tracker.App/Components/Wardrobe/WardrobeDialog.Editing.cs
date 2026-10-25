using Ironmon.Tracker.Protocol.Wardrobe;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Applies native cosmetic edits while keeping local navigation independent of the game draft.
/// </summary>
public partial class WardrobeDialog
{
    /// <summary>
    /// Selects a local browsing category without changing the game-owned appearance.
    /// </summary>
    /// <param name="category">The native or presentation category.</param>
    private void SelectCategory(string category)
    {
        _category = category;
        _search = string.Empty;
        _page = 0;
        _confirmation = Confirmation.None;
        _form = ProfileForm.None;
        _error = null;
        QueueImages();
    }

    /// <summary>
    /// Selects the accessory layer being browsed.
    /// </summary>
    /// <param name="slot">The native accessory field.</param>
    private void SelectSlot(string slot)
    {
        _slot = slot;
        _page = 0;
        QueueImages();
    }

    /// <summary>
    /// Selects a native preview angle without changing the equipped character.
    /// </summary>
    /// <param name="mode">The native preview mode.</param>
    private void SelectView(string mode)
    {
        _view = mode;
        QueueImages();
    }

    /// <summary>
    /// Updates local search while retaining the current category.
    /// </summary>
    /// <param name="args">The search input.</param>
    private void SearchChanged(ChangeEventArgs args)
    {
        _search = args.Value?.ToString() ?? string.Empty;
        _page = 0;
        QueueImages();
    }

    /// <summary>
    /// Updates the ownership filter without a native mutation.
    /// </summary>
    /// <param name="args">The selected ownership filter.</param>
    private void FilterChanged(ChangeEventArgs args)
    {
        _filter = args.Value?.ToString() ?? WardrobeUi.AllFilter;
        _page = 0;
        QueueImages();
    }

    /// <summary>
    /// Moves through catalog pages while keeping the selected look intact.
    /// </summary>
    /// <param name="delta">The signed page offset.</param>
    private void ChangePage(int delta)
    {
        _page = Math.Clamp(_page + delta, 0, PageCount - 1);
        QueueImages();
    }

    /// <summary>
    /// Stages an installed item, or restores its worn slot when the selected item is clicked again.
    /// </summary>
    /// <param name="item">The installed candidate.</param>
    /// <returns>Completion after the game accepts or rejects the draft.</returns>
    private Task SelectItemAsync(WardrobeCatalogItemPayload item)
    {
        if (!item.Available)
            return Task.CompletedTask;

        WardrobeAppearancePayload draft = WardrobeSelection.Select(Appearance, _state.EquippedAppearance, item, _slot);
        return draft == Appearance ? Task.CompletedTask : MutateAsync(WardrobeAction.Change, draft);
    }

    /// <summary>
    /// Clears only the selected native accessory layer.
    /// </summary>
    /// <returns>Completion after the empty accessory selection is accepted.</returns>
    private Task RemoveAccessoryAsync()
        => MutateAsync(WardrobeAction.Change, _slot == WardrobeProtocol.Hat ? Appearance with { Hat = null } : Appearance with { Hat2 = null });

    /// <summary>
    /// Selects one installed base variant of the current hairstyle.
    /// </summary>
    /// <param name="version">The native installed variant.</param>
    /// <returns>Completion after the game accepts the base-color change.</returns>
    private Task ChangeHairVersionAsync(int version)
        => MutateAsync(WardrobeAction.Change, Appearance with { Hair = version.ToString(System.Globalization.CultureInfo.InvariantCulture) + '_' + HairId });

    /// <summary>
    /// Changes the selected dye field while preserving all other appearance values.
    /// </summary>
    /// <param name="field">The native dye field.</param>
    /// <param name="value">The supported hue from zero through 359 degrees.</param>
    /// <returns>Completion after the game accepts the dye.</returns>
    private Task ChangeDyeAsync(string field, int value)
    {
        WardrobeAppearancePayload draft = field switch
        {
            WardrobeProtocol.HairColor => Appearance with { HairColor = value },
            WardrobeProtocol.ClothesColor => Appearance with { ClothesColor = value },
            WardrobeProtocol.HatColor => Appearance with { HatColor = value },
            WardrobeProtocol.Hat2Color => Appearance with { Hat2Color = value },
            WardrobeProtocol.BikeColor => Appearance with { BikeColor = value },
            _ => throw new ArgumentException("Invalid wardrobe dye field.", nameof(field))
        };

        if (field == WardrobeProtocol.BikeColor)
            _view = WardrobeProtocol.Bike;

        return MutateAsync(WardrobeAction.Change, draft);
    }

    /// <summary>
    /// Selects one native skin tone independently of outfit profiles.
    /// </summary>
    /// <param name="tone">The native skin-tone index.</param>
    /// <returns>Completion after the game accepts the skin tone.</returns>
    private Task ChangeSkinAsync(int tone)
        => MutateAsync(WardrobeAction.Change, Appearance with { SkinTone = tone });

    /// <summary>
    /// Selects which native hue palette the color tab displays.
    /// </summary>
    /// <param name="args">The selected native hue field.</param>
    private void ColorTargetChanged(ChangeEventArgs args)
    {
        _colorTarget = args.Value?.ToString() ?? WardrobeProtocol.HairColor;
        _dyeColors = [];
        if (_colorTarget == WardrobeProtocol.BikeColor)
            _view = WardrobeProtocol.Bike;

        QueueImages();
    }

    /// <summary>
    /// Opens the explicit confirmation for the currently selected permanent purchase.
    /// </summary>
    private void AskPurchase()
    {
        _confirmation = Confirmation.Purchase;
        _error = null;
    }

    /// <summary>
    /// Performs the selected permanent purchase using the game's price and balance validation.
    /// </summary>
    /// <returns>Completion after the ownership transaction is acknowledged.</returns>
    private Task PurchaseAsync()
        => SelectedItem is { } item ? MutateAsync(WardrobeAction.Purchase, pieceKey: item.Key) : Task.CompletedTask;

    /// <summary>
    /// Discards a draft immediately when unchanged, otherwise asks for explicit discard confirmation.
    /// </summary>
    /// <returns>Completion after any immediate session cancellation.</returns>
    private Task CancelAsync()
    {
        if (_busy || !Connected)
            return Task.CompletedTask;

        if (_confirmation != Confirmation.None)
        {
            _confirmation = Confirmation.None;
            return Task.CompletedTask;
        }

        if (_form != ProfileForm.None)
        {
            _form = ProfileForm.None;
            return Task.CompletedTask;
        }

        if (Appearance != _state.EquippedAppearance || _state.InitialSelection)
        {
            _confirmation = Confirmation.Discard;
            return Task.CompletedTask;
        }

        return MutateAsync(WardrobeAction.Cancel);
    }

    /// <summary>
    /// Clears a local purchase or discard confirmation without changing the game.
    /// </summary>
    private void ClearConfirmation()
        => _confirmation = Confirmation.None;

    /// <summary>
    /// Applies an owned look or consumes the single free selection and returns control to the game.
    /// </summary>
    /// <returns>Completion after the native profile commit and appearance update.</returns>
    private Task ApplyAsync()
        => MutateAsync(WardrobeAction.Confirm);

    /// <summary>
    /// Sends one mutation while disabling conflicting edits and cancelling obsolete image work.
    /// </summary>
    /// <param name="action">The operation on the existing session.</param>
    /// <param name="appearance">The replacement draft, or null.</param>
    /// <param name="pieceKey">The selected permanent item key, or null.</param>
    /// <param name="profileId">The saved-outfit identity, or null.</param>
    /// <param name="name">The requested saved-outfit name, or null.</param>
    /// <returns>Completion after the game responds or its error is displayed.</returns>
    private async Task MutateAsync(WardrobeAction action, WardrobeAppearancePayload? appearance = null, string? pieceKey = null, string? profileId = null, string? name = null)
    {
        if (Disabled)
            return;

        _busy = true;
        _error = null;
        CancelImages();
        try
        {
            _state = await Client.ExecuteAsync(action, appearance, pieceKey, profileId, name, _lifetime.Token);
            _owned = new(_state.OwnedKeys, StringComparer.Ordinal);
            _confirmation = Confirmation.None;
            _form = ProfileForm.None;
            _formError = null;
            if (_observedLoadedProfile != _state.LoadedProfileId)
            {
                _observedLoadedProfile = _state.LoadedProfileId;
                _chosenProfile = _state.LoadedProfileId;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state = Client.Snapshot ?? _state;
            _owned = new(_state.OwnedKeys, StringComparer.Ordinal);
            _error = exception.Message;
        }
        finally
        {
            _busy = false;
            _page = Math.Clamp(_page, 0, PageCount - 1);
            if (!_disposed && _state.Open)
                QueueImages();
        }
    }

    /// <summary>
    /// Requests the existing native expand or restore behavior for this dialog only.
    /// </summary>
    /// <returns>Completion after the native window operation.</returns>
    private async Task ToggleExpandedAsync()
        => _maximized = await WindowService.ToggleMaximizedAsync();
}
