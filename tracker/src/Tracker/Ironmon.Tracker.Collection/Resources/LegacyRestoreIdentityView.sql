CREATE VIEW identities AS SELECT i.identity_id,n.namespace_revision,n.namespace,i.native_value_id,i.source_context_id FROM identity_values i JOIN identity_namespaces n USING(namespace_id);
DROP TABLE IF EXISTS identity_labels;
