CREATE TABLE maintenance_original (
  id INTEGER PRIMARY KEY,
  value ANY,
  computed INTEGER GENERATED ALWAYS AS (id) VIRTUAL,
  detail TEXT
) STRICT;
