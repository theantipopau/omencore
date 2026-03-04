<#
Prepare a non-installer 3.0.0-alpha package for private QA.
Usage: pwsh .\package-alpha.ps1
#>
param()

$version = (Get-Content VERSION.txt | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) { throw "VERSION.txt must contain a version" }
$alphaTag = "$version-alpha"

Write-Host "Packaging OmenCore $alphaTag (portable builds only)"

# Windows portable
Write-Host "Publishing Windows portable (win-x64)..."
dotnet publish "src/OmenCoreApp/OmenCoreApp.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -o "artifacts/win-x64"
Push-Location artifacts
if (Test-Path "OmenCore-${version}-win-x64.zip") { Remove-Item "OmenCore-${version}-win-x64.zip" -ErrorAction Ignore }
Compress-Archive -Path "win-x64/*" -DestinationPath "OmenCore-${version}-alpha-win-x64.zip"
Pop-Location

# Linux CLI
Write-Host "Publishing Linux CLI (linux-x64)..."
cd src/OmenCore.Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ../../publish/linux-x64
cd ../../publish/linux-x64
if (Test-Path "omencore-linux-${version}-alpha.tar.gz") { Remove-Item "omencore-linux-${version}-alpha.tar.gz" -ErrorAction Ignore }
Compress-Archive -Path . -DestinationPath "../../artifacts/omencore-linux-${version}-alpha.zip"

Write-Host "Alpha packages created in artifacts/ (portable ZIPs). Upload them to your release or share with private testers."