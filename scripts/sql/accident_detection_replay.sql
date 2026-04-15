-- =============================================================================
-- Accident Detection Replay Tool
-- =============================================================================
--
-- WHAT THIS DOES
--   Replays historical rows in `gps_positions` to find candidate accidents
--   using a tunable 4-condition algorithm, without touching any production
--   table. Output is three result sets:
--     1) QUALIFIED   — candidates that would have raised an alert
--     2) NEAR_MISS   — anchor pairs that triggered A but failed B or C
--     3) SUMMARY     — aggregate counts by verdict
--   Use the near-misses to catch real accidents you would have filtered out,
--   or to spot noise still leaking through the gate.
--
-- CALIBRATION
--   Thresholds were fitted on the confirmed accident of vehicle 118013
--   (plate 259 TU 4972, company 4) on 2026-04-14 at ~16:02 local time.
--   Signature observed in that case:
--     - two positions with mems_peak ≈ 126 within 27 seconds (anchor pair)
--     - four consecutive positions at speed = 0 with mems_peak ≈ 103 in the
--       6 minutes that followed (sustained tilt at rest)
--     - no movement above 5 km/h in the following 30 minutes
--     - ~40 km/h cruise before the shock (pre-impact cruise, bonus)
--
-- ALGORITHM (AND hard rules, D is a bonus)
--   A  Anchor pair          ≥2 positions with mems_peak ≥ 100 within 60 s
--   B  Sustained tilt       ≥3 positions with speed ≤ 5 AND mems_peak ≥ 80
--                           within 6 min after the anchor
--   C  Extended immobility  no speed > 5 km/h between anchor+15 min and
--                           anchor+30 min
--   D  Pre-impact cruise    ≥40 km/h within 120 s before the anchor  (bonus)
--
--   Qualification = A ∧ B ∧ C.  D only adds confidence — low-speed city
--   impacts are real and would otherwise be dropped.
--
--   A candidate is deduplicated to ONE per (vehicle_id, local_date) so that
--   the follow-up tow/lift events don't re-fire hours later on the same
--   vehicle the same day.
--
-- TUNING
--   All thresholds live in the `params` CTE at the top of the build step.
--   Change them, re-run, compare output. No schema change required.
--
-- SAFETY
--   Read-only on production tables. Materializes intermediate rows into a
--   session-local TEMP TABLE so the three output sections can share the same
--   computation. The temp table is dropped at the end of the script.
--
-- USAGE (local, docker-compose stack)
--   docker exec -i gisv2-postgres-1 \
--       psql -U postgres -d gis_v2 < scripts/sql/accident_detection_replay.sql
--
-- USAGE (prod, via kubectl — read-only, safe)
--   Namespace: gisv2   Pod: postgres-0   DB: gis_v2   User: postgres
--
--     kubectl -n gisv2 cp scripts/sql/accident_detection_replay.sql \
--         postgres-0:/tmp/accident_detection_replay.sql
--     kubectl -n gisv2 exec -it postgres-0 -- \
--         psql -U postgres -d gis_v2 -f /tmp/accident_detection_replay.sql
--
--   To persist the output to a file on your laptop:
--     kubectl -n gisv2 exec postgres-0 -- \
--         psql -U postgres -d gis_v2 -f /tmp/accident_detection_replay.sql \
--         > replay_$(date +%F_%H%M).log 2>&1
--
-- =============================================================================

\pset pager off
\timing on

-- Drop leftover state from a previous run in the same session.
DROP TABLE IF EXISTS tmp_accident_replay;

-- -----------------------------------------------------------------------------
-- Build the replay result into a TEMP table so that the three output sections
-- below can query it without re-running every CTE. The table is dropped at
-- the end of the script (and automatically when the psql session ends).
-- -----------------------------------------------------------------------------
CREATE TEMP TABLE tmp_accident_replay AS
WITH params AS (
    SELECT
        -- Replay window (default: last 7 days). Widen or narrow as needed.
        (NOW() - INTERVAL '7 days')::timestamptz AS scan_from,
        (NOW())::timestamptz                     AS scan_to,

        -- Optional filters (set to NULL to disable)
        NULL::int   AS filter_company_id,   -- e.g. 4
        NULL::int   AS filter_vehicle_id,   -- e.g. 118013
        NULL::int   AS filter_device_id,    -- e.g. raw gps_devices.id

        -- Condition A — anchor pair
        100.0::numeric AS mems_anchor_threshold,     -- peak magnitude
        60             AS anchor_pair_window_s,      -- seconds between two anchors

        -- Condition B — sustained tilt at rest
        80.0::numeric  AS mems_tilt_threshold,
        5.0::numeric   AS tilt_max_speed_kph,
        3              AS tilt_min_count,
        360            AS tilt_scan_window_s,        -- 6 min after anchor

        -- Condition C — extended immobility
        900            AS immob_start_s,             -- 15 min after anchor
        1800           AS immob_end_s,               -- 30 min after anchor
        5.0::numeric   AS immob_max_speed_kph,

        -- Condition D — pre-impact cruise (bonus, not required)
        120            AS precruise_window_s,
        40.0::numeric  AS precruise_min_speed_kph
),

-- -----------------------------------------------------------------------------
-- 1. Positions in the replay window with mems_peak precomputed.
--    mems_peak = Euclidean magnitude of the 3 accelerometer axes.
--
--    NOT MATERIALIZED forces the planner to inline this CTE into its five
--    references, so each reference can push its device_id + recorded_at filter
--    down to the composite index ix_gps_positions_device_recorded rather than
--    scanning a large materialized tuplestore.
-- -----------------------------------------------------------------------------
scanned AS NOT MATERIALIZED (
    SELECT
        p.id,
        p.device_id,
        p.recorded_at,
        p.latitude,
        p.longitude,
        p.speed_kph,
        p.ignition_on,
        p.send_flag,
        p.mems_x,
        p.mems_y,
        p.mems_z,
        SQRT(
            (p.mems_x::numeric * p.mems_x::numeric) +
            (p.mems_y::numeric * p.mems_y::numeric) +
            (p.mems_z::numeric * p.mems_z::numeric)
        ) AS mems_peak
    FROM gps_positions p
    CROSS JOIN params pr
    WHERE p.recorded_at BETWEEN pr.scan_from AND pr.scan_to
      AND p.mems_x IS NOT NULL
      AND p.mems_y IS NOT NULL
      AND p.mems_z IS NOT NULL
      AND (pr.filter_device_id IS NULL OR p.device_id = pr.filter_device_id)
),

-- -----------------------------------------------------------------------------
-- 2. Anchors — positions whose mems_peak clears the anchor threshold.
-- -----------------------------------------------------------------------------
anchors AS (
    SELECT s.*
    FROM scanned s
    CROSS JOIN params pr
    WHERE s.mems_peak >= pr.mems_anchor_threshold
),

-- -----------------------------------------------------------------------------
-- 3. Condition A — anchor pair within N seconds for the same device.
--    For each anchor, look for the earliest subsequent anchor within the
--    pair window.
-- -----------------------------------------------------------------------------
anchor_pairs AS (
    SELECT
        a1.id                                              AS anchor_id,
        a1.device_id,
        a1.recorded_at                                     AS anchor_at,
        a1.latitude,
        a1.longitude,
        a1.speed_kph                                       AS anchor_speed,
        a1.mems_peak                                       AS anchor_mems,
        a2.id                                              AS companion_id,
        a2.recorded_at                                     AS companion_at,
        a2.mems_peak                                       AS companion_mems,
        EXTRACT(EPOCH FROM (a2.recorded_at - a1.recorded_at))::int AS pair_gap_s
    FROM anchors a1
    CROSS JOIN params pr
    INNER JOIN LATERAL (
        SELECT a.id, a.recorded_at, a.mems_peak
        FROM anchors a
        WHERE a.device_id = a1.device_id
          AND a.recorded_at >  a1.recorded_at
          AND a.recorded_at <= a1.recorded_at + make_interval(secs => pr.anchor_pair_window_s)
        ORDER BY a.recorded_at ASC
        LIMIT 1
    ) a2 ON TRUE
),

-- -----------------------------------------------------------------------------
-- 4. Evaluate B, C, D for each anchor pair.
-- -----------------------------------------------------------------------------
evaluated AS (
    SELECT
        ap.*,

        -- B — sustained tilt: slow + high-mems count after the anchor.
        (
            SELECT COUNT(*)::int
            FROM scanned s
            CROSS JOIN params pr
            WHERE s.device_id = ap.device_id
              AND s.recorded_at >= ap.anchor_at
              AND s.recorded_at <= ap.anchor_at + make_interval(secs => pr.tilt_scan_window_s)
              AND s.speed_kph  <= pr.tilt_max_speed_kph
              AND s.mems_peak  >= pr.mems_tilt_threshold
        ) AS tilt_count,

        -- C — immobility window: max observed speed.
        (
            SELECT COALESCE(MAX(s.speed_kph), 0)::numeric
            FROM scanned s
            CROSS JOIN params pr
            WHERE s.device_id = ap.device_id
              AND s.recorded_at >= ap.anchor_at + make_interval(secs => pr.immob_start_s)
              AND s.recorded_at <= ap.anchor_at + make_interval(secs => pr.immob_end_s)
        ) AS immob_max_speed,

        -- C — immobility window: sample count (to avoid false positives on
        -- zero rows returning 0 max-speed, which would be wrongly "immobile").
        (
            SELECT COUNT(*)::int
            FROM scanned s
            CROSS JOIN params pr
            WHERE s.device_id = ap.device_id
              AND s.recorded_at >= ap.anchor_at + make_interval(secs => pr.immob_start_s)
              AND s.recorded_at <= ap.anchor_at + make_interval(secs => pr.immob_end_s)
        ) AS immob_sample_count,

        -- D — pre-impact cruise: max observed speed in the lead-up window.
        (
            SELECT COALESCE(MAX(s.speed_kph), 0)::numeric
            FROM scanned s
            CROSS JOIN params pr
            WHERE s.device_id = ap.device_id
              AND s.recorded_at >= ap.anchor_at - make_interval(secs => pr.precruise_window_s)
              AND s.recorded_at <  ap.anchor_at
        ) AS precruise_max_speed
    FROM anchor_pairs ap
),

-- -----------------------------------------------------------------------------
-- 5. Apply hard rules and compute confidence (0..100).
-- -----------------------------------------------------------------------------
scored AS (
    SELECT
        e.*,
        (e.tilt_count >= pr.tilt_min_count)                                        AS passes_b,
        (e.immob_sample_count > 0 AND e.immob_max_speed <= pr.immob_max_speed_kph) AS passes_c,
        (e.precruise_max_speed >= pr.precruise_min_speed_kph)                      AS passes_d,
        (
              30                                                              -- A mandatory, already passed
            + CASE WHEN e.tilt_count >= pr.tilt_min_count                     THEN 30 ELSE 0 END
            + CASE WHEN e.immob_sample_count > 0
                   AND  e.immob_max_speed <= pr.immob_max_speed_kph           THEN 25 ELSE 0 END
            + CASE WHEN e.precruise_max_speed >= pr.precruise_min_speed_kph   THEN 15 ELSE 0 END
        )::int AS confidence
    FROM evaluated e
    CROSS JOIN params pr
),

-- -----------------------------------------------------------------------------
-- 6. Join vehicle/company metadata.
--    recorded_at is timestamptz — `AT TIME ZONE 'Africa/Tunis'` yields a naive
--    timestamp in Tunis wall-clock, cast to date for local-day dedup.
-- -----------------------------------------------------------------------------
joined AS (
    SELECT
        s.*,
        gd.device_uid,
        gd.company_id                                                          AS device_company_id,
        v.id                                                                   AS vehicle_id,
        v.company_id                                                           AS vehicle_company_id,
        v.plate_number,
        v.brand                                                                AS vehicle_brand,
        v.model                                                                AS vehicle_model,
        COALESCE(v.company_id, gd.company_id)                                  AS company_id,
        (s.anchor_at AT TIME ZONE 'Africa/Tunis')::date                        AS local_date
    FROM scored s
    LEFT JOIN gps_devices gd ON gd.id = s.device_id
    LEFT JOIN vehicles    v  ON v.gps_device_id = gd.id
),

-- -----------------------------------------------------------------------------
-- 7. Apply optional filters (company / vehicle).
-- -----------------------------------------------------------------------------
filtered AS (
    SELECT j.*
    FROM joined j
    CROSS JOIN params pr
    WHERE (pr.filter_company_id IS NULL OR j.company_id = pr.filter_company_id)
      AND (pr.filter_vehicle_id IS NULL OR j.vehicle_id = pr.filter_vehicle_id)
),

-- -----------------------------------------------------------------------------
-- 8. Rank qualified rows so we can keep only the earliest per
--    (vehicle_id, local_date). Non-qualified rows get rank 0 (no dedup).
-- -----------------------------------------------------------------------------
ranked AS (
    SELECT
        f.*,
        CASE
            WHEN f.passes_b AND f.passes_c THEN
                ROW_NUMBER() OVER (
                    PARTITION BY COALESCE(f.vehicle_id, -f.device_id), f.local_date
                    ORDER BY f.anchor_at ASC
                )
            ELSE 0
        END AS qualified_rank
    FROM filtered f
)

SELECT * FROM ranked;

-- Helpful index for the two output selects below.
CREATE INDEX ON tmp_accident_replay (passes_b, passes_c, qualified_rank);

-- =============================================================================
-- SECTION 1 — QUALIFIED CANDIDATES (A ∧ B ∧ C, earliest per vehicle-day)
-- These are the alerts the detector would raise.
-- =============================================================================
\echo
\echo '============================================================='
\echo 'SECTION 1 — QUALIFIED CANDIDATES (would raise an alert)'
\echo '============================================================='

SELECT
    'QUALIFIED'::text                                                  AS verdict,
    d.company_id,
    d.vehicle_id,
    d.plate_number,
    d.device_id,
    d.device_uid,
    d.anchor_at                                                        AS incident_at_utc,
    to_char((d.anchor_at AT TIME ZONE 'Africa/Tunis'),
            'YYYY-MM-DD HH24:MI:SS')                                   AS incident_at_local,
    ROUND(d.anchor_mems, 1)                                            AS anchor_mems,
    ROUND(d.companion_mems, 1)                                         AS companion_mems,
    d.pair_gap_s,
    d.tilt_count,
    ROUND(d.immob_max_speed, 1)                                        AS immob_max_speed,
    ROUND(d.precruise_max_speed, 1)                                    AS precruise_max_speed,
    d.passes_d                                                         AS cruise_bonus,
    d.confidence,
    d.latitude,
    d.longitude,
    'https://maps.google.com/?q='
        || d.latitude::text || ',' || d.longitude::text                AS map_url
FROM tmp_accident_replay d
WHERE d.passes_b AND d.passes_c AND d.qualified_rank = 1
ORDER BY d.anchor_at DESC;

-- =============================================================================
-- SECTION 2 — NEAR-MISSES (A passed but B and/or C failed)
-- Inspect these to spot real accidents filtered out by the gate, or noise
-- still clearing A. Limited to 200 rows to avoid flooding.
-- =============================================================================
\echo
\echo '============================================================='
\echo 'SECTION 2 — NEAR-MISSES (A cleared, B or C failed)'
\echo '============================================================='

SELECT
    CASE
        WHEN NOT f.passes_b AND NOT f.passes_c THEN 'NEAR_MISS: B+C fail'
        WHEN NOT f.passes_b                    THEN 'NEAR_MISS: B fail — no sustained tilt'
        WHEN NOT f.passes_c                    THEN 'NEAR_MISS: C fail — vehicle moved 15–30 min later'
    END                                                                AS verdict,
    f.company_id,
    f.vehicle_id,
    f.plate_number,
    f.device_id,
    f.device_uid,
    f.anchor_at                                                        AS incident_at_utc,
    to_char((f.anchor_at AT TIME ZONE 'Africa/Tunis'),
            'YYYY-MM-DD HH24:MI:SS')                                   AS incident_at_local,
    ROUND(f.anchor_mems, 1)                                            AS anchor_mems,
    ROUND(f.companion_mems, 1)                                         AS companion_mems,
    f.pair_gap_s,
    f.tilt_count,
    ROUND(f.immob_max_speed, 1)                                        AS immob_max_speed,
    ROUND(f.precruise_max_speed, 1)                                    AS precruise_max_speed,
    f.confidence,
    'https://maps.google.com/?q='
        || f.latitude::text || ',' || f.longitude::text                AS map_url
FROM tmp_accident_replay f
WHERE NOT (f.passes_b AND f.passes_c)
ORDER BY f.anchor_at DESC
LIMIT 200;

-- =============================================================================
-- SECTION 3 — SUMMARY COUNTS
-- =============================================================================
\echo
\echo '============================================================='
\echo 'SECTION 3 — SUMMARY'
\echo '============================================================='

SELECT
    COUNT(*) FILTER (WHERE passes_b AND passes_c AND qualified_rank = 1) AS qualified_candidates,
    COUNT(*) FILTER (WHERE passes_b AND passes_c AND qualified_rank > 1) AS qualified_dupes_dropped,
    COUNT(*) FILTER (WHERE NOT passes_b AND NOT passes_c)                AS near_miss_b_and_c,
    COUNT(*) FILTER (WHERE NOT passes_b AND passes_c)                    AS near_miss_b_only,
    COUNT(*) FILTER (WHERE passes_b AND NOT passes_c)                    AS near_miss_c_only,
    COUNT(*)                                                             AS total_anchor_pairs
FROM tmp_accident_replay;

-- Cleanup
DROP TABLE tmp_accident_replay;
