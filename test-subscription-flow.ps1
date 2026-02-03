# Test Script: Subscription & Permissions Flow
# This script tests the complete flow of subscription management

$BaseUrl = "http://localhost:5000/api"
$AdminToken = ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TEST: Subscription & Permissions Flow" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Login as System Admin
Write-Host "`n[STEP 1] Login as System Admin (admin@belive.ma)" -ForegroundColor Yellow
$loginBody = @{
    email = "admin@belive.ma"
    password = "Calypso@2026+"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    $AdminToken = $loginResponse.token
    Write-Host "  [OK] Login successful" -ForegroundColor Green
    Write-Host "  User: $($loginResponse.user.email)" -ForegroundColor Gray
    Write-Host "  IsSystemAdmin: $($loginResponse.user.isSystemAdmin)" -ForegroundColor Gray
    Write-Host "  CompanyId: $($loginResponse.user.companyId)" -ForegroundColor Gray
    
    # Check subscription features
    Write-Host "`n  Subscription Features:" -ForegroundColor Magenta
    $sf = $loginResponse.user.subscriptionFeatures
    if ($sf) {
        Write-Host "    ModuleDashboard: $($sf.moduleDashboard)" -ForegroundColor Gray
        Write-Host "    ModuleMonitoring: $($sf.moduleMonitoring)" -ForegroundColor Gray
        Write-Host "    ModuleVehicles: $($sf.moduleVehicles)" -ForegroundColor Gray
        Write-Host "    ModuleEmployees: $($sf.moduleEmployees)" -ForegroundColor Gray
        Write-Host "    ModuleMaintenance: $($sf.moduleMaintenance)" -ForegroundColor Gray
        Write-Host "    ModuleFleetManagement: $($sf.moduleFleetManagement)" -ForegroundColor Gray
    } else {
        Write-Host "    [WARNING] No subscription features returned!" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Login failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $AdminToken"
    "Content-Type" = "application/json"
}

# Step 2: Get existing subscription types
Write-Host "`n[STEP 2] Get Subscription Types" -ForegroundColor Yellow
try {
    $subscriptionTypes = Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types" -Method GET -Headers $headers
    Write-Host "  [OK] Found $($subscriptionTypes.Count) subscription type(s)" -ForegroundColor Green
    foreach ($st in $subscriptionTypes) {
        Write-Host "    - ID: $($st.id), Name: $($st.name), Price: $($st.yearlyPrice) DT/an" -ForegroundColor Gray
        Write-Host "      Modules: Dashboard=$($st.moduleDashboard), Monitoring=$($st.moduleMonitoring), Vehicles=$($st.moduleVehicles)" -ForegroundColor DarkGray
    }
} catch {
    Write-Host "  [ERROR] Failed to get subscription types: $($_.Exception.Message)" -ForegroundColor Red
}

# Step 3: Create a TEST subscription type with LIMITED modules
Write-Host "`n[STEP 3] Create Test Subscription (Limited Modules)" -ForegroundColor Yellow
$testSubscriptionName = "Test-Limited-$(Get-Date -Format 'HHmmss')"
$subscriptionBody = @{
    name = $testSubscriptionName
    code = $testSubscriptionName.ToLower()
    description = "Test subscription with limited modules"
    targetCompanyType = "all"
    monthlyPrice = 100
    quarterlyPrice = 250
    yearlyPrice = 900
    maxVehicles = 5
    maxUsers = 3
    maxGpsDevices = 5
    maxGeofences = 10
    historyRetentionDays = 30
    gpsTracking = $true
    gpsInstallation = $false
    apiAccess = $false
    advancedReports = $false
    realTimeAlerts = $true
    historyPlayback = $true
    fuelAnalysis = $false
    drivingBehavior = $false
    # LIMITED MODULES - Only these are enabled
    moduleDashboard = $true
    moduleMonitoring = $false  # DISABLED
    moduleVehicles = $true
    moduleEmployees = $true
    moduleGeofences = $false   # DISABLED
    moduleMaintenance = $true
    moduleCosts = $false       # DISABLED
    moduleReports = $true
    moduleSettings = $true
    moduleUsers = $true
    moduleSuppliers = $false   # DISABLED
    moduleDocuments = $false   # DISABLED
    moduleAccidents = $false   # DISABLED
    moduleFleetManagement = $false  # DISABLED
    # Reports
    reportTrips = $true
    reportFuel = $false
    reportSpeed = $true
    reportStops = $true
    reportMileage = $true
    reportCosts = $false
    reportMaintenance = $true
    reportDaily = $true
    reportMonthly = $false
    reportMileagePeriod = $false
    reportSpeedInfraction = $true
    reportDrivingBehavior = $false
} | ConvertTo-Json

$testSubscriptionId = 0
try {
    $createSubResponse = Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types" -Method POST -Headers $headers -Body $subscriptionBody
    $testSubscriptionId = $createSubResponse.id
    Write-Host "  [OK] Created subscription: $testSubscriptionName (ID: $testSubscriptionId)" -ForegroundColor Green
    Write-Host "    ModuleMonitoring: $($createSubResponse.moduleMonitoring) (should be False)" -ForegroundColor Gray
    Write-Host "    ModuleVehicles: $($createSubResponse.moduleVehicles) (should be True)" -ForegroundColor Gray
} catch {
    Write-Host "  [ERROR] Failed to create subscription: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
}

# Step 4: Create a TEST company with the limited subscription
Write-Host "`n[STEP 4] Create Test Company with Limited Subscription" -ForegroundColor Yellow
$testCompanyName = "TestCompany-$(Get-Date -Format 'HHmmss')"
$testAdminEmail = "admin-$((Get-Date -Format 'HHmmss'))@testcompany.com"
$testAdminPassword = "Test@2026!"

$companyBody = @{
    name = $testCompanyName
    email = "contact-$((Get-Date -Format 'HHmmss'))@testcompany.com"
    phone = "+21612345678"
    type = "transport"
    subscriptionId = $testSubscriptionId
    billingCycle = "yearly"
    adminName = "Test Admin"
    adminEmail = $testAdminEmail
    adminPassword = $testAdminPassword
} | ConvertTo-Json

$testCompanyId = 0
try {
    $createCompanyResponse = Invoke-RestMethod -Uri "$BaseUrl/admin/company" -Method POST -Headers $headers -Body $companyBody
    $testCompanyId = $createCompanyResponse.id
    Write-Host "  [OK] Created company: $testCompanyName (ID: $testCompanyId)" -ForegroundColor Green
    Write-Host "    Admin Email: $testAdminEmail" -ForegroundColor Gray
    Write-Host "    Admin Password: $testAdminPassword" -ForegroundColor Gray
    Write-Host "    SubscriptionId: $($createCompanyResponse.subscriptionId)" -ForegroundColor Gray
} catch {
    Write-Host "  [ERROR] Failed to create company: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
}

# Step 5: Login as the new company admin
Write-Host "`n[STEP 5] Login as Company Admin ($testAdminEmail)" -ForegroundColor Yellow
$companyAdminLoginBody = @{
    email = $testAdminEmail
    password = $testAdminPassword
} | ConvertTo-Json

try {
    $companyAdminLogin = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method POST -Body $companyAdminLoginBody -ContentType "application/json"
    Write-Host "  [OK] Login successful!" -ForegroundColor Green
    Write-Host "  User: $($companyAdminLogin.user.email)" -ForegroundColor Gray
    Write-Host "  CompanyId: $($companyAdminLogin.user.companyId)" -ForegroundColor Gray
    Write-Host "  IsCompanyAdmin: $($companyAdminLogin.user.isCompanyAdmin)" -ForegroundColor Gray
    Write-Host "  IsSystemAdmin: $($companyAdminLogin.user.isSystemAdmin)" -ForegroundColor Gray
    
    # Check subscription features
    Write-Host "`n  Subscription Features (from login response):" -ForegroundColor Magenta
    $sf = $companyAdminLogin.user.subscriptionFeatures
    if ($sf) {
        Write-Host "    ModuleDashboard: $($sf.moduleDashboard)" -ForegroundColor $(if($sf.moduleDashboard){"Green"}else{"Red"})
        Write-Host "    ModuleMonitoring: $($sf.moduleMonitoring)" -ForegroundColor $(if($sf.moduleMonitoring){"Green"}else{"Red"})
        Write-Host "    ModuleVehicles: $($sf.moduleVehicles)" -ForegroundColor $(if($sf.moduleVehicles){"Green"}else{"Red"})
        Write-Host "    ModuleEmployees: $($sf.moduleEmployees)" -ForegroundColor $(if($sf.moduleEmployees){"Green"}else{"Red"})
        Write-Host "    ModuleGeofences: $($sf.moduleGeofences)" -ForegroundColor $(if($sf.moduleGeofences){"Green"}else{"Red"})
        Write-Host "    ModuleMaintenance: $($sf.moduleMaintenance)" -ForegroundColor $(if($sf.moduleMaintenance){"Green"}else{"Red"})
        Write-Host "    ModuleCosts: $($sf.moduleCosts)" -ForegroundColor $(if($sf.moduleCosts){"Green"}else{"Red"})
        Write-Host "    ModuleReports: $($sf.moduleReports)" -ForegroundColor $(if($sf.moduleReports){"Green"}else{"Red"})
        Write-Host "    ModuleSettings: $($sf.moduleSettings)" -ForegroundColor $(if($sf.moduleSettings){"Green"}else{"Red"})
        Write-Host "    ModuleUsers: $($sf.moduleUsers)" -ForegroundColor $(if($sf.moduleUsers){"Green"}else{"Red"})
        Write-Host "    ModuleSuppliers: $($sf.moduleSuppliers)" -ForegroundColor $(if($sf.moduleSuppliers){"Green"}else{"Red"})
        Write-Host "    ModuleDocuments: $($sf.moduleDocuments)" -ForegroundColor $(if($sf.moduleDocuments){"Green"}else{"Red"})
        Write-Host "    ModuleAccidents: $($sf.moduleAccidents)" -ForegroundColor $(if($sf.moduleAccidents){"Green"}else{"Red"})
        Write-Host "    ModuleFleetManagement: $($sf.moduleFleetManagement)" -ForegroundColor $(if($sf.moduleFleetManagement){"Green"}else{"Red"})
        
        # Validation
        Write-Host "`n  Validation:" -ForegroundColor Cyan
        $errors = @()
        if ($sf.moduleMonitoring -eq $true) { $errors += "ModuleMonitoring should be FALSE" }
        if ($sf.moduleGeofences -eq $true) { $errors += "ModuleGeofences should be FALSE" }
        if ($sf.moduleCosts -eq $true) { $errors += "ModuleCosts should be FALSE" }
        if ($sf.moduleSuppliers -eq $true) { $errors += "ModuleSuppliers should be FALSE" }
        if ($sf.moduleDocuments -eq $true) { $errors += "ModuleDocuments should be FALSE" }
        if ($sf.moduleAccidents -eq $true) { $errors += "ModuleAccidents should be FALSE" }
        if ($sf.moduleFleetManagement -eq $true) { $errors += "ModuleFleetManagement should be FALSE" }
        if ($sf.moduleDashboard -ne $true) { $errors += "ModuleDashboard should be TRUE" }
        if ($sf.moduleVehicles -ne $true) { $errors += "ModuleVehicles should be TRUE" }
        
        if ($errors.Count -eq 0) {
            Write-Host "    [PASS] All module permissions are correct!" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Permission errors found:" -ForegroundColor Red
            foreach ($err in $errors) {
                Write-Host "      - $err" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "    [ERROR] No subscription features in login response!" -ForegroundColor Red
    }
} catch {
    Write-Host "  [ERROR] Login failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
}

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "TEST SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Subscription ID: $testSubscriptionId" -ForegroundColor Gray
Write-Host "Test Company ID: $testCompanyId" -ForegroundColor Gray
Write-Host "Test Admin Email: $testAdminEmail" -ForegroundColor Gray
Write-Host "Test Admin Password: $testAdminPassword" -ForegroundColor Gray
Write-Host "`nYou can now test logging in with:" -ForegroundColor Yellow
Write-Host "  Email: $testAdminEmail" -ForegroundColor White
Write-Host "  Password: $testAdminPassword" -ForegroundColor White
Write-Host "`nExpected behavior:" -ForegroundColor Yellow
Write-Host "  - Dashboard should be visible" -ForegroundColor Green
Write-Host "  - Vehicles should be visible" -ForegroundColor Green
Write-Host "  - Maintenance should be visible" -ForegroundColor Green
Write-Host "  - Monitoring should be HIDDEN" -ForegroundColor Red
Write-Host "  - Geofences should be HIDDEN" -ForegroundColor Red
Write-Host "  - Fleet Management should be HIDDEN" -ForegroundColor Red
