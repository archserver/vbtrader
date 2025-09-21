# Fix HTTPS Certificate Binding for VBTrader
# Run this as Administrator

Write-Host "Fixing HTTPS Certificate Binding for VBTrader" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator" -ForegroundColor Red
    Write-Host "Please right-click PowerShell and 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

Write-Host "Running as Administrator" -ForegroundColor Green
Write-Host ""

# Remove any existing binding for port 3000
Write-Host "Removing any existing SSL binding for port 3000..." -ForegroundColor Cyan
$result = netsh http delete sslcert ipport=127.0.0.1:3000 2>&1
Write-Host "Result: $result" -ForegroundColor Gray
Write-Host ""

# Get the development certificate
Write-Host "Finding development certificate..." -ForegroundColor Cyan
$cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {
    ($_.Subject -like "*localhost*" -or $_.Subject -like "*127.0.0.1*") -and $_.NotAfter -gt (Get-Date)
} | Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    # Try CurrentUser store
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
        ($_.Subject -like "*localhost*" -or $_.Subject -like "*127.0.0.1*") -and $_.NotAfter -gt (Get-Date)
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
}

if ($cert) {
    Write-Host "Found certificate:" -ForegroundColor Green
    Write-Host "  Subject: $($cert.Subject)" -ForegroundColor Gray
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Expires: $($cert.NotAfter)" -ForegroundColor Gray
    Write-Host ""

    # Create the binding
    Write-Host "Creating SSL certificate binding..." -ForegroundColor Cyan
    $appId = "{11111111-2222-3333-4444-555555555555}"
    $thumbprint = $cert.Thumbprint

    Write-Host "Using thumbprint: $thumbprint" -ForegroundColor Gray
    Write-Host "Using AppID: $appId" -ForegroundColor Gray

    $bindingResult = netsh http add sslcert ipport=127.0.0.1:3000 certhash=$thumbprint appid=$appId 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "SSL certificate binding created successfully!" -ForegroundColor Green
    } else {
        Write-Host "Failed to create SSL certificate binding:" -ForegroundColor Red
        Write-Host "$bindingResult" -ForegroundColor Red

        # Try alternative approach - copy cert to LocalMachine store
        Write-Host ""
        Write-Host "Trying alternative: copying certificate to LocalMachine store..." -ForegroundColor Yellow

        try {
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "LocalMachine")
            $store.Open("ReadWrite")
            $store.Add($cert)
            $store.Close()

            Write-Host "Certificate copied to LocalMachine store" -ForegroundColor Green

            # Try binding again
            $bindingResult2 = netsh http add sslcert ipport=127.0.0.1:3000 certhash=$thumbprint appid=$appId 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "SSL certificate binding created successfully!" -ForegroundColor Green
            } else {
                Write-Host "Still failed: $bindingResult2" -ForegroundColor Red
            }
        } catch {
            Write-Host "Failed to copy certificate: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
} else {
    Write-Host "No suitable certificate found" -ForegroundColor Red
    Write-Host "Creating new development certificate..." -ForegroundColor Yellow

    $createResult = dotnet dev-certs https --trust 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Development certificate created" -ForegroundColor Green
        Write-Host "Please run this script again to bind the certificate" -ForegroundColor Yellow
    } else {
        Write-Host "Failed to create certificate: $createResult" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "1. Test VBTrader HTTPS callback server" -ForegroundColor Gray
Write-Host "2. Run VBTrader and try Schwab authentication" -ForegroundColor Gray
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
Read-Host