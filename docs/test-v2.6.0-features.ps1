# OmenCore v2.6.0 Quick Test Script
# Run this in PowerShell to verify new features work correctly

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OmenCore v2.6.0 Feature Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: RAM Detection via WMI (same method used by fallback)
Write-Host "[TEST 1] RAM Detection (WMI Fallback Method)" -ForegroundColor Yellow
try {
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $totalRam = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    $freeRam = [math]::Round($os.FreePhysicalMemory / 1MB / 1024, 1)
    $usedRam = [math]::Round($totalRam - $freeRam, 1)
    
    if ($totalRam -gt 0) {
        Write-Host "  [OK] RAM Total: $totalRam GB" -ForegroundColor Green
        Write-Host "  [OK] RAM Used: $usedRam GB" -ForegroundColor Green
        Write-Host "  [OK] RAM Free: $freeRam GB" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] RAM returned 0 - fallback may be needed" -ForegroundColor Red
    }
} catch {
    Write-Host "  [FAIL] WMI RAM query failed: $_" -ForegroundColor Red
}
Write-Host ""

# Test 2: HP WMI BIOS Availability
Write-Host "[TEST 2] HP WMI BIOS Availability" -ForegroundColor Yellow
try {
    $null = Get-CimInstance -Namespace "root\wmi" -ClassName "hpqBIntM" -ErrorAction Stop
    Write-Host "  [OK] HP WMI BIOS (hpqBIntM) is available" -ForegroundColor Green
} catch {
    Write-Host "  [WARN] HP WMI BIOS not available - this is expected on non-HP systems" -ForegroundColor Yellow
}
Write-Host ""

# Test 3: System Model Detection
Write-Host "[TEST 3] System Model Detection" -ForegroundColor Yellow
try {
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem
    Write-Host "  [OK] Manufacturer: $($cs.Manufacturer)" -ForegroundColor Green
    Write-Host "  [OK] Model: $($cs.Model)" -ForegroundColor Green
    Write-Host "  [OK] System Family: $($cs.SystemFamily)" -ForegroundColor Green
    
    if ($cs.Model -match "OMEN") {
        Write-Host "  [OK] OMEN laptop detected!" -ForegroundColor Cyan
    }
} catch {
    Write-Host "  [FAIL] System detection failed: $_" -ForegroundColor Red
}
Write-Host ""

# Test 4: CPU Temperature (via OpenHardwareMonitor WMI if available)
Write-Host "[TEST 4] Temperature Sensors (WMI)" -ForegroundColor Yellow
try {
    $temps = Get-CimInstance -Namespace "root\OpenHardwareMonitor" -ClassName "Sensor" -ErrorAction Stop | 
             Where-Object { $_.SensorType -eq "Temperature" }
    if ($temps) {
        Write-Host "  [OK] OpenHardwareMonitor sensors available" -ForegroundColor Green
        $temps | Select-Object -First 3 | ForEach-Object {
            Write-Host "    - $($_.Name): $($_.Value) C" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "  [WARN] OpenHardwareMonitor WMI not available (OmenCore will use WMI BIOS fallback)" -ForegroundColor Yellow
}
Write-Host ""

# Test 5: Build Verification
Write-Host "[TEST 5] Build Verification" -ForegroundColor Yellow
$appPath = "F:\Omen\src\OmenCoreApp\bin\Release\net8.0-windows\win-x64\OmenCoreApp.exe"
$workerPath = "F:\Omen\src\OmenCore.HardwareWorker\bin\Release\net8.0\win-x64\OmenCore.HardwareWorker.exe"

if (Test-Path $appPath) {
    $appInfo = Get-Item $appPath
    $appSize = [math]::Round($appInfo.Length / 1MB, 2)
    Write-Host "  [OK] OmenCoreApp.exe exists ($appSize MB)" -ForegroundColor Green
    Write-Host "    Last modified: $($appInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "  [FAIL] OmenCoreApp.exe not found at expected path" -ForegroundColor Red
}

if (Test-Path $workerPath) {
    $workerInfo = Get-Item $workerPath
    $workerSize = [math]::Round($workerInfo.Length / 1MB, 2)
    Write-Host "  [OK] HardwareWorker.exe exists ($workerSize MB)" -ForegroundColor Green
    Write-Host "    Last modified: $($workerInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "  [FAIL] HardwareWorker.exe not found at expected path" -ForegroundColor Red
}
Write-Host ""

# Test 6: Check for conflicting processes
Write-Host "[TEST 6] Conflict Detection" -ForegroundColor Yellow
$conflicts = @("OmenMon", "MSIAfterburner", "RTSS", "HWiNFO64", "HWiNFO32")
$found = @()
foreach ($proc in $conflicts) {
    $running = Get-Process -Name $proc -ErrorAction SilentlyContinue
    if ($running) {
        $found += $proc
        Write-Host "  [WARN] $proc is running (may cause EC conflicts)" -ForegroundColor Yellow
    }
}
if ($found.Count -eq 0) {
    Write-Host "  [OK] No conflicting processes detected" -ForegroundColor Green
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Manual Tests Required:" -ForegroundColor Yellow
Write-Host "  1. Launch OmenCore and verify RAM shows correctly (not 0/0 GB)" -ForegroundColor White
Write-Host "  2. Try Constant fan mode - set slider to 50% and apply" -ForegroundColor White
Write-Host "  3. Enable Temperature-based RGB in settings (if available)" -ForegroundColor White
Write-Host "  4. Press Ctrl+Shift+Alt+A to apply fan settings" -ForegroundColor White
Write-Host "  5. Check startup time is faster (~1 second improvement)" -ForegroundColor White
Write-Host ""
Write-Host "If running alongside OmenMon:" -ForegroundColor Yellow
Write-Host "  - Watch for 'EC Conflict' messages in logs" -ForegroundColor White
Write-Host "  - Both apps should coexist without crashes" -ForegroundColor White
Write-Host ""
