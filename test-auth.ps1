# Test Script for VBTrader Authentication
# This script bypasses the menu system and directly tests authentication

Write-Host "Testing VBTrader Authentication..." -ForegroundColor Green

# Set working directory
Set-Location "C:\Users\Bryon Chase\Documents\vbtrader\src\VBTrader.Console"

# Test authentication with provided credentials
$username = "bchase"
$password = "OICu812@*"

Write-Host "Testing with username: $username" -ForegroundColor Cyan

# Create input file for the application
@"
1
$username
$password
q
"@ | Out-File -FilePath "input.txt" -Encoding ASCII

# Run the application with input redirection
Write-Host "Running VBTrader with test credentials..." -ForegroundColor Yellow
$process = Start-Process -FilePath "dotnet" -ArgumentList "run" -RedirectStandardInput "input.txt" -RedirectStandardOutput "output.txt" -RedirectStandardError "error.txt" -Wait -PassThru

# Check results
Write-Host "Application finished with exit code: $($process.ExitCode)" -ForegroundColor White

if (Test-Path "output.txt") {
    Write-Host "`nStandard Output:" -ForegroundColor Green
    Get-Content "output.txt" | Select-Object -Last 50
}

if (Test-Path "error.txt") {
    Write-Host "`nErrors:" -ForegroundColor Red
    Get-Content "error.txt"
}

# Clean up
Remove-Item "input.txt" -ErrorAction SilentlyContinue
Remove-Item "output.txt" -ErrorAction SilentlyContinue
Remove-Item "error.txt" -ErrorAction SilentlyContinue

Write-Host "`nTest complete!" -ForegroundColor Green