###############################################################################
# Test Geofence Notification Scenario
# 
# This script tests the full chain:
# 1. Login to get JWT token
# 2. Create a geofence (circle) around Tunis center
# 3. Simulate a vehicle OUTSIDE the geofence (baseline)
# 4. Simulate the vehicle ENTERING the geofence
# 5. Check that a notification was created
###############################################################################

$API_BASE = "http://localhost:5000/api"
$ErrorActionPreference = "Continue"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  GEOFENCE NOTIFICATION TEST SCENARIO" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Login ──
Write-Host "[1/6] Logging in as admin@belive.tn..." -ForegroundColor Yellow
$loginBody = @{ email = "admin@belive.tn"; password = "Admin@2026" } | ConvertTo-Json
try {
    $loginResp = Invoke-RestMethod -Uri "$API_BASE/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    $token = $loginResp.token
    Write-Host "  OK - Token received: $($token.Substring(0, 30))..." -ForegroundColor Green
} catch {
    Write-Host "  FAIL - Login error: $_" -ForegroundColor Red
    exit 1
}

$headers = @{ Authorization = "Bearer $token" }

# ── Step 2: Create a test geofence ──
# Circle centered at Tunis center (36.8065, 10.1815) with 500m radius
Write-Host ""
Write-Host "[2/6] Creating test geofence 'Zone Test Tunis'..." -ForegroundColor Yellow

$geofenceBody = @{
    name = "Zone Test Tunis"
    description = "Geofence de test pour notifications"
    type = "circle"
    color = "#FF5722"
    centerLat = 36.8065
    centerLng = 10.1815
    radius = 500
    coordinates = @()
    alertOnEntry = $true
    alertOnExit = $true
    isActive = $true
} | ConvertTo-Json

try {
    $gfResp = Invoke-RestMethod -Uri "$API_BASE/geofences" -Method POST -Body $geofenceBody -ContentType "application/json" -Headers $headers
    $geofenceId = $gfResp.id
    Write-Host "  OK - Geofence created: ID=$geofenceId, Name='$($gfResp.name)'" -ForegroundColor Green
    Write-Host "  Center: ($($gfResp.centerLat), $($gfResp.centerLng)), Radius: $($gfResp.radius)m" -ForegroundColor Gray
    Write-Host "  AlertOnEntry: $($gfResp.alertOnEntry), AlertOnExit: $($gfResp.alertOnExit)" -ForegroundColor Gray
} catch {
    Write-Host "  FAIL - Geofence creation error: $_" -ForegroundColor Red
    Write-Host "  Response: $($_.Exception.Response)" -ForegroundColor Red
    exit 1
}

# ── Step 3: Check current notifications count ──
Write-Host ""
Write-Host "[3/6] Checking current notification count..." -ForegroundColor Yellow
try {
    $notifsBefore = Invoke-RestMethod -Uri "$API_BASE/notifications?page=1&pageSize=5" -Method GET -Headers $headers
    $countBefore = $notifsBefore.totalCount
    Write-Host "  OK - Current notifications: $countBefore" -ForegroundColor Green
} catch {
    Write-Host "  WARN - Could not fetch notifications: $_" -ForegroundColor DarkYellow
    $countBefore = 0
}

# ── Step 4: Simulate vehicle OUTSIDE geofence ──
# Position: 36.78, 10.15 (about 3km south of the geofence center)
Write-Host ""
Write-Host "[4/6] Simulating vehicle OUTSIDE geofence (lat=36.78, lng=10.15)..." -ForegroundColor Yellow

$outsideBody = @{
    vehicleId = 3
    latitude = 36.78
    longitude = 10.15
    speedKph = 60
} | ConvertTo-Json

try {
    $outsideResp = Invoke-RestMethod -Uri "$API_BASE/gps/test/simulate-position" -Method POST -Body $outsideBody -ContentType "application/json" -Headers $headers
    Write-Host "  OK - Vehicle outside geofence: broadcasted=$($outsideResp.broadcasted)" -ForegroundColor Green
    if ($outsideResp.skipReason) {
        Write-Host "  Skip reason: $($outsideResp.skipReason)" -ForegroundColor DarkYellow
    }
} catch {
    Write-Host "  FAIL - Simulate position error: $_" -ForegroundColor Red
}

Write-Host "  Waiting 3s for geofence cache to register..." -ForegroundColor Gray
Start-Sleep -Seconds 3

# ── Step 5: Simulate vehicle ENTERING geofence ──
# Position: 36.8065, 10.1815 (center of the geofence)
Write-Host ""
Write-Host "[5/6] Simulating vehicle ENTERING geofence (lat=36.8065, lng=10.1815)..." -ForegroundColor Yellow

$insideBody = @{
    vehicleId = 3
    latitude = 36.8065
    longitude = 10.1815
    speedKph = 30
} | ConvertTo-Json

try {
    $insideResp = Invoke-RestMethod -Uri "$API_BASE/gps/test/simulate-position" -Method POST -Body $insideBody -ContentType "application/json" -Headers $headers
    Write-Host "  OK - Vehicle inside geofence: broadcasted=$($insideResp.broadcasted)" -ForegroundColor Green
    if ($insideResp.skipReason) {
        Write-Host "  Skip reason: $($insideResp.skipReason)" -ForegroundColor DarkYellow
    }
} catch {
    Write-Host "  FAIL - Simulate position error: $_" -ForegroundColor Red
}

Start-Sleep -Seconds 3

# ── Step 6: Check notifications ──
Write-Host ""
Write-Host "[6/6] Checking notifications after geofence entry..." -ForegroundColor Yellow
try {
    $notifsAfter = Invoke-RestMethod -Uri "$API_BASE/notifications?page=1&pageSize=10" -Method GET -Headers $headers
    $countAfter = $notifsAfter.totalCount
    $newCount = $countAfter - $countBefore
    
    Write-Host "  Notifications before: $countBefore" -ForegroundColor Gray
    Write-Host "  Notifications after:  $countAfter" -ForegroundColor Gray
    
    if ($newCount -gt 0) {
        Write-Host "  NEW NOTIFICATIONS: $newCount" -ForegroundColor Green
        Write-Host ""
        Write-Host "  Latest notifications:" -ForegroundColor Cyan
        foreach ($notif in $notifsAfter.items | Select-Object -First 5) {
            $icon = if ($notif.type -eq "geofence_event") { "[GEOFENCE]" } else { "[$($notif.type)]" }
            $readStatus = if ($notif.isRead) { "read" } else { "UNREAD" }
            Write-Host "    $icon $($notif.title) - $($notif.message) [$readStatus]" -ForegroundColor White
        }
    } else {
        Write-Host "  No new notifications detected." -ForegroundColor DarkYellow
        Write-Host "  Checking DB directly..." -ForegroundColor Gray
    }
} catch {
    Write-Host "  FAIL - Notification check error: $_" -ForegroundColor Red
}

# ── Also check DB directly for geofence notifications ──
Write-Host ""
Write-Host "--- Checking DB for geofence_event notifications ---" -ForegroundColor Cyan
$sql = 'SELECT n."Id", n."Type", n."Title", n."Message", n."CreatedAt" FROM notifications n WHERE n."Type"=$$geofence_event$$ ORDER BY n."CreatedAt" DESC LIMIT 5;'
docker exec gisv2-postgres-1 psql -U postgres -d gis_v2 -c $sql

# ── Cleanup: Delete the test geofence ──
Write-Host ""
Write-Host "--- Cleanup: Deleting test geofence ---" -ForegroundColor DarkGray
try {
    Invoke-RestMethod -Uri "$API_BASE/geofences/$geofenceId" -Method DELETE -Headers $headers | Out-Null
    Write-Host "  Geofence $geofenceId deleted." -ForegroundColor DarkGray
} catch {
    Write-Host "  Could not delete geofence: $_" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  TEST SCENARIO COMPLETE" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To test the UI notification:" -ForegroundColor Yellow
Write-Host "  1. Open http://localhost:4200 in your browser" -ForegroundColor White
Write-Host "  2. Login as admin@belive.tn / Admin@2026" -ForegroundColor White
Write-Host "  3. Click the bell icon in the top nav" -ForegroundColor White
Write-Host "  4. You should see the geofence notification" -ForegroundColor White
