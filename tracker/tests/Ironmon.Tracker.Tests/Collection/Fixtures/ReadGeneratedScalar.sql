SELECT group_id,value_domain,value_i64,value_decimal,witness_id
FROM direct_scalar_values
WHERE series_id=$series AND measure_no=$measure;
