CREATE TABLE IF NOT EXISTS aggregate_set_coverage (
  series_id INTEGER NOT NULL, family_id INTEGER NOT NULL, measure_no INTEGER NOT NULL,
  measure_kind TEXT GENERATED ALWAYS AS ('exact_set') VIRTUAL NOT NULL CHECK(measure_kind='exact_set'),
  known_count ANY NOT NULL, unknown_count ANY NOT NULL, unsupported_count ANY NOT NULL, not_applicable_count ANY NOT NULL,
  revision BLOB NOT NULL CHECK(length(revision)=8), PRIMARY KEY(series_id,measure_no),
  FOREIGN KEY(series_id,family_id) REFERENCES aggregate_series(series_id,family_id) ON DELETE CASCADE,
  FOREIGN KEY(family_id,measure_no,measure_kind) REFERENCES family_measures(family_id,measure_no,measure_kind) ON DELETE RESTRICT
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS collection_operations (
  id INTEGER PRIMARY KEY CHECK(id=1), accepted_batches INTEGER NOT NULL, accepted_events INTEGER NOT NULL,
  duplicate_events INTEGER NOT NULL, received_bytes INTEGER NOT NULL, previous_commit_ms REAL NOT NULL,
  updated_utc_ms INTEGER NOT NULL
) STRICT;
CREATE TABLE IF NOT EXISTS owned_record_state (
  individual_id INTEGER PRIMARY KEY REFERENCES owned_individuals(individual_id) ON DELETE RESTRICT,
  fingerprint BLOB NOT NULL CHECK(length(fingerprint)=32), last_revision INTEGER NOT NULL CHECK(last_revision>0),
  last_configuration_id INTEGER NOT NULL REFERENCES configuration_revisions(configuration_id) ON DELETE RESTRICT
) STRICT;
CREATE TABLE IF NOT EXISTS owned_partial_configurations (
  individual_id INTEGER NOT NULL REFERENCES owned_individuals(individual_id) ON DELETE RESTRICT,
  source_stream_id BLOB NOT NULL REFERENCES producer_streams(stream_id) ON DELETE RESTRICT,
  source_sequence BLOB NOT NULL CHECK(length(source_sequence)=8),
  detail_block_id INTEGER NOT NULL REFERENCES block_values(block_id) ON DELETE RESTRICT,
  revision BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY(individual_id,source_stream_id,source_sequence)
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS collection_current_snapshots (
  family_id INTEGER NOT NULL REFERENCES aggregate_families(family_id) ON DELETE RESTRICT,
  scope_id INTEGER NOT NULL REFERENCES scopes(scope_id) ON DELETE RESTRICT,
  container_identity_id INTEGER NOT NULL REFERENCES identity_values(identity_id) ON DELETE RESTRICT,
  format_version INTEGER NOT NULL CHECK(format_version=1),
  state_blob BLOB NOT NULL, source_stream_id BLOB NOT NULL REFERENCES producer_streams(stream_id) ON DELETE RESTRICT,
  source_sequence BLOB NOT NULL CHECK(length(source_sequence)=8), revision BLOB NOT NULL CHECK(length(revision)=8),
  PRIMARY KEY(family_id,scope_id,container_identity_id)
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS completed_attempt_reducer_states (
  attempt_id BLOB PRIMARY KEY REFERENCES attempts(attempt_id) ON DELETE RESTRICT,
  format_version INTEGER NOT NULL CHECK(format_version=1),
  state_codec TEXT NOT NULL CHECK(state_codec='zlib-json-v1'),
  state_blob BLOB NOT NULL, uncompressed_length INTEGER NOT NULL CHECK(uncompressed_length>=0),
  source_stream_id BLOB NOT NULL REFERENCES producer_streams(stream_id) ON DELETE RESTRICT,
  source_sequence BLOB NOT NULL CHECK(length(source_sequence)=8),
  revision BLOB NOT NULL CHECK(length(revision)=8)
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS history_exports (
 archive_id BLOB PRIMARY KEY CHECK(length(archive_id)=16), created_utc_ms INTEGER NOT NULL
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS history_imports (
 archive_id BLOB PRIMARY KEY REFERENCES history_exports(archive_id), imported_utc_ms INTEGER NOT NULL
) STRICT, WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS history_value_roots (
 archive_id BLOB NOT NULL REFERENCES history_exports(archive_id),
 value_id INTEGER NOT NULL REFERENCES collection_values(value_id),
 PRIMARY KEY(archive_id,value_id)
) STRICT, WITHOUT ROWID;
