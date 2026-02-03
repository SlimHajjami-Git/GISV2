# Complete E2E Test Script: Subscription, Company, Roles & Permissions
# This script tests the ENTIRE flow from system admin to limited user

$BaseUrl = "http://localhost:5000/api"
$TestId = Get-Date -Format 'HHmmss'
$AllTestsPassed = $true

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  COMPLETE E2E TEST - Subscriptions & RBAC  " -ForegroundColor Cyan
Write-Host "  Test ID: $TestId" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# ============================================================
# TEST 1: Login as System Admin
# ============================================================
Write-Host "`n[TEST 1] Login as System Admin" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$systemAdminToken = ""
try {
    $loginBody = @{ email = "admin@belive.ma"; password = "Calypso@2026+" } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    $systemAdminToken = $loginResponse.token
    
    if ($loginResponse.user.isSystemAdmin -eq $true) {
        Write-Host "  [PASS] System Admin login successful" -ForegroundColor Green
        Write-Host "    Email: $($loginResponse.user.email)" -ForegroundColor Gray
        Write-Host "    IsSystemAdmin: $($loginResponse.user.isSystemAdmin)" -ForegroundColor Gray
    } else {
        Write-Host "  [FAIL] User is not System Admin!" -ForegroundColor Red
        $AllTestsPassed = $false
    }
} catch {
    Write-Host "  [FAIL] Login failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
    exit 1
}

$systemHeaders = @{ "Authorization" = "Bearer $systemAdminToken"; "Content-Type" = "application/json" }

# ============================================================
# TEST 2: Create Limited Subscription Type
# ============================================================
Write-Host "`n[TEST 2] Create Limited Subscription Type" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$subscriptionName = "Test-Limite-$TestId"
$subscriptionBody = @{
    name = $subscriptionName
    code = "test-limite-$TestId"
    description = "Abonnement de test avec modules limites"
    targetCompanyType = "all"
    monthlyPrice = 50
    quarterlyPrice = 130
    yearlyPrice = 450
    maxVehicles = 10
    maxUsers = 5
    maxGpsDevices = 10
    maxGeofences = 5
    historyRetentionDays = 30
    gpsTracking = $true
    realTimeAlerts = $true
    historyPlayback = $true
    # MODULES - Only some enabled
    moduleDashboard = $true
    moduleMonitoring = $false      # DISABLED
    moduleVehicles = $true
    moduleEmployees = $true
    moduleGeofences = $false       # DISABLED
    moduleMaintenance = $true
    moduleCosts = $false           # DISABLED
    moduleReports = $true
    moduleSettings = $true
    moduleUsers = $true
    moduleSuppliers = $false       # DISABLED
    moduleDocuments = $false       # DISABLED
    moduleAccidents = $false       # DISABLED
    moduleFleetManagement = $false # DISABLED
    # REPORTS
    reportTrips = $true
    reportSpeed = $true
    reportStops = $true
    reportMileage = $true
    reportMaintenance = $true
    reportDaily = $true
} | ConvertTo-Json

$subscriptionId = 0
try {
    $subResponse = Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types" -Method POST -Headers $systemHeaders -Body $subscriptionBody
    $subscriptionId = $subResponse.id
    
    $modulesOk = ($subResponse.moduleMonitoring -eq $false) -and ($subResponse.moduleDashboard -eq $true) -and ($subResponse.moduleVehicles -eq $true)
    if ($modulesOk) {
        Write-Host "  [PASS] Subscription created: $subscriptionName (ID: $subscriptionId)" -ForegroundColor Green
        Write-Host "    ModuleDashboard: $($subResponse.moduleDashboard)" -ForegroundColor Gray
        Write-Host "    ModuleMonitoring: $($subResponse.moduleMonitoring) (should be False)" -ForegroundColor Gray
        Write-Host "    ModuleVehicles: $($subResponse.moduleVehicles)" -ForegroundColor Gray
    } else {
        Write-Host "  [FAIL] Module values incorrect!" -ForegroundColor Red
        $AllTestsPassed = $false
    }
} catch {
    Write-Host "  [FAIL] Create subscription failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 3: Create Company with this Subscription
# ============================================================
Write-Host "`n[TEST 3] Create Company with Limited Subscription" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$companyName = "SocieteTest-$TestId"
$companyAdminEmail = "admin-$TestId@societetest.com"
$companyAdminPassword = "Admin@2026!"

$companyBody = @{
    name = $companyName
    email = "contact-$TestId@societetest.com"
    phone = "+21698765432"
    type = "transport"
    subscriptionId = $subscriptionId
    billingCycle = "yearly"
    adminName = "Admin Test"
    adminEmail = $companyAdminEmail
    adminPassword = $companyAdminPassword
} | ConvertTo-Json

$companyId = 0
try {
    $companyResponse = Invoke-RestMethod -Uri "$BaseUrl/admin/company" -Method POST -Headers $systemHeaders -Body $companyBody
    $companyId = $companyResponse.id
    Write-Host "  [PASS] Company created: $companyName (ID: $companyId)" -ForegroundColor Green
    Write-Host "    Admin Email: $companyAdminEmail" -ForegroundColor Gray
    Write-Host "    SubscriptionId: $($companyResponse.subscriptionId)" -ForegroundColor Gray
} catch {
    Write-Host "  [FAIL] Create company failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 4: Login as Company Admin
# ============================================================
Write-Host "`n[TEST 4] Login as Company Admin" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$companyAdminToken = ""
$companyAdminUser = $null
try {
    $loginBody = @{ email = $companyAdminEmail; password = $companyAdminPassword } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    $companyAdminToken = $loginResponse.token
    $companyAdminUser = $loginResponse.user
    
    if ($loginResponse.user.isCompanyAdmin -eq $true -and $loginResponse.user.companyId -eq $companyId) {
        Write-Host "  [PASS] Company Admin login successful" -ForegroundColor Green
        Write-Host "    Email: $($loginResponse.user.email)" -ForegroundColor Gray
        Write-Host "    CompanyId: $($loginResponse.user.companyId)" -ForegroundColor Gray
        Write-Host "    IsCompanyAdmin: $($loginResponse.user.isCompanyAdmin)" -ForegroundColor Gray
    } else {
        Write-Host "  [FAIL] Not company admin or wrong company!" -ForegroundColor Red
        $AllTestsPassed = $false
    }
    
    # Verify subscription features
    $sf = $loginResponse.user.subscriptionFeatures
    if ($sf) {
        Write-Host "`n  Subscription Features Check:" -ForegroundColor Magenta
        $errors = @()
        if ($sf.moduleMonitoring -eq $true) { $errors += "ModuleMonitoring should be FALSE" }
        if ($sf.moduleGeofences -eq $true) { $errors += "ModuleGeofences should be FALSE" }
        if ($sf.moduleCosts -eq $true) { $errors += "ModuleCosts should be FALSE" }
        if ($sf.moduleDashboard -ne $true) { $errors += "ModuleDashboard should be TRUE" }
        if ($sf.moduleVehicles -ne $true) { $errors += "ModuleVehicles should be TRUE" }
        
        if ($errors.Count -eq 0) {
            Write-Host "    [PASS] All subscription features correct!" -ForegroundColor Green
        } else {
            foreach ($err in $errors) { Write-Host "    [FAIL] $err" -ForegroundColor Red }
            $AllTestsPassed = $false
        }
    }
} catch {
    Write-Host "  [FAIL] Login failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

$companyHeaders = @{ "Authorization" = "Bearer $companyAdminToken"; "Content-Type" = "application/json" }

# ============================================================
# TEST 5: Company Admin creates a LIMITED ROLE
# ============================================================
Write-Host "`n[TEST 5] Company Admin Creates Limited Role" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$limitedRoleName = "Conducteur-Limite-$TestId"
$roleBody = @{
    name = $limitedRoleName
    description = "Role conducteur avec acces limite"
    permissions = @{
        dashboard = $true
        vehicles = $true      # Can see vehicles
        maintenance = $false  # Cannot see maintenance
        reports = $false      # Cannot see reports
        employees = $false    # Cannot see employees
        settings = $false     # Cannot see settings
        users = $false        # Cannot manage users
    }
} | ConvertTo-Json

$limitedRoleId = 0
try {
    $roleResponse = Invoke-RestMethod -Uri "$BaseUrl/roles" -Method POST -Headers $companyHeaders -Body $roleBody
    $limitedRoleId = $roleResponse.id
    Write-Host "  [PASS] Limited role created: $limitedRoleName (ID: $limitedRoleId)" -ForegroundColor Green
    Write-Host "    Permissions: dashboard=true, vehicles=true, maintenance=false" -ForegroundColor Gray
} catch {
    Write-Host "  [FAIL] Create role failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 6: Company Admin creates a USER with Limited Role
# ============================================================
Write-Host "`n[TEST 6] Company Admin Creates User with Limited Role" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

$limitedUserEmail = "conducteur-$TestId@societetest.com"
$limitedUserPassword = "Conducteur@2026!"

$userBody = @{
    firstName = "Jean"
    lastName = "Conducteur"
    email = $limitedUserEmail
    password = $limitedUserPassword
    phone = "+21655555555"
    roleId = $limitedRoleId
} | ConvertTo-Json

$limitedUserId = 0
try {
    $userResponse = Invoke-RestMethod -Uri "$BaseUrl/users" -Method POST -Headers $companyHeaders -Body $userBody
    $limitedUserId = $userResponse.id
    Write-Host "  [PASS] Limited user created: $limitedUserEmail (ID: $limitedUserId)" -ForegroundColor Green
    Write-Host "    RoleId: $limitedRoleId" -ForegroundColor Gray
} catch {
    Write-Host "  [FAIL] Create user failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 7: Login as Limited User and verify permissions
# ============================================================
Write-Host "`n[TEST 7] Login as Limited User & Verify Permissions" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

try {
    $loginBody = @{ email = $limitedUserEmail; password = $limitedUserPassword } | ConvertTo-Json
    $loginResponse = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method POST -Body $loginBody -ContentType "application/json"
    
    Write-Host "  [PASS] Limited user login successful" -ForegroundColor Green
    Write-Host "    Email: $($loginResponse.user.email)" -ForegroundColor Gray
    Write-Host "    RoleId: $($loginResponse.user.roleId)" -ForegroundColor Gray
    Write-Host "    RoleName: $($loginResponse.user.roleName)" -ForegroundColor Gray
    Write-Host "    IsCompanyAdmin: $($loginResponse.user.isCompanyAdmin)" -ForegroundColor Gray
    
    # Check role permissions
    $perms = $loginResponse.user.permissions
    if ($perms) {
        Write-Host "`n  Role Permissions Check:" -ForegroundColor Magenta
        Write-Host "    dashboard: $($perms.dashboard)" -ForegroundColor $(if($perms.dashboard){"Green"}else{"Red"})
        Write-Host "    vehicles: $($perms.vehicles)" -ForegroundColor $(if($perms.vehicles){"Green"}else{"Red"})
        Write-Host "    maintenance: $($perms.maintenance)" -ForegroundColor $(if($perms.maintenance){"Red"}else{"Green"})
        Write-Host "    reports: $($perms.reports)" -ForegroundColor $(if($perms.reports){"Red"}else{"Green"})
        
        # Validate
        if ($perms.dashboard -eq $true -and $perms.vehicles -eq $true -and $perms.maintenance -ne $true) {
            Write-Host "    [PASS] Role permissions are correct!" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Role permissions mismatch!" -ForegroundColor Red
            $AllTestsPassed = $false
        }
    }
    
    # Check subscription features (should still apply)
    $sf = $loginResponse.user.subscriptionFeatures
    if ($sf) {
        Write-Host "`n  Subscription Features (still applies to limited user):" -ForegroundColor Magenta
        Write-Host "    ModuleMonitoring: $($sf.moduleMonitoring) (should be False from subscription)" -ForegroundColor $(if($sf.moduleMonitoring){"Red"}else{"Green"})
        
        if ($sf.moduleMonitoring -eq $false) {
            Write-Host "    [PASS] Subscription limits still apply!" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Subscription limits not applied!" -ForegroundColor Red
            $AllTestsPassed = $false
        }
    }
} catch {
    Write-Host "  [FAIL] Login failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 8: Verify subscription edit returns correct data
# ============================================================
Write-Host "`n[TEST 8] Verify Subscription Edit Data (GET)" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

try {
    $subTypes = Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types" -Method GET -Headers $systemHeaders
    $ourSub = $subTypes | Where-Object { $_.id -eq $subscriptionId }
    
    if ($ourSub) {
        Write-Host "  [PASS] Subscription data retrieved for editing" -ForegroundColor Green
        Write-Host "    Name: $($ourSub.name)" -ForegroundColor Gray
        Write-Host "    ModuleDashboard: $($ourSub.moduleDashboard)" -ForegroundColor Gray
        Write-Host "    ModuleMonitoring: $($ourSub.moduleMonitoring)" -ForegroundColor Gray
        Write-Host "    ModuleVehicles: $($ourSub.moduleVehicles)" -ForegroundColor Gray
        Write-Host "    ModuleMaintenance: $($ourSub.moduleMaintenance)" -ForegroundColor Gray
        
        # Verify all module fields are present
        $hasAllFields = ($null -ne $ourSub.moduleDashboard) -and ($null -ne $ourSub.moduleMonitoring) -and 
                        ($null -ne $ourSub.moduleVehicles) -and ($null -ne $ourSub.moduleMaintenance) -and
                        ($null -ne $ourSub.moduleReports) -and ($null -ne $ourSub.moduleUsers)
        
        if ($hasAllFields) {
            Write-Host "    [PASS] All module fields present for edit form!" -ForegroundColor Green
        } else {
            Write-Host "    [FAIL] Some module fields missing!" -ForegroundColor Red
            $AllTestsPassed = $false
        }
    } else {
        Write-Host "  [FAIL] Subscription not found!" -ForegroundColor Red
        $AllTestsPassed = $false
    }
} catch {
    Write-Host "  [FAIL] Get subscription failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# TEST 9: Update subscription and verify changes
# ============================================================
Write-Host "`n[TEST 9] Update Subscription & Verify Changes" -ForegroundColor Yellow
Write-Host "=" * 50 -ForegroundColor Gray

try {
    # Enable moduleMonitoring which was disabled
    $updateBody = @{
        moduleMonitoring = $true
        moduleCosts = $true
    } | ConvertTo-Json
    
    $updateResponse = Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types/$subscriptionId" -Method PUT -Headers $systemHeaders -Body $updateBody
    
    if ($updateResponse.moduleMonitoring -eq $true -and $updateResponse.moduleCosts -eq $true) {
        Write-Host "  [PASS] Subscription updated successfully" -ForegroundColor Green
        Write-Host "    ModuleMonitoring: $($updateResponse.moduleMonitoring) (changed to True)" -ForegroundColor Gray
        Write-Host "    ModuleCosts: $($updateResponse.moduleCosts) (changed to True)" -ForegroundColor Gray
    } else {
        Write-Host "  [FAIL] Update didn't apply correctly!" -ForegroundColor Red
        $AllTestsPassed = $false
    }
    
    # Revert changes
    $revertBody = @{ moduleMonitoring = $false; moduleCosts = $false } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/admin/subscription-types/$subscriptionId" -Method PUT -Headers $systemHeaders -Body $revertBody | Out-Null
    Write-Host "    (Changes reverted for further testing)" -ForegroundColor DarkGray
} catch {
    Write-Host "  [FAIL] Update subscription failed: $($_.Exception.Message)" -ForegroundColor Red
    $AllTestsPassed = $false
}

# ============================================================
# FINAL SUMMARY
# ============================================================
Write-Host "`n=============================================" -ForegroundColor Cyan
Write-Host "  TEST SUMMARY" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

if ($AllTestsPassed) {
    Write-Host "`n  [ALL TESTS PASSED]" -ForegroundColor Green
} else {
    Write-Host "`n  [SOME TESTS FAILED]" -ForegroundColor Red
}

Write-Host "`n  Test Accounts Created:" -ForegroundColor Yellow
Write-Host "  ----------------------" -ForegroundColor Gray
Write-Host "  1. Company Admin:" -ForegroundColor White
Write-Host "     Email: $companyAdminEmail" -ForegroundColor Gray
Write-Host "     Password: $companyAdminPassword" -ForegroundColor Gray
Write-Host "     Access: Full admin (limited by subscription)" -ForegroundColor Gray

Write-Host "`n  2. Limited User (Conducteur):" -ForegroundColor White
Write-Host "     Email: $limitedUserEmail" -ForegroundColor Gray
Write-Host "     Password: $limitedUserPassword" -ForegroundColor Gray
Write-Host "     Access: Dashboard + Vehicles only" -ForegroundColor Gray

Write-Host "`n  Company: $companyName (ID: $companyId)" -ForegroundColor Gray
Write-Host "  Subscription: $subscriptionName (ID: $subscriptionId)" -ForegroundColor Gray

Write-Host "`n  FRONTEND TESTS TO DO MANUALLY:" -ForegroundColor Yellow
Write-Host "  ================================" -ForegroundColor Gray
Write-Host "  1. Login with Company Admin ($companyAdminEmail)" -ForegroundColor White
Write-Host "     - Verify Monitoring is HIDDEN (subscription limit)" -ForegroundColor Gray
Write-Host "     - Verify Dashboard, Vehicles, Maintenance visible" -ForegroundColor Gray

Write-Host "`n  2. Login with Limited User ($limitedUserEmail)" -ForegroundColor White
Write-Host "     - Verify only Dashboard + Vehicles visible" -ForegroundColor Gray
Write-Host "     - Verify Maintenance is HIDDEN (role limit)" -ForegroundColor Gray
Write-Host "     - Verify Reports is HIDDEN (role limit)" -ForegroundColor Gray

Write-Host "`n  3. Admin Panel - Edit Subscription" -ForegroundColor White
Write-Host "     - Go to /admin/subscriptions" -ForegroundColor Gray
Write-Host "     - Click Edit on '$subscriptionName'" -ForegroundColor Gray
Write-Host "     - Verify all checkboxes show current values" -ForegroundColor Gray
Write-Host "     - Verify ModuleMonitoring is UNCHECKED" -ForegroundColor Gray
