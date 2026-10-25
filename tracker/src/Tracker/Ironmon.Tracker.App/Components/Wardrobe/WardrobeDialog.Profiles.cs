using Ironmon.Tracker.Protocol.Wardrobe;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Manages the explicit named-outfit operations within the shared wardrobe.
/// </summary>
public partial class WardrobeDialog
{
    private const string _pieceSeparator = " · ";

    /// <summary>
    /// Submits a saved-outfit name with the standard Enter key.
    /// </summary>
    /// <param name="args">The naming input's keyboard event.</param>
    /// <returns>Completion after any requested profile operation.</returns>
    private Task ProfileNameKeyDown(KeyboardEventArgs args)
        => args.Key == TrackerKeyboardKeys.Enter ? CommitProfileAsync() : Task.CompletedTask;

    /// <summary>
    /// Gets the saved outfit currently shown by the confirmation or naming form.
    /// </summary>
    private WardrobeOutfitProfilePayload? FormProfile => _state.OutfitProfiles.FirstOrDefault(profile => profile.Id == _formProfileId);

    /// <summary>
    /// Gets the selected saved profile without loading it into the game draft.
    /// </summary>
    private WardrobeOutfitProfilePayload? ChosenProfile => _state.OutfitProfiles.FirstOrDefault(profile => profile.Id == _chosenProfile);

    /// <summary>
    /// Formats a saved-profile option, marking a modified loaded appearance without explanatory copy.
    /// </summary>
    /// <param name="profile">The named saved outfit.</param>
    /// <returns>The saved name with a small edited marker when applicable.</returns>
    private string ProfileOption(WardrobeOutfitProfilePayload profile)
        => profile.Name + (profile.Id == _state.LoadedProfileId && (profile.Appearance with { SkinTone = Appearance.SkinTone }) != Appearance ? _editedSuffix : string.Empty);

    /// <summary>
    /// Describes the actual outfit and hairstyle saved in one profile.
    /// </summary>
    /// <param name="profile">The named saved outfit.</param>
    /// <returns>The two authored piece names or an unavailable-item state.</returns>
    private string ProfilePieces(WardrobeOutfitProfilePayload profile)
    {
        if (!profile.Available)
            return Text[WardrobeUi.Unavailable];

        string hair = profile.Appearance.Hair.Contains('_') ? profile.Appearance.Hair[(profile.Appearance.Hair.IndexOf('_') + 1)..] : string.Empty;
        return ItemName(WardrobeProtocol.Clothes, profile.Appearance.Clothes) + _pieceSeparator + ItemName(WardrobeProtocol.Hair, hair);
    }

    /// <summary>
    /// Selects a profile in the quick picker without changing the preview until Load is pressed.
    /// </summary>
    /// <param name="args">The selected saved-outfit identifier.</param>
    private void ProfileSelectionChanged(ChangeEventArgs args)
        => _chosenProfile = args.Value?.ToString();

    /// <summary>
    /// Loads the quick picker's available saved outfit into the game-owned draft.
    /// </summary>
    /// <returns>Completion after the native outfit is accepted.</returns>
    private Task LoadChosenAsync()
        => ChosenProfile is { Available: true } profile ? LoadProfileAsync(profile) : Task.CompletedTask;

    /// <summary>
    /// Loads a saved outfit while leaving the equipped character and skin tone unchanged.
    /// </summary>
    /// <param name="profile">The available saved outfit.</param>
    /// <returns>Completion after the native profile validation and draft update.</returns>
    private Task LoadProfileAsync(WardrobeOutfitProfilePayload profile)
        => MutateAsync(WardrobeAction.LoadProfile, profileId: profile.Id);

    /// <summary>
    /// Opens a local saved-outfit form with the current native appearance still visible.
    /// </summary>
    /// <param name="form">The explicit profile operation.</param>
    /// <param name="profile">The existing named outfit when required.</param>
    private void OpenProfileForm(ProfileForm form, WardrobeOutfitProfilePayload? profile = null)
    {
        _category = WardrobeUi.ProfilesCategory;
        _page = 0;
        _form = form;
        _formProfileId = profile?.Id;
        _formName = form == ProfileForm.Rename ? profile?.Name ?? string.Empty : string.Empty;
        _formError = null;
        _error = null;
        _confirmation = Confirmation.None;
        QueueImages();
    }

    /// <summary>
    /// Dismisses a saved-outfit form without a game mutation.
    /// </summary>
    private void CloseProfileForm()
    {
        _form = ProfileForm.None;
        _formError = null;
    }

    /// <summary>
    /// Validates a requested name and submits the explicit saved-outfit operation.
    /// </summary>
    /// <returns>Completion after the profile is saved, renamed, replaced or deleted.</returns>
    private Task CommitProfileAsync()
    {
        string name = _formName.Trim();
        if (_form is ProfileForm.New or ProfileForm.Rename)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 48 || name.Any(char.IsControl))
            {
                _formError = Text[WardrobeUi.InvalidName];
                return Task.CompletedTask;
            }

            if (_state.OutfitProfiles.Any(profile => profile.Id != _formProfileId && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _formError = Text[WardrobeUi.DuplicateName];
                return Task.CompletedTask;
            }
        }

        return _form switch
        {
            ProfileForm.New => MutateAsync(WardrobeAction.SaveProfile, profileId: Guid.NewGuid().ToString(Ironmon.Tracker.Protocol.Transport.TrackerProtocol.RequestIdFormat), name: name),
            ProfileForm.Rename => MutateAsync(WardrobeAction.RenameProfile, profileId: _formProfileId, name: name),
            ProfileForm.Replace when FormProfile is { } profile => MutateAsync(WardrobeAction.ReplaceProfile, profileId: profile.Id, name: profile.Name),
            ProfileForm.Delete => MutateAsync(WardrobeAction.DeleteProfile, profileId: _formProfileId),
            _ => Task.CompletedTask
        };
    }
}
