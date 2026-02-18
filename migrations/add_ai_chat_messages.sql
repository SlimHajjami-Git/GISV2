-- Migration: Add AI Chat Messages table for LLM diagnostic chat
-- Run on prod: docker compose -f docker-compose.prod.yml exec postgres psql -U postgres -d gis_v2 -f /migrations/add_ai_chat_messages.sql

CREATE TABLE IF NOT EXISTS ai_chat_messages (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE RESTRICT,
    role VARCHAR(20) NOT NULL DEFAULT 'user',
    content TEXT NOT NULL,
    session_id VARCHAR(100),
    tokens_used INTEGER,
    company_id INTEGER NOT NULL REFERENCES societes(id),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_ai_chat_messages_user_vehicle_time 
    ON ai_chat_messages (user_id, vehicle_id, created_at);
