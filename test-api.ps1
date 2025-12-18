# Test API Endpoints Script
Write-Host "=== Test API Endpoints ===" -ForegroundColor Cyan

$baseUrl = "http://localhost:5000/api"
$token = $null

# Helper function for API calls
function Invoke-Api {
    param(
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null,
        [switch]$Auth
    )
    
    $headers = @{ "Content-Type" = "application/json" }
    if ($Auth -and $script:token) {
        $headers["Authorization"] = "Bearer $($script:token)"
    }
    
    $params = @{
        Uri = "$baseUrl$Endpoint"
        Method = $Method
        Headers = $headers
        ErrorAction = "Stop"
    }
    
    if ($Body) {
        $params["Body"] = ($Body | ConvertTo-Json -Depth 10)
    }
    
    try {
        return Invoke-RestMethod @params
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# ==================== AUTH TESTS ====================
Write-Host "`n[1] TEST AUTH - Login" -ForegroundColor Yellow
$loginResult = Invoke-Api -Method POST -Endpoint "/auth/login" -Body @{
    email = "test@example.com"
    password = "Test123!"
}
if ($loginResult -and $loginResult.token) {
    $script:token = $loginResult.token
    Write-Host "  OK: Login successful, token received" -ForegroundColor Green
    Write-Host "  User: $($loginResult.user.name) (ID: $($loginResult.user.id))" -ForegroundColor Gray
} else {
    Write-Host "  FAIL: No token received" -ForegroundColor Red
    exit 1
}

# ==================== VEHICLES TESTS ====================
Write-Host "`n[2] TEST VEHICLES - List" -ForegroundColor Yellow
$vehicles = Invoke-Api -Method GET -Endpoint "/vehicles" -Auth
if ($vehicles -ne $null) {
    Write-Host "  OK: Got $($vehicles.Count) vehicles" -ForegroundColor Green
    $vehicles | ForEach-Object { Write-Host "    - $($_.name) ($($_.plate))" -ForegroundColor Gray }
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n[3] TEST VEHICLES - Create" -ForegroundColor Yellow
$newVehicle = Invoke-Api -Method POST -Endpoint "/vehicles" -Auth -Body @{
    name = "Test Vehicle $(Get-Date -Format 'HHmmss')"
    type = "camion"
    brand = "Mercedes"
    model = "Actros"
    plate = "TEST-$(Get-Random -Maximum 999)"
    year = 2024
    color = "Blanc"
    mileage = 0
}
if ($newVehicle) {
    Write-Host "  OK: Created vehicle ID $($newVehicle.id)" -ForegroundColor Green
    $testVehicleId = $newVehicle.id
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n[4] TEST VEHICLES - Get by ID" -ForegroundColor Yellow
if ($testVehicleId) {
    $vehicle = Invoke-Api -Method GET -Endpoint "/vehicles/$testVehicleId" -Auth
    if ($vehicle) {
        Write-Host "  OK: Got vehicle $($vehicle.name)" -ForegroundColor Green
    } else {
        Write-Host "  FAIL" -ForegroundColor Red
    }
}

Write-Host "`n[5] TEST VEHICLES - List after create" -ForegroundColor Yellow
$vehiclesAfter = Invoke-Api -Method GET -Endpoint "/vehicles" -Auth
if ($vehiclesAfter -ne $null) {
    Write-Host "  OK: Got $($vehiclesAfter.Count) vehicles" -ForegroundColor Green
    $vehiclesAfter | ForEach-Object { Write-Host "    - $($_.name) ($($_.plate))" -ForegroundColor Gray }
}

# ==================== EMPLOYEES TESTS ====================
Write-Host "`n[6] TEST EMPLOYEES - List" -ForegroundColor Yellow
$employees = Invoke-Api -Method GET -Endpoint "/employees" -Auth
if ($employees -ne $null) {
    Write-Host "  OK: Got $($employees.Count) employees" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n[7] TEST EMPLOYEES - Create" -ForegroundColor Yellow
$newEmployee = Invoke-Api -Method POST -Endpoint "/employees" -Auth -Body @{
    name = "Test Driver $(Get-Date -Format 'HHmmss')"
    email = "driver$(Get-Random -Maximum 999)@test.com"
    phone = "+212600000000"
    role = "driver"
    status = "active"
}
if ($newEmployee) {
    Write-Host "  OK: Created employee ID $($newEmployee.id)" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

# ==================== MAINTENANCE TESTS ====================
Write-Host "`n[8] TEST MAINTENANCE - List" -ForegroundColor Yellow
$maintenance = Invoke-Api -Method GET -Endpoint "/maintenance" -Auth
if ($maintenance -ne $null) {
    Write-Host "  OK: Got $($maintenance.Count) maintenance records" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

# ==================== COSTS TESTS ====================
Write-Host "`n[9] TEST COSTS - List" -ForegroundColor Yellow
$costs = Invoke-Api -Method GET -Endpoint "/costs" -Auth
if ($costs -ne $null) {
    Write-Host "  OK: Got $($costs.Count) cost records" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n[10] TEST COSTS - Summary" -ForegroundColor Yellow
$costSummary = Invoke-Api -Method GET -Endpoint "/costs/summary" -Auth
if ($costSummary) {
    Write-Host "  OK: Total amount = $($costSummary.totalAmount)" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

# ==================== ALERTS TESTS ====================
Write-Host "`n[11] TEST ALERTS - List" -ForegroundColor Yellow
$alerts = Invoke-Api -Method GET -Endpoint "/alerts" -Auth
if ($alerts -ne $null) {
    Write-Host "  OK: Got $($alerts.Count) alerts" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n[12] TEST ALERTS - Unread count" -ForegroundColor Yellow
$unreadCount = Invoke-Api -Method GET -Endpoint "/alerts/unread-count" -Auth
if ($unreadCount -ne $null) {
    Write-Host "  OK: Unread count = $unreadCount" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

# ==================== GPS DEVICES TESTS ====================
Write-Host "`n[13] TEST GPS DEVICES - List" -ForegroundColor Yellow
$devices = Invoke-Api -Method GET -Endpoint "/gpsdevices" -Auth
if ($devices -ne $null) {
    Write-Host "  OK: Got $($devices.Count) GPS devices" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

# ==================== GEOFENCES TESTS ====================
Write-Host "`n[14] TEST GEOFENCES - List" -ForegroundColor Yellow
$geofences = Invoke-Api -Method GET -Endpoint "/geofences" -Auth
if ($geofences -ne $null) {
    Write-Host "  OK: Got $($geofences.Count) geofences" -ForegroundColor Green
} else {
    Write-Host "  FAIL" -ForegroundColor Red
}

Write-Host "`n=== Tests Complete ===" -ForegroundColor Cyan
