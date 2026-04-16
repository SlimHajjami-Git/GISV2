-- Migration 028: Enforce uniqueness on (device_id, recorded_at) in gps_positions
--
-- Root cause of the playback teleport bug:
-- The Rust ingest builds event_key as <imei>:<ts>:<lat>:<lon>:<speed>:<send_flag>,
-- so two frames with the same (device_id, recorded_at) but slightly different
-- coordinates (delayed/cached frame arriving later, or same-timestamp-default
-- on GPS-not-locked state) produce DIFFERENT event_keys and both get inserted.
-- Result: 59,027 duplicate rows in prod (1.6M total), up to 788 km apart,
-- rendering as visible cross-country teleport lines in playback.
--
-- This migration:
--   1. Purges existing duplicates, keeping the earliest-arrived row per
--      (device_id, recorded_at) — that's almost always the live frame,
--      not a stale buffered re-send.
--   2. Replaces the non-unique (device_id, recorded_at) index with a
--      UNIQUE version, so future duplicate inserts fail the ON CONFLICT
--      check in the ingest and are silently dropped.
--
-- Safe to re-run: if duplicates are already gone, step 1 is a no-op.

-- Step 1: Remove existing duplicates (keep earliest-arrived row per key).
DELETE FROM gps_positions WHERE id IN (
  SELECT id FROM (
    SELECT id,
           ROW_NUMBER() OVER (
             PARTITION BY device_id, recorded_at
             ORDER BY created_at
           ) AS rk
    FROM gps_positions
  ) ranked
  WHERE rk > 1
);

-- Step 2: Replace the non-unique index with a UNIQUE one.
-- (CONCURRENTLY cannot be used inside a transaction; plain form is fine —
-- index build on ~1.5M rows takes a few seconds.)
DROP INDEX IF EXISTS ix_gps_positions_device_time;
CREATE UNIQUE INDEX ix_gps_positions_device_time
  ON gps_positions (device_id, recorded_at);
