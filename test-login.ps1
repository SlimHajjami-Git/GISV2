# Test Login API Script
Write-Host "=== Test API Login ===" -ForegroundColor Cyan

# Test 1: Health Check
Write-Host "`n[TEST 1] Health Check API..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method GET
    Write-Host "OK: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 2: Health via nginx proxy
Write-Host "`n[TEST 2] Health via nginx proxy (port 4200)..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "http://localhost:4200/api/health" -Method GET -ErrorAction Stop
    Write-Host "OK: API accessible via nginx" -ForegroundColor Green
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 3: Direct Login API
Write-Host "`n[TEST 3] Login direct (port 5000)..." -ForegroundColor Yellow
try {
    $body = '{"email":"test@example.com","password":"Test123!"}'
    $response = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method POST -ContentType "application/json" -Body $body
    Write-Host "OK: Token recu - $($response.token.Substring(0,50))..." -ForegroundColor Green
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Login via nginx proxy
Write-Host "`n[TEST 4] Login via nginx proxy (port 4200)..." -ForegroundColor Yellow
try {
    $body = '{"email":"test@example.com","password":"Test123!"}'
    $response = Invoke-RestMethod -Uri "http://localhost:4200/api/auth/login" -Method POST -ContentType "application/json" -Body $body
    Write-Host "OK: Token recu - $($response.token.Substring(0,50))..." -ForegroundColor Green
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 5: Check CORS headers
Write-Host "`n[TEST 5] Verification CORS headers..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/api/auth/login" -Method OPTIONS -Headers @{
        "Origin" = "http://localhost:4200"
        "Access-Control-Request-Method" = "POST"
        "Access-Control-Request-Headers" = "content-type"
    } -ErrorAction Stop
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "CORS Headers:" -ForegroundColor Cyan
    $response.Headers | Where-Object { $_.Key -like "Access-Control-*" } | ForEach-Object {
        Write-Host "  $($_.Key): $($_.Value)"
    }
} catch {
    Write-Host "CORS check: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "`n=== Tests termines ===" -ForegroundColor Cyan
