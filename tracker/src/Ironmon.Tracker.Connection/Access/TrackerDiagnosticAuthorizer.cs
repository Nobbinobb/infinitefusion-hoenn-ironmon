using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Access;

/// <summary>
/// Evaluates negotiated diagnostic capabilities and enforces request-specific access policy.
/// </summary>
internal sealed class TrackerDiagnosticAuthorizer
{
    private readonly DiagnosticAccessService? _access;
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerConnectionState _state;

    /// <summary>
    /// Initializes diagnostic authorization for one tracker connection.
    /// </summary>
    /// <param name="options">The tracker launch options.</param>
    /// <param name="state">The connected game state.</param>
    /// <param name="access">The current signed diagnostic access, when configured.</param>
    internal TrackerDiagnosticAuthorizer(TrackerConnectionOptions options, TrackerConnectionState state, DiagnosticAccessService? access)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        _options = options;
        _state = state;
        _access = access;
    }

    /// <summary>
    /// Gets whether both tracker launch mode and the connected game authorize unrestricted development access.
    /// </summary>
    internal bool DebugAuthorized => _options.DebugRequested && _state.Snapshot.Game?.DebugAvailable == true;

    /// <summary>
    /// Determines whether one named diagnostic capability is effective for the connected game.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether legacy development access or a negotiated grant authorizes the capability.</returns>
    internal bool HasCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (DebugAuthorized)
            return true;

        GameHandshakePayload? game = _state.Snapshot.Game;
        DiagnosticAccessSnapshot? access = _access?.Snapshot;
        return game is not null
            && access?.State != DiagnosticAccessState.DeveloperOverride
            && game.SupportedDiagnosticCapabilities.Contains(capability, StringComparer.Ordinal)
            && access?.HasCapability(capability) == true;
    }

    /// <summary>
    /// Gets the token capabilities supported by one connected game in stable order.
    /// </summary>
    /// <param name="game">The connected game handshake.</param>
    /// <returns>The negotiated capability identifiers.</returns>
    internal IReadOnlyList<string> GetNegotiatedCapabilities(GameHandshakePayload game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (_access is null || (_access.Snapshot.State == DiagnosticAccessState.DeveloperOverride && !(_options.DebugRequested && game.DebugAvailable)))
            return [];

        HashSet<string> supported = new(game.SupportedDiagnosticCapabilities, StringComparer.Ordinal);
        return [.. _access.Snapshot.EffectiveCapabilities.Where(supported.Contains).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Determines whether active-run details for one area category are authorized.
    /// </summary>
    /// <param name="category">The area content category.</param>
    /// <returns>Whether the category-specific capability is effective.</returns>
    internal bool HasAreaDetails(AreaContentCategory category)
        => HasCapability(GetAreaCapability(category));

    /// <summary>
    /// Rejects diagnostic requests unless every required named capability is effective.
    /// </summary>
    /// <param name="capabilities">The capabilities required by the request.</param>
    /// <exception cref="InvalidOperationException">Thrown when diagnostic access is not authorized.</exception>
    internal void EnsureAll(params string[] capabilities)
    {
        if (capabilities.Any(capability => !HasCapability(capability)))
            throw new InvalidOperationException("The connected game has not been granted every diagnostic capability required by this request.");
    }

    /// <summary>
    /// Rejects a composite diagnostic request unless at least one supported section is effective.
    /// </summary>
    /// <param name="capabilities">The independently optional diagnostic sections.</param>
    /// <exception cref="InvalidOperationException">Thrown when no requested diagnostic section is authorized.</exception>
    internal void EnsureAny(params string[] capabilities)
    {
        if (!capabilities.Any(HasCapability))
            throw new InvalidOperationException("The connected game has not been granted access to any run diagnostic section.");
    }

    /// <summary>
    /// Rejects an arbitrary species request without all-active access, or validates the selected live source.
    /// </summary>
    /// <param name="target">The optional live source that supplies the requested species.</param>
    internal void EnsurePokemonSource(DebugPokemonTarget? target)
    {
        if (target is null)
        {
            EnsureAll(DiagnosticCapabilities.PokemonAllActive);
            return;
        }

        EnsureAll(GetAvailabilityCapability(target.Value));
    }

    /// <summary>
    /// Rejects an arbitrary active-run lookup without all-active and exact section access.
    /// </summary>
    /// <param name="section">The requested information section.</param>
    internal void EnsurePokemonLookup(PokemonLookupSection section)
        => EnsureAll(DiagnosticCapabilities.PokemonAllActive, GetInformationCapability(section));

    /// <summary>
    /// Rejects an inspector section unless at least one independently rendered surface is authorized.
    /// </summary>
    /// <param name="section">The requested shared information section.</param>
    internal void EnsurePokemonInformation(PokemonLookupSection section)
    {
        if (section == PokemonLookupSection.Overview)
        {
            EnsureAny(DiagnosticCapabilities.PokemonOverview, DiagnosticCapabilities.WorldWildEncounters, DiagnosticCapabilities.WorldTrainerParties, DiagnosticCapabilities.FusionMaterialPairs, DiagnosticCapabilities.FusionPreviewResults);
            return;
        }

        if (section == PokemonLookupSection.Evolutions)
        {
            EnsureAny(DiagnosticCapabilities.EvolutionResults, DiagnosticCapabilities.EvolutionCandidates);
            return;
        }

        EnsureAll(GetInformationCapability(section));
    }

    /// <summary>
    /// Gets the availability capability required by one live Pokemon target.
    /// </summary>
    /// <param name="target">The requested live target.</param>
    /// <returns>The stable availability capability.</returns>
    private static string GetAvailabilityCapability(DebugPokemonTarget target)
    {
        return target switch
        {
            DebugPokemonTarget.Player => DiagnosticCapabilities.PokemonCurrentPlayer,
            DebugPokemonTarget.Enemy => DiagnosticCapabilities.PokemonCurrentEnemies,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Only the represented current player or current enemy may be inspected.")
        };
    }

    /// <summary>
    /// Gets the information capability required by one Pokemon section.
    /// </summary>
    /// <param name="section">The requested information section.</param>
    /// <returns>The stable information capability.</returns>
    private static string GetInformationCapability(PokemonLookupSection section)
    {
        return section switch
        {
            PokemonLookupSection.Overview => DiagnosticCapabilities.PokemonOverview,
            PokemonLookupSection.Abilities => DiagnosticCapabilities.PokemonAbilities,
            PokemonLookupSection.Stats => DiagnosticCapabilities.PokemonBaseStats,
            PokemonLookupSection.Moves => DiagnosticCapabilities.PokemonMoveAccess,
            PokemonLookupSection.Evolutions => DiagnosticCapabilities.EvolutionResults,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "The Pokemon lookup section is unsupported.")
        };
    }

    /// <summary>
    /// Gets the active-run information capability associated with one area category.
    /// </summary>
    /// <param name="category">The area content category.</param>
    /// <returns>The stable information capability.</returns>
    private static string GetAreaCapability(AreaContentCategory category)
    {
        return category switch
        {
            AreaContentCategory.Trainer => DiagnosticCapabilities.WorldTrainerParties,
            AreaContentCategory.Encounter => DiagnosticCapabilities.WorldWildEncounters,
            AreaContentCategory.Item => DiagnosticCapabilities.WorldItems,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "The area content category is unsupported.")
        };
    }
}
