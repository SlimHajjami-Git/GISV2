-- =============================================================
-- Migration: Notification System
-- Date: 2026-02-11
-- Description: Add performance indexes to notifications table
-- Note: Table was auto-created by EF Core with PascalCase columns
-- =============================================================

-- Performance indexes (PascalCase column names from EF Core)
CREATE INDEX IF NOT EXISTS idx_notifications_company_id ON notifications("CompanyId");
CREATE INDEX IF NOT EXISTS idx_notifications_user_unread ON notifications("UserId", "IsRead") WHERE NOT "IsRead";
CREATE INDEX IF NOT EXISTS idx_notifications_created_at ON notifications("CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS idx_notifications_type ON notifications("Type");
CREATE INDEX IF NOT EXISTS idx_notifications_reference ON notifications("ReferenceType", "ReferenceId") WHERE "ReferenceType" IS NOT NULL;
