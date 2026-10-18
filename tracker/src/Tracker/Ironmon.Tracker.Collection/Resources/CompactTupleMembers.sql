INSERT INTO tuple_member_groups SELECT tuple_id,ordinal/4,max(CASE WHEN ordinal%4=0 THEN identity_id END),max(CASE WHEN ordinal%4=1 THEN identity_id END),max(CASE WHEN ordinal%4=2 THEN identity_id END),max(CASE WHEN ordinal%4=3 THEN identity_id END) FROM tuple_members GROUP BY tuple_id,ordinal/4;
DROP TABLE tuple_members;
