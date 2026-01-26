# Test SignalR Broadcasting via API endpoint
# This script calls the test/broadcast endpoint to simulate GPS updates

param(
    [int]$VehicleId = 1,          # Vehicle ID to test
    [double]$Speed = 0,            # Speed in km/h
    [switch]$IgnitionOn,           # Ignition state
    [switch]$IsMoving,             # Force moving state
    [int]$Count = 3,               # Number of broadcasts
    [int]$IntervalMs = 2000        # Interval between broadcasts
)

# Get auth token - you need to set this
$Token = $env:GIS_AUTH_TOKEN

if (-not $Token) {
    Write-Host "Please set GIS_AUTH_TOKEN environment variable with a valid JWT token" -ForegroundColor Red
    Write-Host "You can get this from browser localStorage.getItem('auth_token')" -ForegroundColor Yellow
    exit 1
}

$BaseUrl = "http://localhost:5000"

Write-Host "=== SignalR Broadcast Test ===" -ForegroundColor Cyan
Write-Host "Vehicle ID: $VehicleId"
Write-Host "Speed: $Speed km/h"
Write-Host "Ignition: $($IgnitionOn.IsPresent)"
Write-Host "IsMoving: $($IsMoving.IsPresent)"
Write-Host "Broadcasts: $Count"
Write-Host ""

for ($i = 1; $i -le $Count; $i++) {
    $body = @{
        vehicleId = $VehicleId
        speedKph = $Speed
        ignitionOn = $IgnitionOn.IsPresent
        isMoving = $IsMoving.IsPresent
    } | ConvertTo-Json

    Write-Host "[$i/$Count] Broadcasting: Speed=$Speed, Ignition=$($IgnitionOn.IsPresent), IsMoving=$($IsMoving.IsPresent)" -ForegroundColor Yellow
    
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/api/gps/test/broadcast" `
            -Method Post `
            -Headers @{ 
                Authorization = "Bearer $Token"
                "Content-Type" = "application/json"
            } `
            -Body $body

        Write-Host "  -> Success: $($response | ConvertTo-Json -Compress)" -ForegroundColor Green
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Write-Host "  -> Error ($statusCode): $_" -ForegroundColor Red
    }

    if ($i -lt $Count) {
        Start-Sleep -Milliseconds $IntervalMs
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host "Check browser console for 'Real-time position update' logs"
