using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps the existing attempts columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedAttemptRow
{
    private const string _table = "attempts";
    private const string _attempt = "attempt_id";
    private const string _context = "initial_context_id";
    private const string _started = "started_utc_ms";
    private const string _status = "status";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte attempt identity.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[] Attempt { get; init; } = [];

    /// <summary>
    /// Gets or initializes the initial retained context identity.
    /// </summary>
    [SqlColumn(_context)]
    public long Context { get; init; }

    /// <summary>
    /// Gets or initializes the creation time in UTC Unix milliseconds.
    /// </summary>
    [SqlColumn(_started)]
    public long Started { get; init; }

    /// <summary>
    /// Gets or initializes the initial open attempt status.
    /// </summary>
    [SqlColumn(_status)]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps the existing owned_individuals columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedIndividualRow
{
    private const string _table = "owned_individuals";
    private const string _id = "individual_id";
    private const string _attempt = "attempt_id";
    private const string _key = "entity_key";
    private const string _revision = "first_recorded_revision";

    /// <summary>
    /// Gets or initializes the exact owned individual identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte attempt identity.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[] Attempt { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete canonical owned entity key.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte first recorded revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps the existing owned_record_state columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedRecordStateRow
{
    private const string _table = "owned_record_state";
    private const string _individual = "individual_id";
    private const string _fingerprint = "fingerprint";
    private const string _revision = "last_revision";
    private const string _configuration = "last_configuration_id";

    /// <summary>
    /// Gets or initializes the owned individual identity.
    /// </summary>
    [SqlColumn(_individual)]
    public long Individual { get; init; }

    /// <summary>
    /// Gets or initializes the complete meaningful configuration fingerprint.
    /// </summary>
    [SqlColumn(_fingerprint)]
    public byte[] Fingerprint { get; init; } = [];

    /// <summary>
    /// Gets or initializes the last retained per-individual configuration revision number.
    /// </summary>
    [SqlColumn(_revision)]
    public long Revision { get; init; }

    /// <summary>
    /// Gets or initializes the last retained complete configuration identity.
    /// </summary>
    [SqlColumn(_configuration)]
    public long Configuration { get; init; }
}

/// <summary>
/// Maps the existing stat_vectors columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class StatVectorRow
{
    private const string _table = "stat_vectors";
    private const string _id = "stat_vector_id";
    private const string _hp = "hp";
    private const string _attack = "attack";
    private const string _defense = "defense";
    private const string _specialAttack = "special_attack";
    private const string _specialDefense = "special_defense";
    private const string _speed = "speed";

    /// <summary>
    /// Gets or initializes the exact immutable stat vector identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative HP stat value.
    /// </summary>
    [SqlColumn(_hp)]
    public long Hp { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative attack stat value.
    /// </summary>
    [SqlColumn(_attack)]
    public long Attack { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative defense stat value.
    /// </summary>
    [SqlColumn(_defense)]
    public long Defense { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative special attack stat value.
    /// </summary>
    [SqlColumn(_specialAttack)]
    public long SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative special defense stat value.
    /// </summary>
    [SqlColumn(_specialDefense)]
    public long SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative speed stat value.
    /// </summary>
    [SqlColumn(_speed)]
    public long Speed { get; init; }
}

/// <summary>
/// Maps the existing movesets columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedMovesetRow
{
    private const string _table = "movesets";
    private const string _id = "moveset_id";
    private const string _slots = "slot_count";
    private const string _digest = "digest";

    /// <summary>
    /// Gets or initializes the exact simultaneous moveset identity.
    /// </summary>
    [SqlColumn(_id)]
    public long Id { get; init; }

    /// <summary>
    /// Gets or initializes the number of simultaneously retained move slots, zero through four.
    /// </summary>
    [SqlColumn(_slots)]
    public int Slots { get; init; }

    /// <summary>
    /// Gets or initializes the complete moveset digest including slot and PP capacity metadata.
    /// </summary>
    [SqlColumn(_digest)]
    public byte[] Digest { get; init; } = [];
}

/// <summary>
/// Maps the existing moveset_slots columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedMoveSlotRow
{
    private const string _table = "moveset_slots";
    private const string _moveset = "moveset_id";
    private const string _slot = "slot_no";
    private const string _move = "move_identity_id";
    private const string _maxPp = "max_pp";
    private const string _ppUps = "pp_up_count";

    /// <summary>
    /// Gets or initializes the owning simultaneous moveset identity.
    /// </summary>
    [SqlColumn(_moveset)]
    public long Moveset { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based move slot ordinal.
    /// </summary>
    [SqlColumn(_slot)]
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact move identity.
    /// </summary>
    [SqlColumn(_move)]
    public long Move { get; init; }

    /// <summary>
    /// Gets or initializes the exact nonnegative PP capacity.
    /// </summary>
    [SqlColumn(_maxPp)]
    public long MaxPp { get; init; }

    /// <summary>
    /// Gets or initializes the PP Up count, or null when unavailable.
    /// </summary>
    [SqlColumn(_ppUps)]
    public long? PpUps { get; init; }
}

/// <summary>
/// Maps the existing configuration_revisions columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedConfigurationRow
{
    private const string _table = "configuration_revisions";
    private const string _individual = "individual_id";
    private const string _revision = "revision_no";
    private const string _previous = "previous_revision_no";
    private const string _branch = "play_branch_identity_id";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _recorded = "recorded_at_revision";
    private const string _capture = "capture_kind";
    private const string _role = "ownership_role";
    private const string _retained = "retained_after_commit";
    private const string _species = "species_identity_id";
    private const string _form = "form_identity_id";
    private const string _head = "head_species_identity_id";
    private const string _body = "body_species_identity_id";
    private const string _level = "level";
    private const string _experience = "experience";
    private const string _generated = "generated_stats_id";
    private const string _reference = "reference_stats_id";
    private const string _computed = "computed_stats_id";
    private const string _rawIv = "raw_iv_id";
    private const string _effectiveIv = "effective_iv_id";
    private const string _ev = "ev_id";
    private const string _moveset = "moveset_id";
    private const string _ability = "ability_identity_id";
    private const string _nature = "nature_identity_id";
    private const string _effectiveNature = "effective_nature_identity_id";
    private const string _heldItem = "held_item_identity_id";
    private const string _favorite = "favorite_context_id";
    private const string _detail = "detail_block_id";
    private const string _checkpoint = "checkpoint_block_id";

    /// <summary>
    /// Gets or initializes the owned individual identity.
    /// </summary>
    [SqlColumn(_individual)]
    public long Individual { get; init; }

    /// <summary>
    /// Gets or initializes the per-individual configuration revision number.
    /// </summary>
    [SqlColumn(_revision)]
    public long Revision { get; init; }

    /// <summary>
    /// Gets or initializes the preceding per-individual revision number, or null for the first.
    /// </summary>
    [SqlColumn(_previous)]
    public long? Previous { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact play branch identity.
    /// </summary>
    [SqlColumn(_branch)]
    public long Branch { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte global collection revision.
    /// </summary>
    [SqlColumn(_recorded)]
    public byte[] Recorded { get; init; } = [];

    /// <summary>
    /// Gets or initializes the normalized configuration capture kind.
    /// </summary>
    [SqlColumn(_capture)]
    public string Capture { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the actual retained ownership role.
    /// </summary>
    [SqlColumn(_role)]
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the whether the individual remains owned after this commit, zero or one.
    /// </summary>
    [SqlColumn(_retained)]
    public int Retained { get; init; }

    /// <summary>
    /// Gets or initializes the interned actual species identity.
    /// </summary>
    [SqlColumn(_species)]
    public long Species { get; init; }

    /// <summary>
    /// Gets or initializes the interned form identity, or null when absent.
    /// </summary>
    [SqlColumn(_form)]
    public long? Form { get; init; }

    /// <summary>
    /// Gets or initializes the interned fusion head species identity, or null when absent.
    /// </summary>
    [SqlColumn(_head)]
    public long? Head { get; init; }

    /// <summary>
    /// Gets or initializes the interned fusion body species identity, or null when absent.
    /// </summary>
    [SqlColumn(_body)]
    public long? Body { get; init; }

    /// <summary>
    /// Gets or initializes the actual Pokemon level.
    /// </summary>
    [SqlColumn(_level)]
    public long Level { get; init; }

    /// <summary>
    /// Gets or initializes the exact cumulative Pokemon experience.
    /// </summary>
    [SqlColumn(_experience)]
    public long Experience { get; init; }

    /// <summary>
    /// Gets or initializes the complete generated base-stat vector identity.
    /// </summary>
    [SqlColumn(_generated)]
    public long Generated { get; init; }

    /// <summary>
    /// Gets or initializes the reference base-stat vector identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_reference)]
    public long? Reference { get; init; }

    /// <summary>
    /// Gets or initializes the complete permanent computed-stat vector identity.
    /// </summary>
    [SqlColumn(_computed)]
    public long Computed { get; init; }

    /// <summary>
    /// Gets or initializes the raw IV vector identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_rawIv)]
    public long? RawIv { get; init; }

    /// <summary>
    /// Gets or initializes the effective IV vector identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_effectiveIv)]
    public long? EffectiveIv { get; init; }

    /// <summary>
    /// Gets or initializes the EV vector identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_ev)]
    public long? Ev { get; init; }

    /// <summary>
    /// Gets or initializes the complete simultaneous moveset identity.
    /// </summary>
    [SqlColumn(_moveset)]
    public long Moveset { get; init; }

    /// <summary>
    /// Gets or initializes the interned ability identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_ability)]
    public long? Ability { get; init; }

    /// <summary>
    /// Gets or initializes the interned nature identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_nature)]
    public long? Nature { get; init; }

    /// <summary>
    /// Gets or initializes the interned effective nature identity, or null when unavailable.
    /// </summary>
    [SqlColumn(_effectiveNature)]
    public long? EffectiveNature { get; init; }

    /// <summary>
    /// Gets or initializes the interned actual held-item identity, or null when absent.
    /// </summary>
    [SqlColumn(_heldItem)]
    public long? HeldItem { get; init; }

    /// <summary>
    /// Gets or initializes the retained favorite context identity, or null when absent.
    /// </summary>
    [SqlColumn(_favorite)]
    public long? Favorite { get; init; }

    /// <summary>
    /// Gets or initializes the immutable complete detail block identity.
    /// </summary>
    [SqlColumn(_detail)]
    public long Detail { get; init; }

    /// <summary>
    /// Gets or initializes the complete resource checkpoint block identity, or null when absent.
    /// </summary>
    [SqlColumn(_checkpoint)]
    public long? Checkpoint { get; init; }
}

/// <summary>
/// Maps the existing owned_partial_configurations columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedPartialConfigurationRow
{
    private const string _table = "owned_partial_configurations";
    private const string _individual = "individual_id";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _detail = "detail_block_id";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the owned individual identity.
    /// </summary>
    [SqlColumn(_individual)]
    public long Individual { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the immutable complete detail block identity.
    /// </summary>
    [SqlColumn(_detail)]
    public long Detail { get; init; }

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps the existing owned_lineage_edges columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class OwnedLineageRow
{
    private const string _table = "owned_lineage_edges";
    private const string _child = "child_configuration_id";
    private const string _role = "role";
    private const string _ordinal = "ordinal";
    private const string _source = "source_configuration_id";
    private const string _sourceContext = "source_context_id";
    private const string _transaction = "transaction_context_id";

    /// <summary>
    /// Gets or initializes the actual child configuration identity.
    /// </summary>
    [SqlColumn(_child)]
    public long Child { get; init; }

    /// <summary>
    /// Gets or initializes the actual lineage relationship role.
    /// </summary>
    [SqlColumn(_role)]
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the zero-based parent relationship ordinal.
    /// </summary>
    [SqlColumn(_ordinal)]
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets or initializes the actual retained parent configuration, or null when only a source context exists.
    /// </summary>
    [SqlColumn(_source)]
    public long? Source { get; init; }

    /// <summary>
    /// Gets or initializes the actual unretained parent context, or null for a retained parent.
    /// </summary>
    [SqlColumn(_sourceContext)]
    public long? SourceContext { get; init; }

    /// <summary>
    /// Gets or initializes the actual ownership transaction context identity.
    /// </summary>
    [SqlColumn(_transaction)]
    public long Transaction { get; init; }
}

/// <summary>
/// Maps the existing completed_attempt_reducer_states columns used by generated ownership and recovery queries.
/// </summary>
[SqlTable(_table)]
internal sealed class CompletedAttemptRow
{
    private const string _table = "completed_attempt_reducer_states";
    private const string _attempt = "attempt_id";
    private const string _format = "format_version";
    private const string _codec = "state_codec";
    private const string _payload = "state_blob";
    private const string _length = "uncompressed_length";
    private const string _stream = "source_stream_id";
    private const string _sequence = "source_sequence";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact sixteen-byte attempt identity.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[] Attempt { get; init; } = [];

    /// <summary>
    /// Gets or initializes the checkpoint format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the checkpoint encoding discriminator.
    /// </summary>
    [SqlColumn(_codec)]
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the complete compressed sufficient attempt state.
    /// </summary>
    [SqlColumn(_payload)]
    public byte[] Payload { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact uncompressed state length in bytes.
    /// </summary>
    [SqlColumn(_length)]
    public long Length { get; init; }

    /// <summary>
    /// Gets or initializes the exact sixteen-byte source stream identity.
    /// </summary>
    [SqlColumn(_stream)]
    public byte[] Stream { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte source sequence.
    /// </summary>
    [SqlColumn(_sequence)]
    public byte[] Sequence { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact eight-byte collection revision.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}

/// <summary>
/// Maps existing attempts columns for the remaining typed collection operations.
/// </summary>
[SqlTable(_table)]
internal sealed class StoreAttemptWriteRow
{
    private const string _table = "attempts";
    private const string _attempt = "attempt_id";
    private const string _closed = "closed_utc_ms";
    private const string _status = "status";
    private const string _summary = "terminal_summary_block_id";
    private const string _revision = "revision";

    /// <summary>
    /// Gets or initializes the exact stored attempt_id value.
    /// </summary>
    [SqlColumn(_attempt)]
    public byte[] Attempt { get; init; } = [];

    /// <summary>
    /// Gets or initializes the exact stored closed_utc_ms value.
    /// </summary>
    [SqlColumn(_closed)]
    public long? Closed { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored status value.
    /// </summary>
    [SqlColumn(_status)]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the exact stored terminal_summary_block_id value.
    /// </summary>
    [SqlColumn(_summary)]
    public long? Summary { get; init; }

    /// <summary>
    /// Gets or initializes the exact stored revision value.
    /// </summary>
    [SqlColumn(_revision)]
    public byte[] Revision { get; init; } = [];
}
