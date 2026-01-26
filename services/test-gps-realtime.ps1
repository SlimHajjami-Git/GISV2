# Test GPS Real-time Updates via RabbitMQ
# This script sends GPS position messages to RabbitMQ to test SignalR broadcasting

param(
    [string]$DeviceUid = "860141071570544",  # Use an existing device IMEI
    [double]$Speed = 0,                       # Speed in km/h (0 = stopped)
    [switch]$IgnitionOn,                      # Ignition state (use -IgnitionOn to enable)
    [int]$Count = 5,                          # Number of messages to send
    [int]$IntervalMs = 2000                   # Interval between messages
)

$RabbitMQHost = "localhost"
$RabbitMQPort = 15672
$RabbitMQUser = "guest"
$RabbitMQPassword = "guest"
$Exchange = "gps.telemetry"
$RoutingKey = "gps.position"

# Base64 encode credentials
$credentials = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${RabbitMQUser}:${RabbitMQPassword}"))

Write-Host "=== GPS Real-time Test ===" -ForegroundColor Cyan
Write-Host "Device: $DeviceUid"
Write-Host "Speed: $Speed km/h"
Write-Host "Ignition: $IgnitionOn"
Write-Host "Messages: $Count"
Write-Host ""

# Test positions (simulate movement or stationary)
$baseLat = 36.8065
$baseLng = 10.1815

for ($i = 1; $i -le $Count; $i++) {
    # Slightly vary position if moving
    if ($Speed -gt 0) {
        $lat = $baseLat + ($i * 0.0001)
        $lng = $baseLng + ($i * 0.0001)
    } else {
        $lat = $baseLat
        $lng = $baseLng
    }

    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    
    $message = @{
        deviceUid = $DeviceUid
        latitude = $lat
        longitude = $lng
        speedKph = $Speed
        courseDeg = 90
        ignitionOn = $IgnitionOn
        alertType = "periodic"
        recordedAt = $timestamp
    } | ConvertTo-Json -Compress

    Write-Host "[$i/$Count] Sending: Speed=$Speed, Ignition=$IgnitionOn, Lat=$lat, Lng=$lng" -ForegroundColor Yellow
    
    # Publish to RabbitMQ via HTTP API
    $body = @{
        properties = @{}
        routing_key = $RoutingKey
        payload = $message
        payload_encoding = "string"
    } | ConvertTo-Json -Depth 3

    try {
        $response = Invoke-RestMethod -Uri "http://${RabbitMQHost}:${RabbitMQPort}/api/exchanges/%2f/${Exchange}/publish" `
            -Method Post `
            -Headers @{ Authorization = "Basic $credentials" } `
            -ContentType "application/json" `
            -Body $body

        if ($response.routed) {
            Write-Host "  -> Message routed successfully" -ForegroundColor Green
        } else {
            Write-Host "  -> Message NOT routed (no consumers?)" -ForegroundColor Red
        }
    } catch {
        Write-Host "  -> Error: $_" -ForegroundColor Red
    }

    if ($i -lt $Count) {
        Start-Sleep -Milliseconds $IntervalMs
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host "Check browser console for 'Real-time position update' logs"
