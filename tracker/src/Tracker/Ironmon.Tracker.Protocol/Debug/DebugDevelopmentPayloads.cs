namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Identifies one tracker-driven development action.
/// </summary>
public enum DebugDevelopmentAction
{
    /// <summary>
    /// Changes whether defeat or a draw keeps the attempt active and revives the current player Pokémon.
    /// </summary>
    SetAutoRevive = 0,

    /// <summary>
    /// Fully heals the current player Pokémon.
    /// </summary>
    FullHeal = 1,

    /// <summary>
    /// Sets the current player Pokémon's level.
    /// </summary>
    SetLevel = 2,

    /// <summary>
    /// Assigns an unrestricted game ability to the current player Pokémon.
    /// </summary>
    SetAbility = 3,

    /// <summary>
    /// Replaces the current player Pokémon's moves.
    /// </summary>
    SetMoves = 4,

    /// <summary>
    /// Adds an item to the player's Bag.
    /// </summary>
    GiveItem = 5,

    /// <summary>
    /// Evolves the current player Pokémon to a direct generated target.
    /// </summary>
    Evolve = 6,

    /// <summary>
    /// Devolves the current player Pokémon to a direct generated predecessor.
    /// </summary>
    Devolve = 7,

    /// <summary>
    /// Replaces the current player Pokémon with a new Pokémon of the selected species.
    /// </summary>
    SwapPokemon = 8
}

/// <summary>
/// Carries one authorized tracker-driven development action.
/// </summary>
public sealed class DebugDevelopmentActionRequestPayload
{
    /// <summary>
    /// Initializes an empty development-action request for protocol serialization.
    /// </summary>
    public DebugDevelopmentActionRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the requested development action.
    /// </summary>
    public DebugDevelopmentAction Action { get; init; }

    /// <summary>
    /// Gets or initializes the requested AutoRevive state when applicable.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Gets or initializes the requested Pokémon level when applicable.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>
    /// Gets or initializes the requested stable ability identifier when applicable.
    /// </summary>
    public string? AbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the ordered one-to-four stable move identifiers when applicable.
    /// </summary>
    public IReadOnlyList<string>? MoveIds { get; init; }

    /// <summary>
    /// Gets or initializes the requested stable item identifier when applicable.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the requested item quantity when applicable.
    /// </summary>
    public int? Quantity { get; init; }

    /// <summary>
    /// Gets or initializes the requested target species and form identifier when applicable.
    /// </summary>
    public string? SpeciesId { get; init; }
}

/// <summary>
/// Describes one selectable ability, move, item, evolution, or devolution option.
/// </summary>
public sealed class DebugDevelopmentCatalogEntry
{
    /// <summary>
    /// Initializes an empty development catalog entry for protocol serialization.
    /// </summary>
    public DebugDevelopmentCatalogEntry()
    {
    }

    /// <summary>
    /// Gets or initializes the stable option identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or initializes the localized option name.
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// Describes the current player Pokémon targeted by development actions.
/// </summary>
public sealed class DebugDevelopmentPlayerSnapshot
{
    /// <summary>
    /// Initializes an empty development player snapshot for protocol serialization.
    /// </summary>
    public DebugDevelopmentPlayerSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable individual Pokémon identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the current level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the active stable ability identifier when one is assigned.
    /// </summary>
    public string? AbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the localized active ability name when one is assigned.
    /// </summary>
    public required string AbilityName { get; init; }

    /// <summary>
    /// Gets or initializes the current stable move identifiers in game order.
    /// </summary>
    public IReadOnlyList<string> MoveIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the localized current move names in game order.
    /// </summary>
    public IReadOnlyList<string> MoveNames { get; init; } = [];
}

/// <summary>
/// Describes the currently authorized tracker-driven development controls and options.
/// </summary>
public sealed class DebugDevelopmentStateSnapshot
{
    /// <summary>
    /// Initializes an empty development state snapshot for protocol serialization.
    /// </summary>
    public DebugDevelopmentStateSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes whether NoFail and AutoRevive are currently enabled.
    /// </summary>
    public bool AutoReviveEnabled { get; init; }

    /// <summary>
    /// Gets or initializes the current player Pokémon when one is available.
    /// </summary>
    public DebugDevelopmentPlayerSnapshot? Player { get; init; }

    /// <summary>
    /// Gets or initializes the complete ability catalog when requested and authorized.
    /// </summary>
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Abilities { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete move catalog when requested and authorized.
    /// </summary>
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Moves { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete item catalog when requested and authorized.
    /// </summary>
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Items { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current Pokémon's direct generated evolution targets when requested and authorized.
    /// </summary>
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Evolutions { get; init; } = [];

    /// <summary>
    /// Gets or initializes the current Pokémon's direct generated predecessors when requested and authorized.
    /// </summary>
    public IReadOnlyList<DebugDevelopmentCatalogEntry> Devolutions { get; init; } = [];
}
