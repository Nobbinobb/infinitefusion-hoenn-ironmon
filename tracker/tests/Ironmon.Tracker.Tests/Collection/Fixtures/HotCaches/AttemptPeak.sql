SELECT v.value_i64
FROM direct_scalar_values v
JOIN direct_groups g ON g.group_id=v.group_id
WHERE g.projection_key='scope_statistic_peaks' AND g.variant_key='6bc8aff8bd439935';
