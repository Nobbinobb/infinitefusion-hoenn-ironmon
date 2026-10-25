SELECT group_concat(amount,',') FROM (
 SELECT sum(c.value_blob) AS amount
 FROM aggregate_cells c
 JOIN aggregate_series s USING(series_id)
 JOIN hot_partitions p ON p.partition_id=s.partition_id
 JOIN scopes x ON x.scope_id=p.scope_id
 JOIN aggregate_families f ON f.family_id=s.family_id
 WHERE f.family_key='actual_hp_lost' AND x.scope_kind='attempt'
 GROUP BY x.scope_id
 ORDER BY amount
);
