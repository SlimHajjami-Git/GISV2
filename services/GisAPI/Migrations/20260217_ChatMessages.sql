-- Chat messages table for instant messaging between users
-- Multi-tenant: filtered by company_id

CREATE TABLE IF NOT EXISTS chat_messages (
    id SERIAL PRIMARY KEY,
    sender_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    receiver_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    content TEXT NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    read_at TIMESTAMP WITH TIME ZONE,
    company_id INTEGER NOT NULL REFERENCES societes(id),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Index for conversation queries (sender + receiver + time)
CREATE INDEX IF NOT EXISTS ix_chat_messages_sender_receiver_time 
ON chat_messages (sender_id, receiver_id, created_at DESC);

-- Index for unread message counts
CREATE INDEX IF NOT EXISTS ix_chat_messages_receiver_unread 
ON chat_messages (receiver_id, is_read) WHERE is_read = FALSE;

-- Index for company filtering
CREATE INDEX IF NOT EXISTS ix_chat_messages_company_id 
ON chat_messages (company_id);

ANALYZE chat_messages;
