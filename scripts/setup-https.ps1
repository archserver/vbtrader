# VBTrader HTTPS Setup Script
# This script helps set up HTTPS for the Schwab OAuth callback

Write-Host "VBTrader HTTPS Setup" -ForegroundColor Green
Write-Host "===================" -ForegroundColor Green
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")

if (-not $isAdmin) {
    Write-Host "WARNING: Not running as administrator. Some operations may fail." -ForegroundColor Yellow
    Write-Host "For best results, run PowerShell as Administrator." -ForegroundColor Yellow
    Write-Host ""
}

# Step 1: Create development certificate
Write-Host "Step 1: Creating development SSL certificate..." -ForegroundColor Cyan
try {
    $output = dotnet dev-certs https --trust 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Certificate created/updated successfully" -ForegroundColor Green
    } else {
        Write-Host "Failed to create certificate: $output" -ForegroundColor Red
        Write-Host "You may need to run: dotnet dev-certs https --clean" -ForegroundColor Yellow
        Write-Host "Then run: dotnet dev-certs https --trust" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error running dotnet dev-certs: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Step 2: Check if port 3000 is available
Write-Host "Step 2: Checking port 3000 availability..." -ForegroundColor Cyan
try {
    $portCheck = Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue
    if ($portCheck) {
        Write-Host "Port 3000 is in use by another application" -ForegroundColor Yellow
        Write-Host "You may need to stop other services using this port" -ForegroundColor Yellow
    } else {
        Write-Host "Port 3000 is available" -ForegroundColor Green
    }
} catch {
    Write-Host "Port 3000 appears to be available" -ForegroundColor Green
}

Write-Host ""

# Step 3: Attempt to create certificate binding (requires admin)
Write-Host "Step 3: Setting up certificate binding for port 3000..." -ForegroundColor Cyan

if ($isAdmin) {
    try {
        # First, check if binding already exists
        $existingBinding = netsh http show sslcert ipport=127.0.0.1:3000 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Certificate binding already exists for 127.0.0.1:3000" -ForegroundColor Green
        } else {
            # Get the thumbprint of the development certificate
            $cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
                ($_.Subject -like "*localhost*" -or $_.Subject -like "*127.0.0.1*") -and $_.NotAfter -gt (Get-Date)
            } | Sort-Object NotAfter -Descending | Select-Object -First 1

            if ($cert) {
                $thumbprint = $cert.Thumbprint
                $appId = "{12345678-1234-5678-9abc-123456789012}"

                Write-Host "Found certificate with thumbprint: $thumbprint" -ForegroundColor Gray

                $bindingResult = netsh http add sslcert ipport=127.0.0.1:3000 certhash=$thumbprint appid=$appId 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "Certificate binding created successfully" -ForegroundColor Green
                } else {
                    Write-Host "Failed to create certificate binding: $bindingResult" -ForegroundColor Red
                    Write-Host "This may not prevent HTTPS from working in development" -ForegroundColor Yellow
                }
            } else {
                Write-Host "Could not find a suitable certificate" -ForegroundColor Red
            }
        }
    } catch {
        Write-Host "Error setting up certificate binding: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping certificate binding (requires administrator privileges)" -ForegroundColor Yellow
    Write-Host "The application may still work without explicit binding" -ForegroundColor Gray
}

Write-Host ""

# Step 4: Instructions for Schwab Developer Portal
Write-Host "Step 4: Schwab Developer Portal Configuration" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Please update your Schwab Developer Portal settings:" -ForegroundColor White
Write-Host "   1. Go to https://developer.schwab.com/" -ForegroundColor Gray
Write-Host "   2. Navigate to your application settings" -ForegroundColor Gray
Write-Host "   3. Update the Callback/Redirect URI to:" -ForegroundColor Gray
Write-Host "      https://127.0.0.1:3000" -ForegroundColor Yellow
Write-Host "   4. Save the changes" -ForegroundColor Gray
Write-Host ""

# Step 5: Test instructions
Write-Host "Step 5: Testing" -ForegroundColor Cyan
Write-Host "===============" -ForegroundColor Cyan
Write-Host ""
Write-Host "To test the HTTPS setup:" -ForegroundColor White
Write-Host "1. Run the VBTrader application" -ForegroundColor Gray
Write-Host "2. Login with your credentials" -ForegroundColor Gray
Write-Host "3. Press 'C' to configure Schwab credentials" -ForegroundColor Gray
Write-Host "4. Enter your App Key and App Secret" -ForegroundColor Gray
Write-Host "5. Use callback URL: https://127.0.0.1:3000" -ForegroundColor Gray
Write-Host ""

Write-Host "Setup complete!" -ForegroundColor Green