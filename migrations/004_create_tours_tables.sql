-- Migration: Create Tour Management tables
-- Date: 2026-02-17
-- Description: Adds tours, tour_waypoints, and tour_pauses tables for the Tour Management feature

-- ═══════════════════════════════════════════════════════
-- 1. Tours table
-- ═══════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tours (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "VehicleId" INT NOT NULL,
    "DriverId" INT,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'planned',
    "ScheduledStartTime" TIMESTAMP NOT NULL DEFAULT NOW(),
    "ScheduledEndTime" TIMESTAMP,
    "ActualStartTime" TIMESTAMP,
    "ActualEndTime" TIMESTAMP,
    "EstimatedDistanceKm" DECIMAL(10,2) NOT NULL DEFAULT 0,
    "EstimatedDurationMinutes" INT NOT NULL DEFAULT 0,
    "EstimatedFuelLiters" DECIMAL(10,2),
    "EstimatedRoutePolyline" TEXT,
    "ActualDistanceKm" DECIMAL(10,2),
    "ActualDurationMinutes" INT,
    "ActualFuelLiters" DECIMAL(10,2),
    "ActualRoutePolyline" TEXT,
    "TotalPauseMinutes" INT NOT NULL DEFAULT 0,
    "Recurrence" VARCHAR(50) NOT NULL DEFAULT 'none',
    "Notes" TEXT,
    "CompanyId" INT NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ═══════════════════════════════════════════════════════
-- 2. Tour Waypoints table
-- ═══════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tour_waypoints (
    "Id" SERIAL PRIMARY KEY,
    "TourId" INT NOT NULL REFERENCES tours("Id") ON DELETE CASCADE,
    "SequenceOrder" INT NOT NULL DEFAULT 0,
    "Name" VARCHAR(255),
    "Address" TEXT,
    "Latitude" DOUBLE PRECISION NOT NULL,
    "Longitude" DOUBLE PRECISION NOT NULL,
    "Type" VARCHAR(50) NOT NULL DEFAULT 'waypoint',
    "EstimatedArrivalTime" TIMESTAMP,
    "ActualArrivalTime" TIMESTAMP,
    "PlannedPauseMinutes" INT NOT NULL DEFAULT 0,
    "ActualPauseMinutes" INT,
    "IsCompleted" BOOLEAN NOT NULL DEFAULT FALSE
);

-- ═══════════════════════════════════════════════════════
-- 3. Tour Pauses table
-- ═══════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tour_pauses (
    "Id" SERIAL PRIMARY KEY,
    "TourId" INT NOT NULL REFERENCES tours("Id") ON DELETE CASCADE,
    "StartTime" TIMESTAMP NOT NULL,
    "EndTime" TIMESTAMP,
    "DurationMinutes" INT,
    "Latitude" DOUBLE PRECISION,
    "Longitude" DOUBLE PRECISION,
    "Reason" VARCHAR(50) NOT NULL DEFAULT 'break',
    "Notes" TEXT
);

-- ═══════════════════════════════════════════════════════
-- 4. Indexes for performance
-- ═══════════════════════════════════════════════════════
CREATE INDEX IF NOT EXISTS idx_tours_company ON tours("CompanyId");
CREATE INDEX IF NOT EXISTS idx_tours_status ON tours("Status");
CREATE INDEX IF NOT EXISTS idx_tours_vehicle ON tours("VehicleId");
CREATE INDEX IF NOT EXISTS idx_tours_driver ON tours("DriverId");
CREATE INDEX IF NOT EXISTS idx_tours_scheduled ON tours("ScheduledStartTime");
CREATE INDEX IF NOT EXISTS idx_tour_waypoints_tour ON tour_waypoints("TourId");
CREATE INDEX IF NOT EXISTS idx_tour_pauses_tour ON tour_pauses("TourId");
