CREATE TABLE collection_values (
    value_id INTEGER PRIMARY KEY,
    value_kind INTEGER NOT NULL,
    payload BLOB NOT NULL,
    digest BLOB NOT NULL
) STRICT;
INSERT INTO collection_values VALUES(2147483648,0,X'',X'0000000000000000');
CREATE INDEX ix_collection_values_lookup ON collection_values(value_kind,digest);
CREATE TABLE direct_scalar_values (
    series_id INTEGER NOT NULL,
    group_id INTEGER NOT NULL,
    measure_no INTEGER NOT NULL,
    value_domain TEXT NOT NULL,
    value_i64 INTEGER,
    value_decimal TEXT,
    witness_id INTEGER,
    PRIMARY KEY(series_id,measure_no),
    CHECK((value_i64 IS NULL) <> (value_decimal IS NULL))
) STRICT;
CREATE TABLE generator_native_values (
    native_id INTEGER PRIMARY KEY,
    native_value ANY
) STRICT;
