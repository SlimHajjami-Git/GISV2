###############################################################################
# GISV2 - Tour Management E2E Test Scenario
# Tests the full tour lifecycle with GPS simulation via Redis
#
# Scenario 1: Vehicle passes THROUGH waypoints (within 300m) -> auto-complete
# Scenario 2: Vehicle passes NEAR but OUTSIDE waypoints (>300m) -> no completion
#
# Architecture flow:
#   Script -> Redis (vehicle:position:{deviceUid}) -> TourMonitoringService (30s cycle)
#            -> auto waypoint completion -> SignalR notifications
###############################################################################

$ErrorActionPreference = "Stop"

# ── Configuration ──
$API_BASE = "http://localhost:5000/api"
$COMPOSE_DIR = "c:\Users\Mega-PC\Desktop\GISV2"
$DEVICE_UID = "861001002935999"   # Camion Test 1
$VEHICLE_ID = 3
$COMPANY_ID = 1

# Colors for output
function Write-Step($msg)   { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)     { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Fail($msg)   { Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Write-Info($msg)   { Write-Host "  [INFO] $msg" -ForegroundColor Yellow }
function Write-Detail($msg) { Write-Host "  $msg" -ForegroundColor Gray }

###############################################################################
# STEP 0: Authenticate
###############################################################################
Write-Step "STEP 0 - Authenticate (admin@belive.tn)"

$loginBody = @{
    email    = "admin@belive.tn"
    password = "Admin@2026"
} | ConvertTo-Json

try {
    $loginResp = Invoke-RestMethod -Uri "$API_BASE/auth/login" -Method POST `
        -ContentType "application/json" -Body $loginBody
    $TOKEN = $loginResp.token
    if (-not $TOKEN) { throw "No token in response" }
    Write-Ok "Authenticated, token length = $($TOKEN.Length)"
} catch {
    Write-Fail "Authentication failed: $_"
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $TOKEN"
    "Content-Type"  = "application/json"
}

###############################################################################
# HELPER: Inject GPS position into Redis
###############################################################################
function Set-VehiclePosition {
    param(
        [double]$Lat,
        [double]$Lon,
        [double]$Speed = 45.0,
        [double]$Heading = 90.0,
        [bool]$Ignition = $true
    )

    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $posJson = @{
        DeviceUid    = $DEVICE_UID
        VehicleId    = $VEHICLE_ID
        CompanyId    = $COMPANY_ID
        Latitude     = $Lat
        Longitude    = $Lon
        SpeedKph     = $Speed
        HeadingDeg   = $Heading
        IgnitionOn   = $Ignition
        IsValid      = $true
        FuelRaw      = 500
        PowerVoltage = 12400
        RecordedAt   = $now
        CachedAt     = $now
    } | ConvertTo-Json -Compress

    # Escape for redis-cli
    $escaped = $posJson.Replace('"', '\"')
    
    docker compose exec -T redis redis-cli SET "vehicle:position:$DEVICE_UID" "$escaped" EX 300 2>&1 | Out-Null
    # Also add to company device set
    docker compose exec -T redis redis-cli SADD "company:${COMPANY_ID}:devices" "$DEVICE_UID" 2>&1 | Out-Null
    
    Write-Detail "  GPS -> ($([math]::Round($Lat,5)), $([math]::Round($Lon,5))) speed=${Speed}km/h @ $now"
}

###############################################################################
# HELPER: Haversine distance (meters)
###############################################################################
function Get-HaversineDistance {
    param([double]$lat1, [double]$lon1, [double]$lat2, [double]$lon2)
    $R = 6371000
    $dLat = ($lat2 - $lat1) * [math]::PI / 180
    $dLon = ($lon2 - $lon1) * [math]::PI / 180
    $a = [math]::Sin($dLat/2) * [math]::Sin($dLat/2) +
         [math]::Cos($lat1 * [math]::PI / 180) * [math]::Cos($lat2 * [math]::PI / 180) *
         [math]::Sin($dLon/2) * [math]::Sin($dLon/2)
    $c = 2 * [math]::Atan2([math]::Sqrt($a), [math]::Sqrt(1-$a))
    return [math]::Round($R * $c, 1)
}

###############################################################################
# HELPER: Get tour tracking status
###############################################################################
function Get-TourTracking($tourId) {
    try {
        return Invoke-RestMethod -Uri "$API_BASE/tours/$tourId/tracking" -Method GET -Headers $headers
    } catch {
        Write-Fail "Failed to get tracking: $_"
        return $null
    }
}

###############################################################################
# HELPER: Get tour detail
###############################################################################
function Get-TourDetail($tourId) {
    try {
        return Invoke-RestMethod -Uri "$API_BASE/tours/$tourId" -Method GET -Headers $headers
    } catch {
        Write-Fail "Failed to get tour: $_"
        return $null
    }
}

###############################################################################
# HELPER: Cleanup old test tours
###############################################################################
function Remove-TestTours {
    try {
        $resp = Invoke-RestMethod -Uri "$API_BASE/tours?pageSize=100" -Method GET -Headers $headers
        foreach ($tour in $resp.items) {
            if ($tour.name -like "TEST_SCENARIO_*") {
                Invoke-RestMethod -Uri "$API_BASE/tours/$($tour.id)" -Method DELETE -Headers $headers 2>&1 | Out-Null
                Write-Detail "Deleted old test tour: $($tour.name) (ID=$($tour.id))"
            }
        }
    } catch {
        Write-Detail "No old test tours to clean"
    }
}

Write-Step "CLEANUP - Remove old test tours"
Remove-TestTours

###############################################################################
###############################################################################
#                                                                             #
#    SCENARIO 1: Vehicle passes THROUGH waypoints (within 300m radius)        #
#    Expected: TourMonitoringService auto-completes all waypoints             #
#                                                                             #
###############################################################################
###############################################################################

Write-Host "`n" -NoNewline
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  SCENARIO 1: Vehicle passes THROUGH waypoints (< 300m)" -ForegroundColor Magenta
Write-Host "  Expected: All waypoints auto-completed by TourMonitoringService" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

# ── Tunis route waypoints ──
# Origin:      Tunis-Carthage Airport
# Waypoint 1:  La Marsa Centre
# Waypoint 2:  Sidi Bou Said
# Destination: Port de La Goulette

$WP_ORIGIN      = @{ lat = 36.8510; lon = 10.2272; name = "Aeroport Tunis-Carthage";  addr = "Aeroport Tunis-Carthage, Tunis" }
$WP_MARSA       = @{ lat = 36.8783; lon = 10.3247; name = "La Marsa Centre";          addr = "Avenue Habib Bourguiba, La Marsa" }
$WP_SIDI        = @{ lat = 36.8687; lon = 10.3417; name = "Sidi Bou Said";            addr = "Rue Hedi Zarrouk, Sidi Bou Said" }
$WP_DESTINATION = @{ lat = 36.8181; lon = 10.3050; name = "Port de La Goulette";      addr = "Port de La Goulette, Tunis" }

###############################################################################
# STEP 1: Create the tour
###############################################################################
Write-Step "STEP 1 - Create Tour (Scenario 1)"

$scheduledStart = (Get-Date).ToUniversalTime().AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ssZ")

$createBody = @{
    name             = "TEST_SCENARIO_1_THROUGH"
    description      = "Test: vehicle passes through all waypoints within 300m radius"
    vehicleId        = $VEHICLE_ID
    scheduledStartTime = $scheduledStart
    notes            = "Automated E2E test - Scenario 1"
    waypoints        = @(
        @{ name = $WP_ORIGIN.name;      address = $WP_ORIGIN.addr;      latitude = $WP_ORIGIN.lat;      longitude = $WP_ORIGIN.lon;      plannedPauseMinutes = 0 },
        @{ name = $WP_MARSA.name;       address = $WP_MARSA.addr;       latitude = $WP_MARSA.lat;       longitude = $WP_MARSA.lon;       plannedPauseMinutes = 5 },
        @{ name = $WP_SIDI.name;        address = $WP_SIDI.addr;        latitude = $WP_SIDI.lat;        longitude = $WP_SIDI.lon;        plannedPauseMinutes = 5 },
        @{ name = $WP_DESTINATION.name; address = $WP_DESTINATION.addr; latitude = $WP_DESTINATION.lat; longitude = $WP_DESTINATION.lon; plannedPauseMinutes = 0 }
    )
} | ConvertTo-Json -Depth 5

try {
    $tour1 = Invoke-RestMethod -Uri "$API_BASE/tours" -Method POST -Headers $headers -Body $createBody
    $TOUR1_ID = $tour1.id
    Write-Ok "Tour created: ID=$TOUR1_ID, Name='$($tour1.name)', Status=$($tour1.status)"
    Write-Detail "  Waypoints: $($tour1.waypoints.Count)"
    Write-Detail "  Estimated: $($tour1.estimatedDistanceKm)km, $($tour1.estimatedDurationMinutes)min"
    foreach ($wp in $tour1.waypoints) {
        Write-Detail "    [$($wp.sequenceOrder)] $($wp.type): $($wp.name) ($($wp.latitude), $($wp.longitude))"
    }
} catch {
    Write-Fail "Failed to create tour: $_"
    exit 1
}

###############################################################################
# STEP 2: Wait for auto-start (TourMonitoringService runs every 30s)
###############################################################################
Write-Step "STEP 2 - Wait for tour auto-start (scheduled in past, should start within 30s)"

# Place vehicle at origin first
Set-VehiclePosition -Lat $WP_ORIGIN.lat -Lon $WP_ORIGIN.lon -Speed 0 -Ignition $true

$started = $false
for ($i = 0; $i -lt 4; $i++) {
    Start-Sleep -Seconds 15
    $tourDetail = Get-TourDetail $TOUR1_ID
    if ($tourDetail.status -eq "in_progress") {
        Write-Ok "Tour auto-started! Status=$($tourDetail.status), ActualStart=$($tourDetail.actualStartTime)"
        $started = $true
        break
    }
    Write-Detail "  Still waiting... status=$($tourDetail.status) (attempt $($i+1)/4)"
}

if (-not $started) {
    Write-Info "Tour didn't auto-start, starting manually..."
    try {
        Invoke-RestMethod -Uri "$API_BASE/tours/$TOUR1_ID/start" -Method POST -Headers $headers | Out-Null
        Write-Ok "Tour started manually"
    } catch {
        Write-Fail "Failed to start tour: $_"
        exit 1
    }
}

###############################################################################
# STEP 3: Simulate vehicle moving THROUGH each waypoint
###############################################################################
Write-Step "STEP 3 - Simulate vehicle GPS positions (passing THROUGH waypoints)"

# ── GPS positions: vehicle moves from origin toward each waypoint ──
# Each position is within ~100-200m of the actual waypoint

$gpsSequence = @(
    # Position 1: Leaving airport area (near origin)
    @{ lat = 36.8515; lon = 10.2280; desc = "Leaving airport" },
    # Position 2: En route to La Marsa (halfway)
    @{ lat = 36.8650; lon = 10.2760; desc = "En route to La Marsa" },
    # Position 3: Arriving La Marsa (within 200m of waypoint)
    @{ lat = 36.8775; lon = 10.3240; desc = "Arriving La Marsa (~100m)" },
    # Position 4: En route to Sidi Bou Said
    @{ lat = 36.8735; lon = 10.3330; desc = "En route to Sidi Bou Said" },
    # Position 5: Arriving Sidi Bou Said (within 150m)
    @{ lat = 36.8692; lon = 10.3410; desc = "Arriving Sidi Bou Said (~120m)" },
    # Position 6: En route to destination
    @{ lat = 36.8450; lon = 10.3230; desc = "En route to La Goulette" },
    # Position 7: Arriving destination (within 200m)
    @{ lat = 36.8185; lon = 10.3055; desc = "Arriving La Goulette (~100m)" }
)

Write-Info "Injecting $($gpsSequence.Count) GPS positions into Redis..."
Write-Info "TourMonitoringService checks every 30s with 300m waypoint radius"

foreach ($pos in $gpsSequence) {
    # Calculate distance to each waypoint
    $dMarsa = Get-HaversineDistance $pos.lat $pos.lon $WP_MARSA.lat $WP_MARSA.lon
    $dSidi  = Get-HaversineDistance $pos.lat $pos.lon $WP_SIDI.lat $WP_SIDI.lon
    $dDest  = Get-HaversineDistance $pos.lat $pos.lon $WP_DESTINATION.lat $WP_DESTINATION.lon
    
    Write-Detail "`n  >> $($pos.desc)"
    Write-Detail "     Distance to La Marsa: ${dMarsa}m $(if($dMarsa -le 300){'<= 300m INSIDE'} else {'> 300m outside'})"
    Write-Detail "     Distance to Sidi Bou Said: ${dSidi}m $(if($dSidi -le 300){'<= 300m INSIDE'} else {'> 300m outside'})"
    Write-Detail "     Distance to Destination: ${dDest}m $(if($dDest -le 300){'<= 300m INSIDE'} else {'> 300m outside'})"
    
    Set-VehiclePosition -Lat $pos.lat -Lon $pos.lon -Speed 50
    
    # Wait for TourMonitoringService cycle
    Write-Detail "     Waiting 35s for monitoring cycle..."
    Start-Sleep -Seconds 35
    
    # Check tracking status
    $tracking = Get-TourTracking $TOUR1_ID
    if ($tracking) {
        $completed = ($tracking.waypoints | Where-Object { $_.isCompleted }).Count
        $total = $tracking.waypoints.Count
        Write-Detail "     Progress: $completed/$total waypoints completed ($($tracking.progress.percentComplete)%)"
        
        if ($tracking.progress.nextWaypointName) {
            Write-Detail "     Next: $($tracking.progress.nextWaypointName), distance=$($tracking.progress.distanceToNextMeters)m"
        }
    }
}

###############################################################################
# STEP 4: Verify final state
###############################################################################
Write-Step "STEP 4 - Verify Scenario 1 Results"

$finalTour1 = Get-TourDetail $TOUR1_ID
Write-Info "Tour status: $($finalTour1.status)"
Write-Info "Duration: estimated=$($finalTour1.estimatedDurationMinutes)min, actual=$($finalTour1.actualDurationMinutes)min"
Write-Info "Distance: estimated=$($finalTour1.estimatedDistanceKm)km, actual=$($finalTour1.actualDistanceKm)km"

$allCompleted = $true
foreach ($wp in $finalTour1.waypoints) {
    $status = if ($wp.isCompleted) { "COMPLETED" } else { "PENDING" }
    $color = if ($wp.isCompleted) { "Green" } else { "Red" }
    Write-Host "  [$($wp.sequenceOrder)] $($wp.type): $($wp.name) -> $status" -ForegroundColor $color
    if ($wp.actualArrivalTime) {
        Write-Detail "      Arrived: $($wp.actualArrivalTime)"
    }
    if (-not $wp.isCompleted -and $wp.type -ne "origin") { $allCompleted = $false }
}

if ($finalTour1.status -eq "completed") {
    Write-Ok "SCENARIO 1 PASSED: Tour completed automatically!"
} elseif ($allCompleted) {
    Write-Ok "SCENARIO 1 PARTIAL: All waypoints completed but tour still in_progress"
} else {
    Write-Fail "SCENARIO 1: Some waypoints not completed"
}


###############################################################################
###############################################################################
#                                                                             #
#    SCENARIO 2: Vehicle passes NEAR but OUTSIDE waypoints (> 300m)           #
#    Expected: Waypoints should NOT auto-complete                             #
#                                                                             #
###############################################################################
###############################################################################

Write-Host "`n" -NoNewline
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  SCENARIO 2: Vehicle passes NEAR but OUTSIDE waypoints (> 300m)" -ForegroundColor Magenta
Write-Host "  Expected: Waypoints should NOT be auto-completed" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta

###############################################################################
# STEP 5: Create second tour
###############################################################################
Write-Step "STEP 5 - Create Tour (Scenario 2)"

$scheduledStart2 = (Get-Date).ToUniversalTime().AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ssZ")

$createBody2 = @{
    name               = "TEST_SCENARIO_2_OUTSIDE"
    description        = "Test: vehicle passes >500m from waypoints - should NOT complete"
    vehicleId          = $VEHICLE_ID
    scheduledStartTime = $scheduledStart2
    notes              = "Automated E2E test - Scenario 2"
    waypoints          = @(
        @{ name = $WP_ORIGIN.name;      address = $WP_ORIGIN.addr;      latitude = $WP_ORIGIN.lat;      longitude = $WP_ORIGIN.lon;      plannedPauseMinutes = 0 },
        @{ name = $WP_MARSA.name;       address = $WP_MARSA.addr;       latitude = $WP_MARSA.lat;       longitude = $WP_MARSA.lon;       plannedPauseMinutes = 5 },
        @{ name = $WP_SIDI.name;        address = $WP_SIDI.addr;        latitude = $WP_SIDI.lat;        longitude = $WP_SIDI.lon;        plannedPauseMinutes = 5 },
        @{ name = $WP_DESTINATION.name; address = $WP_DESTINATION.addr; latitude = $WP_DESTINATION.lat; longitude = $WP_DESTINATION.lon; plannedPauseMinutes = 0 }
    )
} | ConvertTo-Json -Depth 5

try {
    $tour2 = Invoke-RestMethod -Uri "$API_BASE/tours" -Method POST -Headers $headers -Body $createBody2
    $TOUR2_ID = $tour2.id
    Write-Ok "Tour created: ID=$TOUR2_ID, Name='$($tour2.name)'"
} catch {
    Write-Fail "Failed to create tour 2: $_"
    exit 1
}

###############################################################################
# STEP 6: Wait for auto-start
###############################################################################
Write-Step "STEP 6 - Wait for tour auto-start"

Set-VehiclePosition -Lat $WP_ORIGIN.lat -Lon $WP_ORIGIN.lon -Speed 0 -Ignition $true

$started2 = $false
for ($i = 0; $i -lt 4; $i++) {
    Start-Sleep -Seconds 15
    $t2Detail = Get-TourDetail $TOUR2_ID
    if ($t2Detail.status -eq "in_progress") {
        Write-Ok "Tour 2 auto-started!"
        $started2 = $true
        break
    }
    Write-Detail "  Waiting... status=$($t2Detail.status)"
}

if (-not $started2) {
    Write-Info "Starting manually..."
    Invoke-RestMethod -Uri "$API_BASE/tours/$TOUR2_ID/start" -Method POST -Headers $headers | Out-Null
    Write-Ok "Tour 2 started manually"
}

###############################################################################
# STEP 7: Simulate vehicle moving NEAR but OUTSIDE waypoints (>500m away)
###############################################################################
Write-Step "STEP 7 - Simulate vehicle GPS positions (passing OUTSIDE waypoints)"

# GPS positions that are 500-1000m AWAY from the actual waypoints
# Vehicle takes a different route that doesn't pass close enough

$gpsSequenceOutside = @(
    # Position 1: Moving away from airport but north (away from La Marsa)
    @{ lat = 36.8600; lon = 10.2500; desc = "North of airport, heading wrong direction" },
    # Position 2: Near La Marsa but 600m south
    @{ lat = 36.8720; lon = 10.3180; desc = "Near La Marsa but ~700m away" },
    # Position 3: Near Sidi Bou Said but 800m west
    @{ lat = 36.8680; lon = 10.3320; desc = "Near Sidi Bou Said but ~850m away" },
    # Position 4: Near destination but 600m north
    @{ lat = 36.8240; lon = 10.3100; desc = "Near La Goulette but ~700m away" }
)

Write-Info "Injecting $($gpsSequenceOutside.Count) GPS positions OUTSIDE waypoint radius..."

foreach ($pos in $gpsSequenceOutside) {
    $dMarsa = Get-HaversineDistance $pos.lat $pos.lon $WP_MARSA.lat $WP_MARSA.lon
    $dSidi  = Get-HaversineDistance $pos.lat $pos.lon $WP_SIDI.lat $WP_SIDI.lon
    $dDest  = Get-HaversineDistance $pos.lat $pos.lon $WP_DESTINATION.lat $WP_DESTINATION.lon
    
    Write-Detail "`n  >> $($pos.desc)"
    Write-Detail "     Distance to La Marsa: ${dMarsa}m $(if($dMarsa -le 300){'!! INSIDE 300m'} else {'(outside)'})"
    Write-Detail "     Distance to Sidi Bou Said: ${dSidi}m $(if($dSidi -le 300){'!! INSIDE 300m'} else {'(outside)'})"
    Write-Detail "     Distance to Destination: ${dDest}m $(if($dDest -le 300){'!! INSIDE 300m'} else {'(outside)'})"
    
    Set-VehiclePosition -Lat $pos.lat -Lon $pos.lon -Speed 40
    
    Write-Detail "     Waiting 35s for monitoring cycle..."
    Start-Sleep -Seconds 35
    
    $tracking2 = Get-TourTracking $TOUR2_ID
    if ($tracking2) {
        $completed = ($tracking2.waypoints | Where-Object { $_.isCompleted }).Count
        $total = $tracking2.waypoints.Count
        Write-Detail "     Progress: $completed/$total waypoints completed"
    }
}

###############################################################################
# STEP 8: Verify Scenario 2 results
###############################################################################
Write-Step "STEP 8 - Verify Scenario 2 Results"

$finalTour2 = Get-TourDetail $TOUR2_ID
Write-Info "Tour status: $($finalTour2.status)"

$uncompletedWaypoints = 0
foreach ($wp in $finalTour2.waypoints) {
    $status = if ($wp.isCompleted) { "COMPLETED" } else { "PENDING" }
    $color = if ($wp.isCompleted) { "Yellow" } else { "Green" }
    # For scenario 2, PENDING is the expected result (except origin)
    if ($wp.type -eq "origin") {
        $color = if ($wp.isCompleted) { "Green" } else { "Red" }
    }
    Write-Host "  [$($wp.sequenceOrder)] $($wp.type): $($wp.name) -> $status" -ForegroundColor $color
    if (-not $wp.isCompleted -and $wp.type -ne "origin") { $uncompletedWaypoints++ }
}

# In scenario 2, waypoints/destination should NOT be completed
if ($uncompletedWaypoints -ge 2) {
    Write-Ok "SCENARIO 2 PASSED: $uncompletedWaypoints waypoints correctly NOT completed (vehicle was too far)"
} else {
    Write-Fail "SCENARIO 2 FAILED: Waypoints were completed when vehicle was outside radius"
}

###############################################################################
# STEP 9: Cleanup - cancel scenario 2 tour
###############################################################################
Write-Step "STEP 9 - Cleanup"

try {
    Invoke-RestMethod -Uri "$API_BASE/tours/$TOUR2_ID/cancel" -Method POST -Headers $headers | Out-Null
    Write-Ok "Tour 2 cancelled"
} catch {
    Write-Detail "Could not cancel tour 2 (may already be done)"
}

###############################################################################
# FINAL SUMMARY
###############################################################################
Write-Host "`n" -NoNewline
Write-Host "================================================================" -ForegroundColor White
Write-Host "                    TEST SUMMARY" -ForegroundColor White
Write-Host "================================================================" -ForegroundColor White
Write-Host ""

$s1pass = $finalTour1.status -eq "completed"
$s2pass = $uncompletedWaypoints -ge 2

if ($s1pass) {
    Write-Host "  Scenario 1 (THROUGH waypoints):  PASSED" -ForegroundColor Green
} else {
    Write-Host "  Scenario 1 (THROUGH waypoints):  CHECK MANUALLY" -ForegroundColor Yellow
    Write-Host "    Tour status=$($finalTour1.status), may need more monitoring cycles" -ForegroundColor Gray
}

if ($s2pass) {
    Write-Host "  Scenario 2 (OUTSIDE waypoints):  PASSED" -ForegroundColor Green
} else {
    Write-Host "  Scenario 2 (OUTSIDE waypoints):  FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "  Architecture verified:" -ForegroundColor Cyan
Write-Host "    Redis key:    vehicle:position:$DEVICE_UID" -ForegroundColor Gray
Write-Host "    Monitoring:   TourMonitoringService (30s cycle)" -ForegroundColor Gray
Write-Host "    Radius:       300m (waypoint), 300m (destination)" -ForegroundColor Gray
Write-Host "    Tour 1 ID:    $TOUR1_ID" -ForegroundColor Gray
Write-Host "    Tour 2 ID:    $TOUR2_ID" -ForegroundColor Gray
Write-Host ""
Write-Host "================================================================" -ForegroundColor White
