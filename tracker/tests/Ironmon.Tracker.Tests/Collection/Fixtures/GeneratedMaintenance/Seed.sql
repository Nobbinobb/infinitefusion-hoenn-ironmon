CREATE TABLE sentinel (id INTEGER PRIMARY KEY) STRICT;
INSERT INTO sentinel VALUES(1);
CREATE TABLE "quoted""; DROP TABLE sentinel;--" (
  id INTEGER PRIMARY KEY,
  "block""; DROP TABLE sentinel;--" INTEGER NOT NULL,
  payload BLOB NOT NULL
) STRICT;
INSERT INTO "quoted""; DROP TABLE sentinel;--" VALUES(9223372036854775807,9223372036854775807,X'00FF80');
CREATE TABLE child_references (
  id INTEGER PRIMARY KEY,
  parent_id INTEGER NOT NULL REFERENCES "quoted""; DROP TABLE sentinel;--"(id)
) STRICT;
INSERT INTO child_references VALUES(1,9223372036854775807);
CREATE TABLE maintenance_original (
  id INTEGER PRIMARY KEY,
  value ANY,
  computed INTEGER GENERATED ALWAYS AS (id) VIRTUAL,
  detail TEXT
) STRICT;
INSERT INTO maintenance_original(id,value,detail) VALUES
  (2147483648,9223372036854775807,'historical'),
  (9223372036854775807,X'000102FF',''),
  (-9223372036854775808,NULL,NULL);
CREATE INDEX "index""; DROP TABLE sentinel;--" ON maintenance_original(detail);
CREATE TRIGGER "trigger""; DROP TABLE sentinel;--" BEFORE UPDATE ON maintenance_original
  BEGIN SELECT RAISE(ABORT,'retained historical trigger'); END;
