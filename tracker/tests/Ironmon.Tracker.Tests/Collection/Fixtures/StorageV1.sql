-- Gameplay collection schema version 1, initialized only for the new release epoch.
-- Uses the accepted compact relational partition and typed-summary representation.
-- One database per collection profile/epoch. BLOB byte widths use CHECK constraints.
PRAGMA foreign_keys = ON;

-- collection_meta: One row identifies this profile/epoch database and its physical store incarnation. The collection starts empty at release; technical baseline context does not seed gameplay counters.
CREATE TABLE "collection_meta" (
  "meta_id" INTEGER NOT NULL CHECK(meta_id = 1),
  "profile_id" BLOB NOT NULL CHECK(length(profile_id)=16),
  "epoch_id" BLOB NOT NULL CHECK(length(epoch_id)=16),
  "store_incarnation_id" BLOB NOT NULL CHECK(length(store_incarnation_id)=16),
  "schema_version" INTEGER NOT NULL CHECK(schema_version>0),
  "created_utc_ms" INTEGER NOT NULL ,
  "collection_revision" BLOB NOT NULL CHECK(length(collection_revision)=8),
  "minimum_reader_version" INTEGER NOT NULL CHECK(minimum_reader_version>0),
  "coverage_start_kind" TEXT NOT NULL CHECK(coverage_start_kind='fresh_at_release'),
  PRIMARY KEY ("meta_id")
) STRICT;


-- contexts: Immutable namespace/catalog/generation/rules/preferences/load contexts. Context can describe old state technically without treating it as earned progress.
CREATE TABLE "contexts" (
  "context_id" INTEGER NOT NULL ,
  "context_kind" TEXT NOT NULL ,
  "canonical_key" BLOB NOT NULL ,
  "key_hash" BLOB NOT NULL CHECK(length(key_hash)=32),
  "format_version" INTEGER NOT NULL CHECK(format_version>0),
  "origin_kind" TEXT NOT NULL CHECK(origin_kind IN ('technical_baseline','observed','local_definition')),
  "block_id" INTEGER NOT NULL ,
  PRIMARY KEY ("context_id"),
  UNIQUE ("context_kind", "format_version", "canonical_key"),
  CONSTRAINT "fk_contexts_1" FOREIGN KEY ("block_id") REFERENCES "block_values" ("block_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;
CREATE INDEX "ix_contexts_hash" ON "contexts" ("context_kind", "format_version", "key_hash");

-- identities: Intern canonical native IDs and typed literal atoms. Namespace revision changes only when native ID meanings change; seed, generation profile, build, locale and ordinary source contexts do not split identity membership.
CREATE TABLE identity_namespaces (
 namespace_id INTEGER PRIMARY KEY,
 namespace_revision TEXT NOT NULL,
 namespace TEXT NOT NULL,
 UNIQUE(namespace,namespace_revision)
) STRICT;
CREATE TABLE identity_values (
 identity_id INTEGER PRIMARY KEY,
 namespace_id INTEGER NOT NULL REFERENCES identity_namespaces(namespace_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 native_key BLOB NOT NULL,
 source_context_id INTEGER REFERENCES contexts(context_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 UNIQUE(namespace_id,native_key)
) STRICT;
CREATE INDEX ix_identity_values_namespace ON identity_values(namespace_id,identity_id);
CREATE VIEW identities AS
 SELECT i.identity_id,n.namespace_revision,n.namespace,i.native_key,i.source_context_id
 FROM identity_values i JOIN identity_namespaces n USING(namespace_id);

-- identity_labels: Optional human-readable names for items, moves, species and other native IDs. Translation is separate from canonical gameplay identity.
CREATE TABLE "identity_labels" (
  "identity_id" INTEGER NOT NULL ,
  "locale" TEXT NOT NULL ,
  "display_name" TEXT NOT NULL ,
  "source_context_id" INTEGER ,
  PRIMARY KEY ("identity_id", "locale"),
  CONSTRAINT "fk_identity_labels_1" FOREIGN KEY ("identity_id") REFERENCES "identity_values" ("identity_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_identity_labels_2" FOREIGN KEY ("source_context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


-- tuples: Intern complete ordered dimension keys once. Empty dimensions use one actual empty-tuple row, not NULL.
CREATE TABLE "tuples" (
  "tuple_id" INTEGER NOT NULL ,
  "format_version" INTEGER NOT NULL CHECK(format_version>0),
  "canonical_key" BLOB NOT NULL ,
  "arity" INTEGER NOT NULL CHECK(arity>=0),
  PRIMARY KEY ("tuple_id"),
  UNIQUE ("format_version", "canonical_key")
) STRICT;

-- tuple_members: Real foreign keys for ordered dimension atoms. This makes relational tuple references inspectable and enforceable, rather than pretending embedded BLOB IDs have SQL FKs.
CREATE TABLE "tuple_members" (
  "tuple_id" INTEGER NOT NULL ,
  "ordinal" INTEGER NOT NULL CHECK(ordinal>=0),
  "identity_id" INTEGER NOT NULL ,
  PRIMARY KEY ("tuple_id", "ordinal"),
  CONSTRAINT "fk_tuple_members_1" FOREIGN KEY ("tuple_id") REFERENCES "tuples" ("tuple_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_tuple_members_2" FOREIGN KEY ("identity_id") REFERENCES "identity_values" ("identity_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_tuple_members_identity" ON "tuple_members" ("identity_id", "tuple_id");

-- aggregate_families: Versioned generic collector definitions. The 258 current families are rows here, not 258 physical tables and not achievement IDs.
CREATE TABLE "aggregate_families" (
  "family_id" INTEGER NOT NULL ,
  "family_key" TEXT NOT NULL ,
  "projection_version" INTEGER NOT NULL CHECK(projection_version>0),
  "owner_event_key" TEXT NOT NULL ,
  "storage_kind" TEXT NOT NULL CHECK(storage_kind IN ('additive','set_union','histogram_or_joint','extrema','joint_additive_extrema','ordered_current','ordered_transient','ordered_streak')),
  "definition_block_id" INTEGER NOT NULL ,
  "definition_hash" BLOB NOT NULL CHECK(length(definition_hash)=32),
  PRIMARY KEY ("family_id"),
  UNIQUE ("family_key", "projection_version"),
  CONSTRAINT "fk_aggregate_families_1" FOREIGN KEY ("definition_block_id") REFERENCES "block_values" ("block_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;


-- family_measures: Measure declarations inside one family version, including value codec, units and merge operation. A family may retain several numeric and/or exact-membership measures.
CREATE TABLE "family_measures" (
  "family_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL CHECK(measure_no>=0),
  "measure_key" TEXT NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind IN ('scalar','exact_set','ordered_payload')),
  "value_codec" TEXT NOT NULL ,
  "unit" TEXT NOT NULL ,
  "merge_operator" TEXT NOT NULL CHECK(merge_operator IN ('sum','min','max','union','ordered_replace','ordered_reduce')),
  PRIMARY KEY ("family_id", "measure_no"),
  UNIQUE ("family_id", "measure_key"),
  UNIQUE ("family_id", "measure_no", "measure_kind"),
  CONSTRAINT "fk_family_measures_1" FOREIGN KEY ("family_id") REFERENCES "aggregate_families" ("family_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


-- scopes: Exact collection boundaries. A nonnullable canonical scope key avoids NULL-composite-UNIQUE duplicate lifetime rows.
CREATE TABLE "scopes" (
  "scope_id" INTEGER NOT NULL ,
  "scope_kind" TEXT NOT NULL ,
  "exact_key" BLOB NOT NULL ,
  "parent_scope_id" INTEGER ,
  "attempt_id" BLOB CHECK(attempt_id IS NULL OR length(attempt_id)=16),
  "context_id" INTEGER ,
  PRIMARY KEY ("scope_id"),
  UNIQUE ("scope_kind", "exact_key"),
  CONSTRAINT "fk_scopes_1" FOREIGN KEY ("parent_scope_id") REFERENCES "scopes" ("scope_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_scopes_2" FOREIGN KEY ("attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_scopes_3" FOREIGN KEY ("context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;
CREATE INDEX "ix_scopes_attempt" ON "scopes" ("attempt_id", "scope_kind");

-- attempts: Compact actual post-release attempt metadata. One attempt can span multiple game processes and loaded save continuations; gameplay RNG seeds are context, not primary identity.
CREATE TABLE "attempts" (
  "attempt_id" BLOB NOT NULL CHECK(length(attempt_id)=16),
  "parent_attempt_id" BLOB CHECK(parent_attempt_id IS NULL OR length(parent_attempt_id)=16),
  "initial_context_id" INTEGER NOT NULL ,
  "started_utc_ms" INTEGER NOT NULL ,
  "closed_utc_ms" INTEGER ,
  "status" TEXT NOT NULL CHECK(status IN ('open','won','lost','reset','interrupted','retired_unknown')),
  "terminal_summary_block_id" INTEGER ,
  "revision" BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY ("attempt_id"),
  CONSTRAINT "fk_attempts_1" FOREIGN KEY ("parent_attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_attempts_2" FOREIGN KEY ("initial_context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_attempts_3" FOREIGN KEY ("terminal_summary_block_id") REFERENCES "block_values" ("block_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_attempts_status_time" ON "attempts" ("status", "started_utc_ms");

-- aggregate_partitions: One family version within one scope, owning exactly one logical representation. Live scopes use hot rows; sufficiently closed/bounded attempt partitions can use an immutable base plus post-base overlays.
-- Hot partitions retain real SQL identity only while relational series need it.
CREATE TABLE hot_partitions (
 partition_id INTEGER PRIMARY KEY,
 family_id INTEGER NOT NULL REFERENCES aggregate_families(family_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 scope_id INTEGER NOT NULL REFERENCES scopes(scope_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 head_revision BLOB NOT NULL CHECK(length(head_revision)=8),
 UNIQUE(family_id,scope_id),
 UNIQUE(partition_id,family_id)
) STRICT;
CREATE INDEX ix_hot_partitions_scope ON hot_partitions(scope_id,family_id);

-- One immutable header may be shared by many closed family/scope partitions.
-- Assign a new publication to revise a partition; never mutate a shared header.
CREATE TABLE cold_publications (
 publication_id INTEGER PRIMARY KEY,
 head_revision BLOB NOT NULL CHECK(length(head_revision)=8),
 base_through_revision BLOB NOT NULL CHECK(length(base_through_revision)=8),
 CHECK(base_through_revision<=head_revision),
 UNIQUE(head_revision,base_through_revision)
) STRICT;
CREATE TRIGGER cold_publications_immutable BEFORE UPDATE ON cold_publications
 BEGIN SELECT RAISE(ABORT,'cold publication headers are immutable'); END;

CREATE TABLE cold_partitions (
 scope_id INTEGER NOT NULL REFERENCES scopes(scope_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 family_id INTEGER NOT NULL REFERENCES aggregate_families(family_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 base_block_id INTEGER NOT NULL REFERENCES block_values(block_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 publication_id INTEGER NOT NULL REFERENCES cold_publications(publication_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 PRIMARY KEY(scope_id,family_id)
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_cold_partitions_family ON cold_partitions(family_id,scope_id);
CREATE INDEX ix_cold_partitions_publication ON cold_partitions(publication_id);

-- Original global uniqueness spans the two physical representations. Both
-- indexed existence guards also run when parent natural keys are changed.
CREATE TRIGGER hot_partitions_exclusive_insert BEFORE INSERT ON hot_partitions
 WHEN EXISTS(SELECT 1 FROM cold_partitions WHERE scope_id=NEW.scope_id AND family_id=NEW.family_id)
 BEGIN SELECT RAISE(ABORT,'family/scope already has a cold partition'); END;
CREATE TRIGGER hot_partitions_exclusive_update BEFORE UPDATE OF scope_id,family_id ON hot_partitions
 WHEN EXISTS(SELECT 1 FROM cold_partitions WHERE scope_id=NEW.scope_id AND family_id=NEW.family_id)
 BEGIN SELECT RAISE(ABORT,'family/scope already has a cold partition'); END;
CREATE TRIGGER cold_partitions_exclusive_insert BEFORE INSERT ON cold_partitions
 WHEN EXISTS(SELECT 1 FROM hot_partitions WHERE scope_id=NEW.scope_id AND family_id=NEW.family_id)
 BEGIN SELECT RAISE(ABORT,'family/scope already has a hot partition'); END;
CREATE TRIGGER cold_partitions_exclusive_update BEFORE UPDATE OF scope_id,family_id ON cold_partitions
 WHEN EXISTS(SELECT 1 FROM hot_partitions WHERE scope_id=NEW.scope_id AND family_id=NEW.family_id)
 BEGIN SELECT RAISE(ABORT,'family/scope already has a hot partition'); END;

-- Compatibility read view. Closed identity is the exact scope/family pair;
-- NULL deliberately does not invent a global cold SQL partition identifier.
CREATE VIEW aggregate_partitions AS
 SELECT partition_id,family_id,scope_id,'hot' AS storage_mode,head_revision,
        NULL AS base_through_revision,NULL AS base_block_id FROM hot_partitions
 UNION ALL
 SELECT NULL AS partition_id,p.family_id,p.scope_id,'cold' AS storage_mode,
        h.head_revision,h.base_through_revision,p.base_block_id
 FROM cold_partitions p JOIN cold_publications h USING(publication_id);

-- aggregate_series: Observed full dimension tuple within a hot family/scope partition. No unobserved Cartesian combinations are inserted.
CREATE TABLE "aggregate_series" (
  "series_id" INTEGER NOT NULL ,
  "partition_id" INTEGER NOT NULL ,
  "family_id" INTEGER NOT NULL ,
  "storage_mode" TEXT NOT NULL CHECK(storage_mode='hot'),
  "tuple_id" INTEGER NOT NULL ,
  PRIMARY KEY ("series_id"),
  UNIQUE ("partition_id", "tuple_id"),
  UNIQUE ("series_id", "family_id"),
  CONSTRAINT "fk_aggregate_series_1" FOREIGN KEY ("partition_id", "family_id") REFERENCES "hot_partitions" ("partition_id", "family_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_aggregate_series_2" FOREIGN KEY ("tuple_id") REFERENCES "tuples" ("tuple_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;


-- aggregate_cells: One typed exact scalar measure for one observed series. Unknown/unsupported/not-applicable sample counts survive separately from known zero.
CREATE TABLE "aggregate_cells" (
  "series_id" INTEGER NOT NULL ,
  "family_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind='scalar'),
  "value_blob" ANY ,
  "known_count" ANY NOT NULL ,
  "unknown_count" ANY NOT NULL ,
  "unsupported_count" ANY NOT NULL ,
  "not_applicable_count" ANY NOT NULL ,
  "revision" BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY ("series_id", "measure_no"),
  CHECK (value_blob IS NULL OR typeof(value_blob)='integer' OR (typeof(value_blob)='blob' AND length(value_blob)>=10)),
  CHECK ((typeof(known_count)='integer' AND known_count>=0) OR (typeof(known_count)='blob' AND length(known_count)>=10)),
  CHECK ((typeof(unknown_count)='integer' AND unknown_count>=0) OR (typeof(unknown_count)='blob' AND length(unknown_count)>=10)),
  CHECK ((typeof(unsupported_count)='integer' AND unsupported_count>=0) OR (typeof(unsupported_count)='blob' AND length(unsupported_count)>=10)),
  CHECK ((typeof(not_applicable_count)='integer' AND not_applicable_count>=0) OR (typeof(not_applicable_count)='blob' AND length(not_applicable_count)>=10)),
  CHECK ((typeof(known_count)='integer' AND known_count=0 AND value_blob IS NULL) OR ((typeof(known_count)='integer' AND known_count>0 OR typeof(known_count)='blob') AND value_blob IS NOT NULL)),
  CONSTRAINT "fk_aggregate_cells_1" FOREIGN KEY ("series_id", "family_id") REFERENCES "aggregate_series" ("series_id", "family_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_aggregate_cells_2" FOREIGN KEY ("family_id", "measure_no", "measure_kind") REFERENCES "family_measures" ("family_id", "measure_no", "measure_kind") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


-- set_members: Exact membership for a declared set measure under the same complete dimensions. Counts/cardinality alone do not replace these rows.
CREATE TABLE "set_members" (
  "series_id" INTEGER NOT NULL ,
  "family_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind='exact_set'),
  "member_identity_id" INTEGER NOT NULL ,
  PRIMARY KEY ("series_id", "measure_no", "member_identity_id"),
  CONSTRAINT "fk_set_members_1" FOREIGN KEY ("series_id", "family_id") REFERENCES "aggregate_series" ("series_id", "family_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_set_members_2" FOREIGN KEY ("family_id", "measure_no", "measure_kind") REFERENCES "family_measures" ("family_id", "measure_no", "measure_kind") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_set_members_3" FOREIGN KEY ("member_identity_id") REFERENCES "identity_values" ("identity_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_set_members_identity" ON "set_members" ("member_identity_id", "series_id");

-- aggregate_overlays: Late finalized projection deltas for a cold partition. These contain sufficient generic aggregate changes, not a permanent gameplay event archive.
-- Late finalized deltas retain the exact closed natural key and full u64 range.
CREATE TABLE cold_overlays (
 scope_id INTEGER NOT NULL,
 family_id INTEGER NOT NULL,
 after_revision BLOB NOT NULL CHECK(length(after_revision)=8),
 through_revision BLOB NOT NULL CHECK(length(through_revision)=8),
 block_id INTEGER NOT NULL REFERENCES block_values(block_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 PRIMARY KEY(scope_id,family_id,through_revision),
 CHECK(after_revision<through_revision),
 FOREIGN KEY(scope_id,family_id) REFERENCES cold_partitions(scope_id,family_id) ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_cold_overlays_prefix ON cold_overlays(scope_id,family_id,after_revision);
CREATE VIEW aggregate_overlays AS
 SELECT NULL AS partition_id,'cold' AS storage_mode,after_revision,through_revision,block_id,scope_id,family_id
 FROM cold_overlays;

-- ordered_state: Durable complete sufficient state for current/streak/transient reducer families. Survives reconnect/reload when the actual counting rule says so, independently of replaceable technical live context.
CREATE TABLE "ordered_state" (
  "family_id" INTEGER NOT NULL ,
  "scope_id" INTEGER NOT NULL ,
  "tuple_id" INTEGER NOT NULL ,
  "context_id" INTEGER ,
  "format_version" INTEGER NOT NULL CHECK(format_version>0),
  "state_codec" TEXT NOT NULL ,
  "state_blob" BLOB NOT NULL ,
  "last_stream_id" BLOB NOT NULL CHECK(length(last_stream_id)=16),
  "last_sequence" BLOB NOT NULL CHECK(length(last_sequence)=8),
  "revision" BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY ("family_id", "scope_id", "tuple_id"),
  CONSTRAINT "fk_ordered_state_1" FOREIGN KEY ("family_id") REFERENCES "aggregate_families" ("family_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_ordered_state_2" FOREIGN KEY ("scope_id") REFERENCES "scopes" ("scope_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_ordered_state_3" FOREIGN KEY ("tuple_id") REFERENCES "tuples" ("tuple_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_ordered_state_4" FOREIGN KEY ("context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_ordered_state_5" FOREIGN KEY ("last_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_ordered_state_scope" ON "ordered_state" ("scope_id", "family_id");

-- immutable_blocks: One common reusable immutable block store for owned long-tail contexts, cold attempt blocks and causal payloads. No duplicate owned-only copy.
CREATE TABLE block_kinds (
 block_kind_id INTEGER PRIMARY KEY,
 block_kind TEXT NOT NULL UNIQUE
) STRICT;
CREATE TABLE block_values (
 block_id INTEGER PRIMARY KEY,
 block_kind_id INTEGER NOT NULL REFERENCES block_kinds(block_kind_id) ON DELETE RESTRICT ON UPDATE RESTRICT,
 format_version INTEGER NOT NULL CHECK(format_version>0),
 codec TEXT NOT NULL CHECK(codec IN ('raw','zlib')),
 uncompressed_length INTEGER NOT NULL CHECK(uncompressed_length>=0),
 digest BLOB NOT NULL CHECK(length(digest)=32),
 payload BLOB NOT NULL,
 CHECK(codec<>'raw' OR length(payload)=uncompressed_length)
) STRICT;
CREATE INDEX ix_block_values_lookup ON block_values(block_kind_id,format_version,substr(digest,1,8),uncompressed_length);
CREATE VIEW immutable_blocks AS
 SELECT b.block_id,k.block_kind,b.format_version,b.codec,b.uncompressed_length,b.digest,b.payload
 FROM block_values b JOIN block_kinds k USING(block_kind_id);

-- stat_vectors: Intern exact six-value vectors used by generated base stats, reference base stats, permanent computed stats, raw IVs, effective IVs and EVs.
CREATE TABLE stat_vectors (stat_vector_id INTEGER PRIMARY KEY, hp INTEGER NOT NULL CHECK(hp>=0), attack INTEGER NOT NULL CHECK(attack>=0), defense INTEGER NOT NULL CHECK(defense>=0), special_attack INTEGER NOT NULL CHECK(special_attack>=0), special_defense INTEGER NOT NULL CHECK(special_defense>=0), speed INTEGER NOT NULL CHECK(speed>=0), UNIQUE(hp,attack,defense,special_attack,special_defense,speed)) STRICT;

-- movesets: One immutable ordered simultaneous 0â€“4-slot moveset, shared by identical actual configurations.
CREATE TABLE movesets (moveset_id INTEGER PRIMARY KEY, slot_count INTEGER NOT NULL CHECK(slot_count BETWEEN 0 AND 4), digest BLOB NOT NULL CHECK(length(digest)=32)) STRICT;
CREATE INDEX ix_movesets_digest ON movesets(digest,slot_count);

-- moveset_slots: Explicit move columns make actual simultaneous moves readable and queryable without decoding a BLOB.
CREATE TABLE moveset_slots (moveset_id INTEGER NOT NULL REFERENCES movesets(moveset_id) ON DELETE RESTRICT, slot_no INTEGER NOT NULL CHECK(slot_no BETWEEN 0 AND 3), move_identity_id INTEGER NOT NULL REFERENCES identity_values(identity_id) ON DELETE RESTRICT, max_pp INTEGER NOT NULL CHECK(max_pp>=0), pp_up_count INTEGER CHECK(pp_up_count BETWEEN 0 AND 3), PRIMARY KEY(moveset_id,slot_no)) STRICT, WITHOUT ROWID;
CREATE INDEX ix_moveset_slots_move ON moveset_slots(move_identity_id,moveset_id);

-- owned_individuals: One actually retained logical team individual within one authoritative attempt; not one row for every temporary catch/fusion ingredient.
CREATE TABLE owned_individuals (individual_id INTEGER PRIMARY KEY, attempt_id BLOB NOT NULL REFERENCES attempts(attempt_id) ON DELETE RESTRICT CHECK(length(attempt_id)=16), entity_key BLOB NOT NULL CHECK(length(entity_key)>0), first_recorded_revision BLOB NOT NULL CHECK(length(first_recorded_revision)=8 AND first_recorded_revision>=X'0000000000000001'), UNIQUE(attempt_id,entity_key)) STRICT;

-- configuration_revisions: One meaningful final persistent gameplay configuration of an actual retained individual. Ordinary HP loss, PP ticks and no-op recalculations do not append rows.
CREATE TABLE configuration_revisions (configuration_id INTEGER PRIMARY KEY, individual_id INTEGER NOT NULL REFERENCES owned_individuals(individual_id) ON DELETE RESTRICT, revision_no INTEGER NOT NULL CHECK(revision_no>=1), previous_revision_no INTEGER CHECK(previous_revision_no IS NULL OR (previous_revision_no>=1 AND previous_revision_no<revision_no)), play_branch_identity_id INTEGER NOT NULL REFERENCES identity_values(identity_id) ON DELETE RESTRICT, source_stream_id BLOB NOT NULL REFERENCES producer_streams(stream_id) ON DELETE RESTRICT CHECK(length(source_stream_id)=16), source_sequence BLOB NOT NULL CHECK(length(source_sequence)=8), recorded_at_revision BLOB NOT NULL CHECK(length(recorded_at_revision)=8 AND recorded_at_revision>=X'0000000000000001'), capture_kind TEXT NOT NULL CHECK(capture_kind IN ('acquisition','configuration_change','runner_entry','runner_exit','terminal')), ownership_role TEXT NOT NULL CHECK(ownership_role IN ('runner','utility','egg','other_retained_team')), retained_after_commit INTEGER NOT NULL CHECK(retained_after_commit IN (0,1)), species_identity_id INTEGER NOT NULL REFERENCES identity_values(identity_id) ON DELETE RESTRICT, form_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, head_species_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, body_species_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, level INTEGER NOT NULL CHECK(level>=0), experience INTEGER NOT NULL CHECK(experience>=0), generated_stats_id INTEGER NOT NULL REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, reference_stats_id INTEGER REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, computed_stats_id INTEGER NOT NULL REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, raw_iv_id INTEGER REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, effective_iv_id INTEGER REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, ev_id INTEGER REFERENCES stat_vectors(stat_vector_id) ON DELETE RESTRICT, moveset_id INTEGER NOT NULL REFERENCES movesets(moveset_id) ON DELETE RESTRICT, ability_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, nature_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, effective_nature_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, held_item_identity_id INTEGER REFERENCES identity_values(identity_id) ON DELETE RESTRICT, favorite_context_id INTEGER REFERENCES contexts(context_id) ON DELETE RESTRICT, detail_block_id INTEGER NOT NULL REFERENCES block_values(block_id) ON DELETE RESTRICT, checkpoint_block_id INTEGER REFERENCES block_values(block_id) ON DELETE RESTRICT, UNIQUE(individual_id,revision_no), UNIQUE(source_stream_id,source_sequence,individual_id), FOREIGN KEY(individual_id,previous_revision_no) REFERENCES configuration_revisions(individual_id,revision_no) ON DELETE RESTRICT) STRICT;
CREATE INDEX ix_configuration_species ON configuration_revisions(species_identity_id,individual_id);
CREATE INDEX ix_configuration_detail_block ON configuration_revisions(detail_block_id);

-- owned_lineage_edges: Exact retained transformation ancestry without treating temporary ingredients as actual owned team records.
CREATE TABLE owned_lineage_edges (child_configuration_id INTEGER NOT NULL REFERENCES configuration_revisions(configuration_id) ON DELETE RESTRICT, role TEXT NOT NULL CHECK(role IN ('continuation','previous_runner','fusion_head','fusion_body','evolution_input','other')), ordinal INTEGER NOT NULL CHECK(ordinal>=0), source_configuration_id INTEGER REFERENCES configuration_revisions(configuration_id) ON DELETE RESTRICT, source_context_id INTEGER REFERENCES contexts(context_id) ON DELETE RESTRICT, transaction_context_id INTEGER NOT NULL REFERENCES contexts(context_id) ON DELETE RESTRICT, PRIMARY KEY(child_configuration_id,role,ordinal), CHECK((source_configuration_id IS NOT NULL AND source_context_id IS NULL) OR (source_configuration_id IS NULL AND source_context_id IS NOT NULL)), CHECK(source_configuration_id IS NULL OR source_configuration_id<>child_configuration_id)) STRICT, WITHOUT ROWID;
CREATE INDEX ix_owned_lineage_source ON owned_lineage_edges(source_configuration_id,child_configuration_id);

-- private_consumers: One opaque encrypted achievement consumer checkpoint, logically separate from readable general collection tables in the same SQLite transaction domain.
CREATE TABLE private_consumers (consumer_key BLOB NOT NULL PRIMARY KEY CHECK(length(consumer_key)=16), pack_binding_token BLOB NOT NULL CHECK(length(pack_binding_token)=32), state_revision INTEGER NOT NULL CHECK(state_revision>=0), evaluated_through_revision BLOB NOT NULL CHECK(length(evaluated_through_revision)=8), cipher_format_version INTEGER NOT NULL CHECK(cipher_format_version>=1), key_slot INTEGER NOT NULL CHECK(key_slot>=1), nonce BLOB NOT NULL CHECK(length(nonce)=12), auth_tag BLOB NOT NULL CHECK(length(auth_tag)=16), ciphertext BLOB NOT NULL) STRICT, WITHOUT ROWID;

-- consumer_dirty: Generic changed scopes awaiting a registered opaque consumer; no measurement/rule names or thresholds are stored here.
CREATE TABLE consumer_dirty (consumer_key BLOB NOT NULL REFERENCES private_consumers(consumer_key) ON DELETE RESTRICT, scope_id INTEGER NOT NULL REFERENCES scopes(scope_id) ON DELETE RESTRICT, first_revision BLOB NOT NULL CHECK(length(first_revision)=8 AND first_revision>=X'0000000000000001'), latest_revision BLOB NOT NULL CHECK(length(latest_revision)=8 AND latest_revision>=first_revision), PRIMARY KEY(consumer_key,scope_id)) STRICT, WITHOUT ROWID;
CREATE INDEX ix_consumer_dirty_revision ON consumer_dirty(consumer_key,latest_revision,scope_id);

-- private_notice_outbox: Durable encrypted unlock popup queue; game disconnect does not lose an unlock or its pending notification.
CREATE TABLE private_notice_outbox (notification_id BLOB NOT NULL PRIMARY KEY CHECK(length(notification_id)=16), consumer_key BLOB NOT NULL REFERENCES private_consumers(consumer_key) ON DELETE RESTRICT, created_at_revision BLOB NOT NULL CHECK(length(created_at_revision)=8 AND created_at_revision>=X'0000000000000001'), cipher_format_version INTEGER NOT NULL CHECK(cipher_format_version>=1), key_slot INTEGER NOT NULL CHECK(key_slot>=1), nonce BLOB NOT NULL CHECK(length(nonce)=12), auth_tag BLOB NOT NULL CHECK(length(auth_tag)=16), ciphertext BLOB NOT NULL) STRICT, WITHOUT ROWID;
CREATE INDEX ix_private_notice_delivery ON private_notice_outbox(consumer_key,created_at_revision,notification_id);

-- producer_lineages: One durable, externally locked source authority in this collection. Gameplay and tracker-local preferences are separate sources.
CREATE TABLE "producer_lineages" (
  "lineage_id" BLOB NOT NULL  CHECK(lineage_id IS NULL OR length(lineage_id)=16),
  "meta_id" INTEGER NOT NULL  CHECK(meta_id=1),
  "source_kind" TEXT NOT NULL  CHECK(source_kind IN ('gameplay','tracker_preferences')),
  "created_utc_ms" INTEGER NOT NULL ,
  PRIMARY KEY ("lineage_id"),
  CONSTRAINT "fk_producer_lineages_1" FOREIGN KEY ("meta_id") REFERENCES "collection_meta" ("meta_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


-- producer_streams: Immutable descriptor for one producer boot/incarnation. Reconnect retains the stream; process/collector restart creates a new stream.
CREATE TABLE "producer_streams" (
  "stream_id" BLOB NOT NULL  CHECK(stream_id IS NULL OR length(stream_id)=16),
  "lineage_id" BLOB NOT NULL  CHECK(lineage_id IS NULL OR length(lineage_id)=16),
  "session_instance_id" BLOB NOT NULL  CHECK(session_instance_id IS NULL OR length(session_instance_id)=16),
  "descriptor_version" INTEGER NOT NULL  CHECK(descriptor_version>0),
  "descriptor_digest" BLOB NOT NULL  CHECK(length(descriptor_digest)=32),
  "started_utc_ms" INTEGER ,
  "registered_utc_ms" INTEGER NOT NULL ,
  "stream_state" TEXT NOT NULL  CHECK(stream_state IN ('open','retiring','retired')),
  PRIMARY KEY ("stream_id"),
  CONSTRAINT "fk_producer_streams_1" FOREIGN KEY ("lineage_id") REFERENCES "producer_lineages" ("lineage_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE UNIQUE INDEX "ux_stream_lineage_identity" ON "producer_streams" ("lineage_id", "stream_id");
CREATE UNIQUE INDEX "ux_one_open_stream_per_lineage" ON "producer_streams" ("lineage_id") WHERE stream_state='open';

-- stream_receipts: One compact durable prefix per stream; updated in the same commit as projections, causal state and dirty revision. No per-event ledger.
CREATE TABLE "stream_receipts" (
  "stream_id" BLOB NOT NULL  CHECK(stream_id IS NULL OR length(stream_id)=16),
  "disposed_through_sequence" BLOB NOT NULL  CHECK(disposed_through_sequence IS NULL OR length(disposed_through_sequence)=8),
  "store_revision" BLOB NOT NULL  CHECK(store_revision IS NULL OR length(store_revision)=8),
  "committed_utc_ms" INTEGER NOT NULL ,
  PRIMARY KEY ("stream_id"),
  CONSTRAINT "fk_stream_receipts_1" FOREIGN KEY ("stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


-- stream_retirements: One persisted retirement/context fence per predecessor stream. Pending settlement reconciles recovered local tail against tracker receipts before successor admission.
CREATE TABLE "stream_retirements" (
  "stream_id" BLOB NOT NULL  CHECK(stream_id IS NULL OR length(stream_id)=16),
  "retirement_fence_id" BLOB NOT NULL  CHECK(retirement_fence_id IS NULL OR length(retirement_fence_id)=16),
  "source_recovered_prefix" BLOB NOT NULL  CHECK(source_recovered_prefix IS NULL OR length(source_recovered_prefix)=8),
  "required_frontier" BLOB NOT NULL  CHECK(required_frontier IS NULL OR length(required_frontier)=8),
  "tracker_prefix_at_settlement" BLOB  CHECK(tracker_prefix_at_settlement IS NULL OR length(tracker_prefix_at_settlement)=8),
  "settled_frontier" BLOB  CHECK(settled_frontier IS NULL OR length(settled_frontier)=8),
  "retirement_kind" TEXT NOT NULL  CHECK(retirement_kind IN ('clean','recovered_interrupted','gap')),
  "settlement_state" TEXT NOT NULL  CHECK(settlement_state IN ('pending','settled')),
  "closure_format_version" INTEGER NOT NULL  CHECK(closure_format_version>0),
  "closure_codec" TEXT NOT NULL ,
  "closure_payload" BLOB NOT NULL ,
  "settled_utc_ms" INTEGER ,
  PRIMARY KEY ("stream_id"),
  CHECK (settlement_state<>'settled' OR (settled_frontier IS NOT NULL AND tracker_prefix_at_settlement IS NOT NULL AND settled_utc_ms IS NOT NULL)),
  CHECK (settled_frontier IS NULL OR settled_frontier>=tracker_prefix_at_settlement),
  CHECK (settled_frontier IS NULL OR settled_frontier>=required_frontier),
  CHECK (settled_frontier IS NULL OR settled_frontier>=source_recovered_prefix),
  CONSTRAINT "fk_stream_retirements_1" FOREIGN KEY ("stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE UNIQUE INDEX "ux_retirement_fence" ON "stream_retirements" ("retirement_fence_id");
CREATE UNIQUE INDEX "ux_retirement_stream_fence" ON "stream_retirements" ("stream_id", "retirement_fence_id");

-- stream_dependencies: A successor cannot be applied until this same-lineage predecessor retirement/context fence is durably settled.
CREATE TABLE "stream_dependencies" (
  "successor_stream_id" BLOB NOT NULL  CHECK(successor_stream_id IS NULL OR length(successor_stream_id)=16),
  "lineage_id" BLOB NOT NULL  CHECK(lineage_id IS NULL OR length(lineage_id)=16),
  "predecessor_stream_id" BLOB NOT NULL  CHECK(predecessor_stream_id IS NULL OR length(predecessor_stream_id)=16),
  "retirement_fence_id" BLOB NOT NULL  CHECK(retirement_fence_id IS NULL OR length(retirement_fence_id)=16),
  "required_source_prefix" BLOB NOT NULL  CHECK(required_source_prefix IS NULL OR length(required_source_prefix)=8),
  "admission_state" TEXT NOT NULL  CHECK(admission_state IN ('waiting','admitted')),
  "admitted_utc_ms" INTEGER ,
  PRIMARY KEY ("successor_stream_id"),
  CHECK (successor_stream_id<>predecessor_stream_id),
  CHECK (admission_state<>'admitted' OR admitted_utc_ms IS NOT NULL),
  CONSTRAINT "fk_stream_dependencies_1" FOREIGN KEY ("lineage_id", "successor_stream_id") REFERENCES "producer_streams" ("lineage_id", "stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_stream_dependencies_2" FOREIGN KEY ("lineage_id", "predecessor_stream_id") REFERENCES "producer_streams" ("lineage_id", "stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_stream_dependencies_3" FOREIGN KEY ("predecessor_stream_id", "retirement_fence_id") REFERENCES "stream_retirements" ("stream_id", "retirement_fence_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE UNIQUE INDEX "ux_one_successor" ON "stream_dependencies" ("predecessor_stream_id");
CREATE INDEX "ix_waiting_dependencies" ON "stream_dependencies" ("admission_state", "successor_stream_id");

-- live_contexts: Replaceable technical engine-current snapshots for a world/load incarnation. These are not earned same-attempt streak state.
CREATE TABLE "live_contexts" (
  "live_context_id" BLOB NOT NULL  CHECK(live_context_id IS NULL OR length(live_context_id)=16),
  "lineage_id" BLOB NOT NULL  CHECK(lineage_id IS NULL OR length(lineage_id)=16),
  "attempt_id" BLOB  CHECK(attempt_id IS NULL OR length(attempt_id)=16),
  "load_incarnation_id" BLOB  CHECK(load_incarnation_id IS NULL OR length(load_incarnation_id)=16),
  "context_kind" TEXT NOT NULL ,
  "context_state" TEXT NOT NULL  CHECK(context_state IN ('active','closing','interrupted')),
  "format_version" INTEGER NOT NULL  CHECK(format_version>0),
  "codec" TEXT NOT NULL ,
  "payload" BLOB NOT NULL ,
  "last_stream_id" BLOB NOT NULL  CHECK(last_stream_id IS NULL OR length(last_stream_id)=16),
  "last_sequence" BLOB NOT NULL  CHECK(last_sequence IS NULL OR length(last_sequence)=8),
  PRIMARY KEY ("live_context_id"),
  CONSTRAINT "fk_live_contexts_1" FOREIGN KEY ("lineage_id") REFERENCES "producer_lineages" ("lineage_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_live_contexts_2" FOREIGN KEY ("attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_live_contexts_3" FOREIGN KEY ("last_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE UNIQUE INDEX "ux_active_context" ON "live_contexts" ("lineage_id", "context_kind") WHERE context_state='active';
CREATE INDEX "ix_live_context_attempt" ON "live_contexts" ("attempt_id");

-- causal_work: Durable minimal unfinalized action, effect, hit, battle, status episode, resource-lot or transition work. Retains joins needed after input ACK.
CREATE TABLE "causal_work" (
  "work_id" BLOB NOT NULL  CHECK(work_id IS NULL OR length(work_id)=16),
  "live_context_id" BLOB NOT NULL  CHECK(live_context_id IS NULL OR length(live_context_id)=16),
  "work_kind" TEXT NOT NULL ,
  "canonical_group_key" BLOB NOT NULL ,
  "work_state" TEXT NOT NULL  CHECK(work_state IN ('open','closing','interrupted')),
  "projection_version" INTEGER NOT NULL  CHECK(projection_version>0),
  "format_version" INTEGER NOT NULL  CHECK(format_version>0),
  "codec" TEXT NOT NULL ,
  "payload" BLOB NOT NULL ,
  "uncompressed_length" INTEGER NOT NULL  CHECK(uncompressed_length>=0),
  "opened_stream_id" BLOB NOT NULL  CHECK(opened_stream_id IS NULL OR length(opened_stream_id)=16),
  "opened_sequence" BLOB NOT NULL  CHECK(opened_sequence IS NULL OR length(opened_sequence)=8),
  "last_stream_id" BLOB NOT NULL  CHECK(last_stream_id IS NULL OR length(last_stream_id)=16),
  "last_sequence" BLOB NOT NULL  CHECK(last_sequence IS NULL OR length(last_sequence)=8),
  PRIMARY KEY ("work_id"),
  CONSTRAINT "fk_causal_work_1" FOREIGN KEY ("live_context_id") REFERENCES "live_contexts" ("live_context_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_causal_work_2" FOREIGN KEY ("opened_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_causal_work_3" FOREIGN KEY ("last_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE UNIQUE INDEX "ux_causal_group" ON "causal_work" ("live_context_id", "work_kind", "canonical_group_key");
CREATE INDEX "ix_causal_open_context" ON "causal_work" ("live_context_id", "work_state");

-- causal_work_links: Actual SQL relationships between simultaneously retained unresolved groups. Avoid dangling references when an action closes before its dependent episode/battle.
CREATE TABLE "causal_work_links" (
  "work_id" BLOB NOT NULL  CHECK(work_id IS NULL OR length(work_id)=16),
  "requires_work_id" BLOB NOT NULL  CHECK(requires_work_id IS NULL OR length(requires_work_id)=16),
  "relation_kind" TEXT NOT NULL ,
  PRIMARY KEY ("work_id", "requires_work_id", "relation_kind"),
  CHECK (work_id<>requires_work_id),
  CONSTRAINT "fk_causal_work_links_1" FOREIGN KEY ("work_id") REFERENCES "causal_work" ("work_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_causal_work_links_2" FOREIGN KEY ("requires_work_id") REFERENCES "causal_work" ("work_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_causal_required_by" ON "causal_work_links" ("requires_work_id", "work_id");

-- coverage_gaps: Explicit incomplete evidence intervals. Unknown counts stay unknown; collection never silently invents zero or successful history.
CREATE TABLE "coverage_gaps" (
  "gap_id" BLOB NOT NULL  CHECK(gap_id IS NULL OR length(gap_id)=16),
  "scope_id" INTEGER NOT NULL ,
  "stream_id" BLOB  CHECK(stream_id IS NULL OR length(stream_id)=16),
  "first_sequence" BLOB  CHECK(first_sequence IS NULL OR length(first_sequence)=8),
  "last_sequence" BLOB  CHECK(last_sequence IS NULL OR length(last_sequence)=8),
  "reason" TEXT NOT NULL ,
  "disposition_state" TEXT NOT NULL  CHECK(disposition_state IN ('open','disposed')),
  "opened_utc_ms" INTEGER ,
  "closed_utc_ms" INTEGER ,
  "unknown_tail" INTEGER NOT NULL  CHECK(unknown_tail IN (0,1)),
  "detail_version" INTEGER NOT NULL  CHECK(detail_version>0),
  "detail_payload" BLOB NOT NULL ,
  PRIMARY KEY ("gap_id"),
  CHECK (last_sequence IS NULL OR first_sequence IS NOT NULL),
  CHECK (first_sequence IS NULL OR last_sequence IS NULL OR first_sequence<=last_sequence),
  CONSTRAINT "fk_coverage_gaps_1" FOREIGN KEY ("scope_id") REFERENCES "scopes" ("scope_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_coverage_gaps_2" FOREIGN KEY ("stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_coverage_scope" ON "coverage_gaps" ("scope_id", "reason");
CREATE INDEX "ix_coverage_stream_range" ON "coverage_gaps" ("stream_id", "first_sequence", "last_sequence");

-- coverage_gap_families: Which projections have affected completeness/availability; profile-wide gaps can list all applicable families.
CREATE TABLE "coverage_gap_families" (
  "gap_id" BLOB NOT NULL  CHECK(gap_id IS NULL OR length(gap_id)=16),
  "family_id" INTEGER NOT NULL ,
  PRIMARY KEY ("gap_id", "family_id"),
  CONSTRAINT "fk_coverage_gap_families_1" FOREIGN KEY ("gap_id") REFERENCES "coverage_gaps" ("gap_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_coverage_gap_families_2" FOREIGN KEY ("family_id") REFERENCES "aggregate_families" ("family_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_family_gaps" ON "coverage_gap_families" ("family_id", "gap_id");

-- retirement_gaps: Explicit gap decisions that justify retiring an unrecoverable predecessor and admitting its successor.
CREATE TABLE "retirement_gaps" (
  "stream_id" BLOB NOT NULL  CHECK(stream_id IS NULL OR length(stream_id)=16),
  "gap_id" BLOB NOT NULL  CHECK(gap_id IS NULL OR length(gap_id)=16),
  PRIMARY KEY ("stream_id", "gap_id"),
  CONSTRAINT "fk_retirement_gaps_1" FOREIGN KEY ("stream_id") REFERENCES "stream_retirements" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_retirement_gaps_2" FOREIGN KEY ("gap_id") REFERENCES "coverage_gaps" ("gap_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_gap_retirements" ON "retirement_gaps" ("gap_id", "stream_id");

-- Compact numeric codec: signed/unsigned SQLite INTEGER when it fits signed64.
-- Larger exact values use canonical signed/unsigned varint BLOB; readback validates
-- canonical overflow form. All other 37-table declarations/proof FKs remain intact.

-- Shared typed atoms and exact short summary keys. No consumer rules are installed.
-- archive_units remains empty: synthetic witnesses use attempt/stream, not fake archive facts.
CREATE TABLE "archive_units" (
  "unit_id" INTEGER NOT NULL ,
  "unit_kind" TEXT NOT NULL CHECK(unit_kind IN ('battle_projection','attempt_projection','individual_history','profile_projection','lineage','context_bundle')),
  "semantic_scope_key" BLOB NOT NULL ,
  "representation_kind" TEXT NOT NULL CHECK(representation_kind IN ('closed_evidence','current_projection_page','context_bundle')),
  "component_kind" TEXT NOT NULL ,
  "segment_ordinal" INTEGER NOT NULL CHECK(segment_ordinal>=0),
  "generation" INTEGER NOT NULL CHECK(generation>0),
  "is_current_generation" INTEGER NOT NULL CHECK(is_current_generation IN (0,1)),
  "logical_format_version" INTEGER NOT NULL CHECK(logical_format_version>0),
  "namespace_policy_version" INTEGER NOT NULL CHECK(namespace_policy_version>0),
  "attempt_id" BLOB CHECK(attempt_id IS NULL OR length(attempt_id)=16),
  "parent_unit_id" INTEGER ,
  "primary_context_id" INTEGER ,
  "decoded_length" INTEGER NOT NULL CHECK(decoded_length>=0 AND decoded_length<=262144),
  "decoded_digest" BLOB NOT NULL CHECK(decoded_digest IS NOT NULL AND length(decoded_digest)=32),
  "chunk_count" INTEGER NOT NULL CHECK(chunk_count>0 AND chunk_count<=64),
  "published_at_revision" BLOB NOT NULL CHECK(published_at_revision IS NOT NULL AND length(published_at_revision)=8),
  PRIMARY KEY ("unit_id"),
  UNIQUE ("unit_id", "attempt_id"),
  UNIQUE ("unit_kind", "semantic_scope_key", "component_kind", "segment_ordinal", "generation"),
  CHECK (parent_unit_id IS NULL OR parent_unit_id<>unit_id),
  CONSTRAINT "fk_archive_units_1" FOREIGN KEY ("attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_archive_units_2" FOREIGN KEY ("parent_unit_id") REFERENCES "archive_units" ("unit_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_archive_units_3" FOREIGN KEY ("parent_unit_id", "attempt_id") REFERENCES "archive_units" ("unit_id", "attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_archive_units_4" FOREIGN KEY ("primary_context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;
CREATE INDEX "ix_archive_units_owner" ON "archive_units" ("unit_kind", "semantic_scope_key", "component_kind", "segment_ordinal");
CREATE INDEX "ix_archive_units_attempt" ON "archive_units" ("attempt_id", "unit_kind", "segment_ordinal");
CREATE INDEX "ix_archive_units_parent" ON "archive_units" ("parent_unit_id");
CREATE INDEX "ix_archive_units_context" ON "archive_units" ("primary_context_id");
CREATE INDEX "ix_archive_units_content" ON "archive_units" ("unit_kind", "logical_format_version", "decoded_digest", "decoded_length");
CREATE UNIQUE INDEX "ux_archive_units_current_semantic_page" ON "archive_units" ("unit_kind", "semantic_scope_key", "component_kind", "segment_ordinal") WHERE is_current_generation=1;

CREATE TABLE "direct_groups" (
  "group_id" INTEGER NOT NULL ,
  "projection_key" TEXT NOT NULL ,
  "projection_version" INTEGER NOT NULL CHECK(projection_version>0),
  "variant_key" TEXT NOT NULL ,
  "shape" TEXT NOT NULL CHECK(shape IN ('counter','exact_set','maximum','joint_summary','distribution','ordered_state','current_state')),
  "definition_digest" BLOB NOT NULL CHECK(length(definition_digest)=32),
  "definition_json" TEXT NOT NULL ,
  "numeric_codec_version" INTEGER NOT NULL CHECK(numeric_codec_version=1),
  PRIMARY KEY ("group_id"),
  UNIQUE ("projection_key", "projection_version", "variant_key")
) STRICT;
CREATE INDEX "ix_h_direct_group_digest" ON "direct_groups" ("definition_digest");

CREATE TABLE "direct_group_sources" (
  "group_id" INTEGER NOT NULL ,
  "family_id" INTEGER NOT NULL ,
  PRIMARY KEY ("group_id", "family_id"),
  CONSTRAINT "fk_direct_group_sources_1" FOREIGN KEY ("group_id") REFERENCES "direct_groups" ("group_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_group_sources_2" FOREIGN KEY ("family_id") REFERENCES "aggregate_families" ("family_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_h_group_source_family" ON "direct_group_sources" ("family_id", "group_id");

CREATE TABLE "direct_dimensions" (
  "group_id" INTEGER NOT NULL ,
  "dimension_no" INTEGER NOT NULL CHECK(dimension_no>=0),
  "dimension_key" TEXT NOT NULL ,
  "value_kind" TEXT NOT NULL CHECK(value_kind IN ('integer','text','native_id','boolean','uint64','rational','set')),
  "meaning" TEXT NOT NULL ,
  "required" INTEGER NOT NULL CHECK(required IN (0,1)),
  PRIMARY KEY ("group_id", "dimension_no"),
  UNIQUE ("group_id", "dimension_key"),
  UNIQUE ("group_id", "dimension_no", "value_kind"),
  CONSTRAINT "fk_direct_dimensions_1" FOREIGN KEY ("group_id") REFERENCES "direct_groups" ("group_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_measures" (
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL CHECK(measure_no>=0),
  "measure_key" TEXT NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind IN ('scalar','exact_set','rational_extremum')),
  "value_domain" TEXT NOT NULL CHECK(value_domain IN ('nonnegative_integer','signed_integer','native_membership','nonnegative_rational')),
  "merge_operator" TEXT NOT NULL CHECK(merge_operator IN ('sum','min','max','union','ordered_replace','min_ratio','max_ratio')),
  "unit" TEXT NOT NULL ,
  "meaning" TEXT NOT NULL ,
  PRIMARY KEY ("group_id", "measure_no"),
  UNIQUE ("group_id", "measure_key"),
  UNIQUE ("group_id", "measure_no", "measure_kind"),
  UNIQUE ("group_id", "measure_no", "measure_kind", "value_domain"),
  CHECK ((measure_kind='exact_set' AND value_domain='native_membership' AND merge_operator='union') OR (measure_kind='rational_extremum' AND value_domain='nonnegative_rational' AND merge_operator IN ('min_ratio','max_ratio')) OR (measure_kind='scalar' AND value_domain IN ('nonnegative_integer','signed_integer') AND merge_operator IN ('sum','min','max','ordered_replace'))),
  CONSTRAINT "fk_direct_measures_1" FOREIGN KEY ("group_id") REFERENCES "direct_groups" ("group_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_contexts" (
  "direct_context_id" INTEGER NOT NULL ,
  "meta_id" INTEGER NOT NULL CHECK(meta_id=1),
  "context_kind" TEXT NOT NULL CHECK(context_kind IN ('lifetime','profile','active_attempt','active_battle','active_individual')),
  "scope_id" INTEGER NOT NULL ,
  "canonical_key" BLOB NOT NULL ,
  "context_state" TEXT NOT NULL CHECK(context_state IN ('active','draining')),
  PRIMARY KEY ("direct_context_id"),
  UNIQUE ("context_kind", "canonical_key"),
  CONSTRAINT "fk_direct_contexts_1" FOREIGN KEY ("meta_id") REFERENCES "collection_meta" ("meta_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_contexts_2" FOREIGN KEY ("scope_id") REFERENCES "scopes" ("scope_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;
CREATE INDEX "ix_h_direct_context_scope" ON "direct_contexts" ("scope_id", "context_state");


CREATE TABLE direct_series (
 series_id INTEGER PRIMARY KEY,
 group_id INTEGER NOT NULL REFERENCES direct_groups(group_id) ON DELETE RESTRICT,
 direct_context_id INTEGER NOT NULL REFERENCES direct_contexts(direct_context_id) ON DELETE RESTRICT,
 key_format_version INTEGER NOT NULL CHECK(key_format_version=2),
 canonical_key BLOB NOT NULL,
 revision BLOB NOT NULL CHECK(length(revision)=8),
 UNIQUE(group_id,direct_context_id,canonical_key),
 UNIQUE(series_id,group_id)
) STRICT;




CREATE TABLE direct_dimension_atoms (
 atom_id INTEGER PRIMARY KEY CHECK(atom_id>0),
 value_kind TEXT NOT NULL CHECK(value_kind IN ('integer','text','native_id','boolean','uint64','rational','set')),
 coverage_kind TEXT NOT NULL CHECK(coverage_kind IN ('known','unknown','unsupported','not_applicable')),
 int_value INTEGER,
 text_value TEXT,
 blob_value BLOB,
 ratio_numerator INTEGER,
 ratio_denominator INTEGER,
 canonical_value_key BLOB NOT NULL UNIQUE,
 UNIQUE(atom_id,value_kind),
 UNIQUE(atom_id,value_kind,coverage_kind),
 CHECK ((coverage_kind='known' AND (
 (value_kind IN ('integer','boolean') AND int_value IS NOT NULL AND text_value IS NULL AND blob_value IS NULL AND ratio_numerator IS NULL AND ratio_denominator IS NULL) OR
 (value_kind='text' AND int_value IS NULL AND text_value IS NOT NULL AND blob_value IS NULL AND ratio_numerator IS NULL AND ratio_denominator IS NULL) OR
 (value_kind='native_id' AND int_value IS NULL AND text_value IS NULL AND blob_value IS NOT NULL AND ratio_numerator IS NULL AND ratio_denominator IS NULL) OR
 (value_kind='uint64' AND int_value IS NULL AND text_value IS NULL AND blob_value IS NOT NULL AND length(blob_value)=8 AND ratio_numerator IS NULL AND ratio_denominator IS NULL) OR
 (value_kind='rational' AND int_value IS NULL AND text_value IS NULL AND blob_value IS NULL AND ratio_numerator IS NOT NULL AND ratio_numerator>=0 AND ratio_denominator IS NOT NULL AND ratio_denominator>0) OR
 (value_kind='set' AND int_value IS NULL AND text_value IS NULL AND blob_value IS NULL AND ratio_numerator IS NULL AND ratio_denominator IS NULL))) OR
 (coverage_kind<>'known' AND int_value IS NULL AND text_value IS NULL AND blob_value IS NULL AND ratio_numerator IS NULL AND ratio_denominator IS NULL)),
 CHECK(value_kind<>'boolean' OR int_value IS NULL OR int_value IN (0,1))
) STRICT;
CREATE INDEX ix_v2_atom_int ON direct_dimension_atoms(int_value,atom_id) WHERE coverage_kind='known' AND value_kind IN ('integer','boolean');
CREATE INDEX ix_v2_atom_text ON direct_dimension_atoms(text_value,atom_id) WHERE coverage_kind='known' AND value_kind='text';
CREATE INDEX ix_v2_atom_native ON direct_dimension_atoms(blob_value,atom_id) WHERE coverage_kind='known' AND value_kind IN ('native_id','uint64');
CREATE INDEX ix_v2_atom_ratio ON direct_dimension_atoms(ratio_numerator,ratio_denominator,atom_id) WHERE coverage_kind='known' AND value_kind='rational';

CREATE TABLE direct_series_dimension_refs (
 series_id INTEGER NOT NULL,
 group_id INTEGER NOT NULL,
 dimension_no INTEGER NOT NULL,
 atom_id INTEGER NOT NULL,
 value_kind TEXT NOT NULL,
 PRIMARY KEY(series_id,dimension_no),
 FOREIGN KEY(series_id,group_id) REFERENCES direct_series(series_id,group_id) ON DELETE CASCADE ON UPDATE RESTRICT,
 FOREIGN KEY(group_id,dimension_no,value_kind) REFERENCES direct_dimensions(group_id,dimension_no,value_kind) ON DELETE RESTRICT ON UPDATE RESTRICT,
 FOREIGN KEY(atom_id,value_kind) REFERENCES direct_dimension_atoms(atom_id,value_kind) ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_v2_refs_atom ON direct_series_dimension_refs(atom_id,group_id,dimension_no,series_id);

CREATE TABLE direct_atom_members (
 atom_id INTEGER NOT NULL,
 value_kind TEXT NOT NULL CHECK(value_kind='set'),
 coverage_kind TEXT NOT NULL CHECK(coverage_kind='known'),
 member_key BLOB NOT NULL,
 PRIMARY KEY(atom_id,member_key),
 FOREIGN KEY(atom_id,value_kind,coverage_kind) REFERENCES direct_dimension_atoms(atom_id,value_kind,coverage_kind) ON DELETE CASCADE ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX ix_v2_atom_member ON direct_atom_members(member_key,atom_id);

CREATE TRIGGER v2_atom_payload_immutable BEFORE UPDATE ON direct_dimension_atoms
 BEGIN SELECT RAISE(ABORT,'dimension atoms are immutable; insert a new exact value'); END;
CREATE TRIGGER v2_atom_members_insert_guard BEFORE INSERT ON direct_atom_members
 WHEN EXISTS(SELECT 1 FROM direct_series_dimension_refs WHERE atom_id=NEW.atom_id)
 BEGIN SELECT RAISE(ABORT,'referenced set atoms are immutable'); END;
CREATE TRIGGER v2_atom_members_delete_guard BEFORE DELETE ON direct_atom_members
 WHEN EXISTS(SELECT 1 FROM direct_series_dimension_refs WHERE atom_id=OLD.atom_id)
 BEGIN SELECT RAISE(ABORT,'referenced set atoms are immutable'); END;
CREATE TRIGGER v2_atom_members_update_guard BEFORE UPDATE ON direct_atom_members
 WHEN EXISTS(SELECT 1 FROM direct_series_dimension_refs WHERE atom_id=OLD.atom_id OR atom_id=NEW.atom_id)
 BEGIN SELECT RAISE(ABORT,'referenced set atoms are immutable'); END;

CREATE VIEW direct_dimension_values AS
 SELECT r.series_id,r.group_id,r.dimension_no,a.value_kind,a.coverage_kind,
        a.int_value,a.text_value,a.blob_value,a.ratio_numerator,a.ratio_denominator
 FROM direct_series_dimension_refs r JOIN direct_dimension_atoms a ON a.atom_id=r.atom_id;







CREATE VIEW direct_dimension_members AS
 SELECT r.series_id,r.group_id,r.dimension_no,m.value_kind,m.coverage_kind,m.member_key
 FROM direct_series_dimension_refs r JOIN direct_atom_members m ON m.atom_id=r.atom_id;



CREATE TABLE "direct_measure_states" (
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind IN ('scalar','exact_set','rational_extremum')),
  "known_presence" INTEGER NOT NULL CHECK(known_presence IN (0,1)),
  "known_count_i64" INTEGER CHECK(known_count_i64 IS NULL OR known_count_i64>=0),
  "known_count_decimal" TEXT ,
  "unknown_count_i64" INTEGER CHECK(unknown_count_i64 IS NULL OR unknown_count_i64>=0),
  "unknown_count_decimal" TEXT ,
  "unsupported_count_i64" INTEGER CHECK(unsupported_count_i64 IS NULL OR unsupported_count_i64>=0),
  "unsupported_count_decimal" TEXT ,
  "not_applicable_count_i64" INTEGER CHECK(not_applicable_count_i64 IS NULL OR not_applicable_count_i64>=0),
  "not_applicable_count_decimal" TEXT ,
  "revision" BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY ("series_id", "measure_no"),
  UNIQUE ("series_id", "group_id", "measure_no", "measure_kind", "known_presence"),
  CHECK (((known_count_i64 IS NOT NULL)+(known_count_decimal IS NOT NULL))=1),
  CHECK (known_count_decimal IS NULL OR (known_count_decimal='0' OR (length(known_count_decimal)>0 AND known_count_decimal NOT GLOB '*[^0-9]*' AND substr(known_count_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (known_count_decimal IS NULL OR length(known_count_decimal)>19 OR (length(known_count_decimal)=19 AND known_count_decimal>'9223372036854775807')),
  CHECK (((unknown_count_i64 IS NOT NULL)+(unknown_count_decimal IS NOT NULL))=1),
  CHECK (unknown_count_decimal IS NULL OR (unknown_count_decimal='0' OR (length(unknown_count_decimal)>0 AND unknown_count_decimal NOT GLOB '*[^0-9]*' AND substr(unknown_count_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (unknown_count_decimal IS NULL OR length(unknown_count_decimal)>19 OR (length(unknown_count_decimal)=19 AND unknown_count_decimal>'9223372036854775807')),
  CHECK (((unsupported_count_i64 IS NOT NULL)+(unsupported_count_decimal IS NOT NULL))=1),
  CHECK (unsupported_count_decimal IS NULL OR (unsupported_count_decimal='0' OR (length(unsupported_count_decimal)>0 AND unsupported_count_decimal NOT GLOB '*[^0-9]*' AND substr(unsupported_count_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (unsupported_count_decimal IS NULL OR length(unsupported_count_decimal)>19 OR (length(unsupported_count_decimal)=19 AND unsupported_count_decimal>'9223372036854775807')),
  CHECK (((not_applicable_count_i64 IS NOT NULL)+(not_applicable_count_decimal IS NOT NULL))=1),
  CHECK (not_applicable_count_decimal IS NULL OR (not_applicable_count_decimal='0' OR (length(not_applicable_count_decimal)>0 AND not_applicable_count_decimal NOT GLOB '*[^0-9]*' AND substr(not_applicable_count_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (not_applicable_count_decimal IS NULL OR length(not_applicable_count_decimal)>19 OR (length(not_applicable_count_decimal)=19 AND not_applicable_count_decimal>'9223372036854775807')),
  CHECK (known_presence=CASE WHEN known_count_i64=0 THEN 0 ELSE 1 END),
  CONSTRAINT "fk_direct_measure_states_1" FOREIGN KEY ("series_id", "group_id") REFERENCES "direct_series" ("series_id", "group_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_measure_states_2" FOREIGN KEY ("group_id", "measure_no", "measure_kind") REFERENCES "direct_measures" ("group_id", "measure_no", "measure_kind") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_witnesses" (
  "witness_id" INTEGER NOT NULL ,
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "witness_slot" INTEGER NOT NULL CHECK(witness_slot=0),
  "witness_kind" TEXT NOT NULL ,
  "attempt_id" BLOB CHECK(attempt_id IS NULL OR length(attempt_id)=16),
  "context_id" INTEGER ,
  "archive_unit_id" INTEGER ,
  "source_stream_id" BLOB CHECK(source_stream_id IS NULL OR length(source_stream_id)=16),
  "source_sequence" BLOB CHECK(source_sequence IS NULL OR length(source_sequence)=8),
  "source_revision" BLOB NOT NULL CHECK(length(source_revision)=8),
  "locator_key" BLOB ,
  PRIMARY KEY ("witness_id"),
  UNIQUE ("series_id", "measure_no", "witness_slot"),
  UNIQUE ("witness_id", "series_id", "group_id", "measure_no"),
  CHECK ((source_stream_id IS NULL)=(source_sequence IS NULL)),
  CHECK (source_revision>=X'0000000000000001'),
  CHECK (attempt_id IS NOT NULL OR archive_unit_id IS NOT NULL OR source_stream_id IS NOT NULL),
  CHECK (locator_key IS NULL OR length(locator_key)>0),
  CONSTRAINT "fk_direct_witnesses_1" FOREIGN KEY ("series_id", "group_id") REFERENCES "direct_series" ("series_id", "group_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_2" FOREIGN KEY ("group_id", "measure_no") REFERENCES "direct_measures" ("group_id", "measure_no") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_3" FOREIGN KEY ("attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_4" FOREIGN KEY ("context_id") REFERENCES "contexts" ("context_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_5" FOREIGN KEY ("archive_unit_id") REFERENCES "archive_units" ("unit_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_6" FOREIGN KEY ("archive_unit_id", "attempt_id") REFERENCES "archive_units" ("unit_id", "attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_witnesses_7" FOREIGN KEY ("source_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;
CREATE INDEX "ix_h_witness_archive" ON "direct_witnesses" ("archive_unit_id") WHERE archive_unit_id IS NOT NULL;

CREATE TABLE "direct_scalar_values" (
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind='scalar'),
  "known_presence" INTEGER NOT NULL CHECK(known_presence=1),
  "value_domain" TEXT NOT NULL CHECK(value_domain IN ('nonnegative_integer','signed_integer')),
  "value_i64" INTEGER ,
  "value_decimal" TEXT ,
  "witness_id" INTEGER ,
  PRIMARY KEY ("series_id", "measure_no"),
  CHECK (((value_i64 IS NOT NULL)+(value_decimal IS NOT NULL))=1),
  CHECK (value_decimal IS NULL OR ((value_decimal='0' OR (length(value_decimal)>0 AND value_decimal NOT GLOB '*[^0-9]*' AND substr(value_decimal,1,1) BETWEEN '1' AND '9')) OR (substr(value_decimal,1,1)='-' AND length(value_decimal)>1 AND substr(value_decimal,2) NOT GLOB '*[^0-9]*' AND substr(value_decimal,2,1) BETWEEN '1' AND '9'))),
  CHECK (value_decimal IS NULL OR (substr(value_decimal,1,1)<>'-' AND (length(value_decimal)>19 OR (length(value_decimal)=19 AND value_decimal>'9223372036854775807'))) OR (substr(value_decimal,1,1)='-' AND (length(value_decimal)>20 OR (length(value_decimal)=20 AND substr(value_decimal,2)>'9223372036854775808')))),
  CHECK (value_domain<>'nonnegative_integer' OR (value_i64 IS NOT NULL AND value_i64>=0) OR (value_decimal IS NOT NULL AND substr(value_decimal,1,1)<>'-')),
  CONSTRAINT "fk_direct_scalar_values_1" FOREIGN KEY ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") REFERENCES "direct_measure_states" ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_scalar_values_2" FOREIGN KEY ("group_id", "measure_no", "measure_kind", "value_domain") REFERENCES "direct_measures" ("group_id", "measure_no", "measure_kind", "value_domain") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_scalar_values_3" FOREIGN KEY ("witness_id", "series_id", "group_id", "measure_no") REFERENCES "direct_witnesses" ("witness_id", "series_id", "group_id", "measure_no") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_exact_set_members" (
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind='exact_set'),
  "known_presence" INTEGER NOT NULL CHECK(known_presence=1),
  "member_namespace" TEXT NOT NULL ,
  "member_key" BLOB NOT NULL ,
  PRIMARY KEY ("series_id", "measure_no", "member_namespace", "member_key"),
  CONSTRAINT "fk_direct_exact_set_members_1" FOREIGN KEY ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") REFERENCES "direct_measure_states" ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") ON DELETE CASCADE ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_h_exact_member_lookup" ON "direct_exact_set_members" ("group_id", "measure_no", "member_namespace", "member_key", "series_id");

CREATE TABLE "direct_rational_extrema" (
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "measure_no" INTEGER NOT NULL ,
  "measure_kind" TEXT NOT NULL CHECK(measure_kind='rational_extremum'),
  "known_presence" INTEGER NOT NULL CHECK(known_presence=1),
  "numerator_i64" INTEGER CHECK(numerator_i64 IS NULL OR numerator_i64>=0),
  "numerator_decimal" TEXT ,
  "denominator_i64" INTEGER CHECK(denominator_i64 IS NULL OR denominator_i64>=0),
  "denominator_decimal" TEXT ,
  "witness_id" INTEGER NOT NULL ,
  PRIMARY KEY ("series_id", "measure_no"),
  CHECK (((numerator_i64 IS NOT NULL)+(numerator_decimal IS NOT NULL))=1),
  CHECK (numerator_decimal IS NULL OR (numerator_decimal='0' OR (length(numerator_decimal)>0 AND numerator_decimal NOT GLOB '*[^0-9]*' AND substr(numerator_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (numerator_decimal IS NULL OR length(numerator_decimal)>19 OR (length(numerator_decimal)=19 AND numerator_decimal>'9223372036854775807')),
  CHECK (((denominator_i64 IS NOT NULL)+(denominator_decimal IS NOT NULL))=1),
  CHECK (denominator_decimal IS NULL OR (denominator_decimal='0' OR (length(denominator_decimal)>0 AND denominator_decimal NOT GLOB '*[^0-9]*' AND substr(denominator_decimal,1,1) BETWEEN '1' AND '9'))),
  CHECK (denominator_decimal IS NULL OR length(denominator_decimal)>19 OR (length(denominator_decimal)=19 AND denominator_decimal>'9223372036854775807')),
  CHECK (denominator_i64 IS NULL OR denominator_i64>0),
  CONSTRAINT "fk_direct_rational_extrema_1" FOREIGN KEY ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") REFERENCES "direct_measure_states" ("series_id", "group_id", "measure_no", "measure_kind", "known_presence") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_rational_extrema_2" FOREIGN KEY ("witness_id", "series_id", "group_id", "measure_no") REFERENCES "direct_witnesses" ("witness_id", "series_id", "group_id", "measure_no") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_ordered_reducer_state" (
  "series_id" INTEGER NOT NULL ,
  "group_id" INTEGER NOT NULL ,
  "state_version" INTEGER NOT NULL CHECK(state_version>0),
  "active_attempt_id" BLOB CHECK(active_attempt_id IS NULL OR length(active_attempt_id)=16),
  "last_stream_id" BLOB NOT NULL CHECK(length(last_stream_id)=16),
  "last_sequence" BLOB NOT NULL CHECK(length(last_sequence)=8),
  "revision" BLOB NOT NULL CHECK(length(revision)=8),
  "pending_scope_id" INTEGER ,
  "checkpoint_payload" BLOB NOT NULL ,
  PRIMARY KEY ("series_id"),
  CONSTRAINT "fk_direct_ordered_reducer_state_1" FOREIGN KEY ("series_id", "group_id") REFERENCES "direct_series" ("series_id", "group_id") ON DELETE CASCADE ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_ordered_reducer_state_2" FOREIGN KEY ("active_attempt_id") REFERENCES "attempts" ("attempt_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_ordered_reducer_state_3" FOREIGN KEY ("last_stream_id") REFERENCES "producer_streams" ("stream_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_ordered_reducer_state_4" FOREIGN KEY ("pending_scope_id") REFERENCES "scopes" ("scope_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT;


CREATE TABLE "direct_relationship_contracts" (
  "contract_id" INTEGER NOT NULL ,
  "contract_key" TEXT NOT NULL ,
  "contract_version" INTEGER NOT NULL CHECK(contract_version>0),
  "definition_digest" BLOB NOT NULL CHECK(length(definition_digest)=32),
  "definition_json" TEXT NOT NULL ,
  PRIMARY KEY ("contract_id"),
  UNIQUE ("contract_key", "contract_version")
) STRICT;
CREATE INDEX "ix_h_contract_digest" ON "direct_relationship_contracts" ("definition_digest");

CREATE TABLE "direct_relationship_fields" (
  "contract_id" INTEGER NOT NULL ,
  "field_no" INTEGER NOT NULL CHECK(field_no>=0),
  "field_key" TEXT NOT NULL ,
  "logical_type" TEXT NOT NULL ,
  "storage_binding" TEXT NOT NULL CHECK(storage_binding IN ('dimension','dimension_members','measure','coverage','ordered_state','context','witness')),
  "meaning" TEXT NOT NULL ,
  PRIMARY KEY ("contract_id", "field_no"),
  UNIQUE ("contract_id", "field_key"),
  CONSTRAINT "fk_direct_relationship_fields_1" FOREIGN KEY ("contract_id") REFERENCES "direct_relationship_contracts" ("contract_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;


CREATE TABLE "direct_group_contracts" (
  "group_id" INTEGER NOT NULL ,
  "contract_id" INTEGER NOT NULL ,
  PRIMARY KEY ("group_id", "contract_id"),
  CONSTRAINT "fk_direct_group_contracts_1" FOREIGN KEY ("group_id") REFERENCES "direct_groups" ("group_id") ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT "fk_direct_group_contracts_2" FOREIGN KEY ("contract_id") REFERENCES "direct_relationship_contracts" ("contract_id") ON DELETE RESTRICT ON UPDATE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE INDEX "ix_h_contract_groups" ON "direct_group_contracts" ("contract_id", "group_id");
