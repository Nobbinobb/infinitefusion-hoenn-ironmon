CREATE TABLE IF NOT EXISTS collection_values (
  value_id INTEGER PRIMARY KEY,
  value_kind INTEGER NOT NULL CHECK(value_kind BETWEEN 0 AND 7),
  payload BLOB NOT NULL,
  digest BLOB NOT NULL CHECK(length(digest)=8)
) STRICT;
CREATE INDEX IF NOT EXISTS ix_collection_values_lookup ON collection_values(value_kind,digest);
