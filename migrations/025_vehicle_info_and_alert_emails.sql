-- Migration: Add vehicle acquisition info, document dates, reminders + alert_emails table
-- Date: 2026-04-13

-- ── Vehicle: Acquisition info ──
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS acquisition_type VARCHAR(20) NOT NULL DEFAULT 'purchase';
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS purchase_price DECIMAL(12,2);
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS leasing_monthly_payment DECIMAL(10,2);
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS registration_date TIMESTAMP;

-- ── Vehicle: Document start dates ──
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS insurance_start_date TIMESTAMP;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS tax_start_date TIMESTAMP;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS technical_inspection_start_date TIMESTAMP;

-- ── Vehicle: Reminder days before expiry ──
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS insurance_reminder_days INTEGER NOT NULL DEFAULT 30;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS tax_reminder_days INTEGER NOT NULL DEFAULT 30;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS technical_inspection_reminder_days INTEGER NOT NULL DEFAULT 30;

-- ── Alert Emails table ──
CREATE TABLE IF NOT EXISTS alert_emails (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) NOT NULL,
    alert_type VARCHAR(50) NOT NULL, -- 'assurance', 'taxe_circulation', 'visite_technique', 'entretien'
    company_id INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_alert_emails_company_id ON alert_emails(company_id);
CREATE INDEX IF NOT EXISTS idx_alert_emails_type ON alert_emails(alert_type);

-- ── Report permissions: Monthly costs report ──
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS report_monthly_costs BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE users ADD COLUMN IF NOT EXISTS can_report_monthly_costs BOOLEAN NOT NULL DEFAULT true;

-- ── User alert email preferences ──
ALTER TABLE users ADD COLUMN IF NOT EXISTS alert_assurance BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS alert_taxe_circulation BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS alert_visite_technique BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS alert_entretien BOOLEAN NOT NULL DEFAULT false;
