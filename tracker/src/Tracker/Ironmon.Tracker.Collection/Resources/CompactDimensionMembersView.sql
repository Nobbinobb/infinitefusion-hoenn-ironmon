DROP VIEW IF EXISTS direct_dimension_members;
CREATE VIEW direct_dimension_members AS SELECT r.series_id,r.group_id,r.dimension_no,m.value_kind,m.coverage_kind,m.member_value_id FROM direct_series_dimension_refs r JOIN direct_atom_members m ON m.atom_id=r.atom_id;
