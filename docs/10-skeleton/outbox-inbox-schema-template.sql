-- ModulithReliabilityKit Outbox/Inbox schema template (module-scoped)
-- Copy this per module schema (for example: catalog, notifications).

-- Outbox
CREATE TABLE IF NOT EXISTS {{schema_name}}.outbox_messages
(
    id BIGSERIAL PRIMARY KEY,
    logical_id UUID NOT NULL,
    occurred_on_utc TIMESTAMPTZ NOT NULL,
    type TEXT NOT NULL,
    payload JSONB NOT NULL,
    processed_on_utc TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_{{schema_name}}_outbox_logical_id
    ON {{schema_name}}.outbox_messages (logical_id);

CREATE INDEX IF NOT EXISTS ix_{{schema_name}}_outbox_pending
    ON {{schema_name}}.outbox_messages (processed_on_utc, occurred_on_utc);

-- Inbox
CREATE TABLE IF NOT EXISTS {{schema_name}}.inbox_messages
(
    id BIGSERIAL PRIMARY KEY,
    logical_id UUID NOT NULL,
    occurred_on_utc TIMESTAMPTZ NOT NULL,
    type TEXT NOT NULL,
    payload JSONB NOT NULL,
    processed_on_utc TIMESTAMPTZ NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    last_retry_on_utc TIMESTAMPTZ NULL,
    last_error TEXT NULL,
    next_retry_on_utc TIMESTAMPTZ NULL,
    status TEXT NOT NULL DEFAULT 'pending'
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_{{schema_name}}_inbox_logical_occurred
    ON {{schema_name}}.inbox_messages (logical_id, occurred_on_utc);

CREATE INDEX IF NOT EXISTS ix_{{schema_name}}_inbox_pending
    ON {{schema_name}}.inbox_messages (status, next_retry_on_utc, occurred_on_utc);

-- Inbox dead-letter
CREATE TABLE IF NOT EXISTS {{schema_name}}.inbox_dead_letters
(
    id UUID PRIMARY KEY,
    original_message_id UUID NOT NULL,
    type TEXT NOT NULL,
    payload JSONB NOT NULL,
    occurred_on_utc TIMESTAMPTZ NOT NULL,
    retry_count INTEGER NOT NULL,
    last_error TEXT NOT NULL,
    moved_to_dead_letter_on_utc TIMESTAMPTZ NOT NULL,
    resolved_on_utc TIMESTAMPTZ NULL,
    resolved_by TEXT NULL,
    resolution_notes TEXT NULL,
    resolution_status TEXT NOT NULL DEFAULT 'pending'
);
