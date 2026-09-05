using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Coordinates individually authorized tracker-driven development controls.
/// </summary>
public partial class DebugDevelopmentControls
{
    private readonly string?[] _moveIds = new string?[4];
    private DebugDevelopmentStateSnapshot? _state;
    private int _level = 1;
    private string? _abilityId;
    private string? _itemId;
    private string? _evolutionId;
    private string? _devolutionId;
    private int _quantity = 1;
    private string? _error;
    private string? _notice;
    private bool _loading;
    private bool _evolutionsLoading;

    /// <summary>
    /// Gets or initializes the connected-game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Starts loading development state after the component's initial render.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first render.</param>
    /// <returns>A task representing the render lifecycle operation.</returns>
    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _ = LoadAsync();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets whether AutoRevive changes are authorized.
    /// </summary>
    private bool CanAutoRevive => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentAutoRevive);

    /// <summary>
    /// Gets whether full healing is authorized.
    /// </summary>
    private bool CanFullHeal => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentFullHeal);

    /// <summary>
    /// Gets whether level adjustment is authorized.
    /// </summary>
    private bool CanLevel => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentLevel);

    /// <summary>
    /// Gets whether ability changes are authorized.
    /// </summary>
    private bool CanAbility => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentChangeAbility);

    /// <summary>
    /// Gets whether move changes are authorized.
    /// </summary>
    private bool CanMoves => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentChangeMoves);

    /// <summary>
    /// Gets whether item grants are authorized.
    /// </summary>
    private bool CanGiveItem => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentGiveItem);

    /// <summary>
    /// Gets whether evolution and devolution changes are authorized.
    /// </summary>
    private bool CanEvolution => Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentEvolution);

    /// <summary>
    /// Loads the current player state and shared catalogs before loading evolution options separately.
    /// </summary>
    /// <returns>A task representing state and catalog loading.</returns>
    private async Task LoadAsync()
    {
        bool stateLoaded = false;
        _loading = true;
        _evolutionsLoading = false;
        _error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            ApplyState(await Connection.GetDebugDevelopmentStateAsync(includeCatalogs: true, includeEvolutions: false));
            stateLoaded = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }

        if (stateLoaded && _state?.Player is not null && CanEvolution)
            await LoadEvolutionsAsync();
    }

    /// <summary>
    /// Loads the current Pokémon's evolution and devolution options after the main controls render.
    /// </summary>
    /// <returns>A task representing evolution-option loading.</returns>
    private async Task LoadEvolutionsAsync()
    {
        string? expectedPokemonId = _state?.Player?.PokemonId;
        if (expectedPokemonId is null)
            return;

        _evolutionsLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            DebugDevelopmentStateSnapshot evolutionState = await Connection.GetDebugDevelopmentStateAsync(includeCatalogs: false, includeEvolutions: true);
            if (string.Equals(_state?.Player?.PokemonId, expectedPokemonId, StringComparison.Ordinal) && string.Equals(evolutionState.Player?.PokemonId, expectedPokemonId, StringComparison.Ordinal))
                MergeEvolutionState(evolutionState);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _error = exception.Message;
        }
        finally
        {
            _evolutionsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Applies one development action and synchronizes displayed values from the authoritative response.
    /// </summary>
    /// <param name="request">The requested development action.</param>
    /// <param name="noticeKey">The localization key shown after success.</param>
    /// <returns>A task representing the development action.</returns>
    private async Task ApplyAsync(DebugDevelopmentActionRequestPayload request, string noticeKey)
    {
        _loading = true;
        _error = null;
        _notice = null;
        try
        {
            DebugDevelopmentStateSnapshot actionState = await Connection.ApplyDebugDevelopmentActionAsync(request);
            bool speciesChanged = MergeActionState(actionState);
            _notice = Text[noticeKey];
            if (speciesChanged && CanEvolution)
                await LoadEvolutionsAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Replaces the displayed development state and synchronizes its editable player values.
    /// </summary>
    /// <param name="state">The authoritative game-owned state.</param>
    private void ApplyState(DebugDevelopmentStateSnapshot state)
    {
        _state = state;
        ApplyPlayerState(state.Player);
    }

    /// <summary>
    /// Merges a compact action response without discarding previously loaded catalogs.
    /// </summary>
    /// <param name="state">The compact authoritative action response.</param>
    /// <returns><see langword="true"/> when the current Pokémon's species changed; otherwise, <see langword="false"/>.</returns>
    private bool MergeActionState(DebugDevelopmentStateSnapshot state)
    {
        DebugDevelopmentStateSnapshot? previous = _state;
        bool speciesChanged = previous?.Player is not null
            && state.Player is not null
            && !string.Equals(previous.Player.SpeciesId, state.Player.SpeciesId, StringComparison.Ordinal);

        _state = new DebugDevelopmentStateSnapshot
        {
            AutoReviveEnabled = state.AutoReviveEnabled,
            Player = state.Player,
            Abilities = previous?.Abilities ?? [],
            Moves = previous?.Moves ?? [],
            Items = previous?.Items ?? [],
            Evolutions = speciesChanged ? [] : previous?.Evolutions ?? [],
            Devolutions = speciesChanged ? [] : previous?.Devolutions ?? []
        };

        ApplyPlayerState(_state.Player);
        return speciesChanged;
    }

    /// <summary>
    /// Merges asynchronously loaded evolution and devolution options into the displayed state.
    /// </summary>
    /// <param name="state">The evolution-only response.</param>
    private void MergeEvolutionState(DebugDevelopmentStateSnapshot state)
    {
        if (_state is null)
            return;

        _state = new DebugDevelopmentStateSnapshot
        {
            AutoReviveEnabled = _state.AutoReviveEnabled,
            Player = _state.Player,
            Abilities = _state.Abilities,
            Moves = _state.Moves,
            Items = _state.Items,
            Evolutions = state.Evolutions,
            Devolutions = state.Devolutions
        };
    }

    /// <summary>
    /// Synchronizes editable values from the current player snapshot.
    /// </summary>
    /// <param name="player">The current player Pokémon when available.</param>
    private void ApplyPlayerState(DebugDevelopmentPlayerSnapshot? player)
    {
        _evolutionId = null;
        _devolutionId = null;
        if (player is null)
            return;

        _level = player.Level;
        _abilityId = player.AbilityId;
        Array.Clear(_moveIds);
        for (int index = 0; index < Math.Min(_moveIds.Length, player.MoveIds.Count); index++)
            _moveIds[index] = player.MoveIds[index];
    }

    /// <summary>
    /// Applies the AutoRevive state from the toggle input.
    /// </summary>
    /// <param name="args">The checkbox change.</param>
    /// <returns>A task representing the development action.</returns>
    private Task SetAutoReviveAsync(ChangeEventArgs args)
        => ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.SetAutoRevive, Enabled = args.Value is true }, "Development.Saved");

    /// <summary>
    /// Fully heals the current player Pokémon.
    /// </summary>
    /// <returns>A task representing the development action.</returns>
    private Task FullHealAsync()
        => ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.FullHeal }, "Development.FullHeal.Done");

    /// <summary>
    /// Sets the current player Pokémon's level.
    /// </summary>
    /// <returns>A task representing the development action.</returns>
    private Task SetLevelAsync()
        => ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.SetLevel, Level = _level }, "Development.Level.Done");

    /// <summary>
    /// Applies the explicitly selected ability.
    /// </summary>
    /// <param name="abilityId">The selected ability, or <see langword="null"/>.</param>
    /// <returns>A task representing the development action.</returns>
    private async Task SetAbilitySelectionAsync(string? abilityId)
    {
        if (abilityId is null)
            return;

        _abilityId = abilityId;
        await ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.SetAbility, AbilityId = abilityId }, "Development.Ability.Done");
    }

    /// <summary>
    /// Applies a selected or cleared move slot immediately.
    /// </summary>
    /// <param name="index">The zero-based move position.</param>
    /// <param name="moveId">The selected move, or <see langword="null"/>.</param>
    /// <returns>A task representing the development action.</returns>
    private async Task SetMoveSelectionAsync(int index, string? moveId)
    {
        string? previousMoveId = _moveIds[index];
        _moveIds[index] = moveId;
        string[] moves = [.. _moveIds.OfType<string>()];
        if (moves.Length == 0)
        {
            _moveIds[index] = previousMoveId;
            _error = Text["Development.Moves.OneRequired"];
            return;
        }

        if (moves.Distinct(StringComparer.OrdinalIgnoreCase).Count() != moves.Length)
        {
            _moveIds[index] = previousMoveId;
            _error = Text["Development.Moves.NoDuplicates"];
            return;
        }

        await ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.SetMoves, MoveIds = moves }, "Development.Moves.Done");
    }

    /// <summary>
    /// Stores the selected item identifier without granting the item.
    /// </summary>
    /// <param name="itemId">The selected item, or <see langword="null"/>.</param>
    private void SetItemSelection(string? itemId)
        => _itemId = itemId;

    /// <summary>
    /// Adds the selected item quantity to the player's Bag.
    /// </summary>
    /// <returns>A task representing the development action.</returns>
    private Task GiveItemAsync()
        => ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.GiveItem, ItemId = _itemId, Quantity = _quantity }, "Development.Item.Done");

    /// <summary>
    /// Immediately evolves the current Pokémon along the selected direct branch.
    /// </summary>
    /// <param name="speciesId">The selected evolution, or <see langword="null"/>.</param>
    /// <returns>A task representing the development action.</returns>
    private async Task EvolveSelectionAsync(string? speciesId)
    {
        if (speciesId is null)
            return;

        _evolutionId = speciesId;
        await ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.Evolve, SpeciesId = speciesId }, "Development.Evolution.Evolved");
    }

    /// <summary>
    /// Immediately devolves the current Pokémon to the selected direct predecessor.
    /// </summary>
    /// <param name="speciesId">The selected predecessor, or <see langword="null"/>.</param>
    /// <returns>A task representing the development action.</returns>
    private async Task DevolveSelectionAsync(string? speciesId)
    {
        if (speciesId is null)
            return;

        _devolutionId = speciesId;
        await ApplyAsync(new DebugDevelopmentActionRequestPayload { Action = DebugDevelopmentAction.Devolve, SpeciesId = speciesId }, "Development.Evolution.Devolved");
    }
}
