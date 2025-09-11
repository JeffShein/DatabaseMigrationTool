param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "./dist",
    [switch]$Clean,
    [switch]$Trimmed
)

# Clean previous builds if requested
if ($Clean) {
    Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
    dotnet clean --configuration $Configuration
    if (Test-Path $OutputPath) {
        Remove-Item -Path $OutputPath -Recurse -Force
    }
}

# Create output directory
New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null

Write-Host "Database Migration Tool - Single File Builder" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta

$projectFile = "src/DatabaseMigrationTool/DatabaseMigrationTool.csproj"
$version = "1.0.0"
$publishPath = "$OutputPath/single-file"

# Build single file executable
Write-Host "Building single-file executable..." -ForegroundColor Green

# Publish arguments
$publishArgs = @(
    $projectFile
    "-c", $Configuration
    "--self-contained", "true" 
    "-r", "win-x86"
    "-o", $publishPath
    "-p:PublishSingleFile=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "-p:EnableCompressionInSingleFile=true"
    "-p:PublishReadyToRun=true"
)

# Add trimming if requested (reduces size but may break reflection-based code)
if ($Trimmed) {
    $publishArgs += "-p:PublishTrimmed=true"
    Write-Host "Note: Trimming enabled - this may break some functionality" -ForegroundColor Yellow
}

Write-Host "Publishing single-file executable..." -ForegroundColor Cyan
Write-Host "Arguments: dotnet publish $($publishArgs -join ' ')" -ForegroundColor Gray

& dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Copy Firebird DLLs for single-file deployment
Write-Host "Copying Firebird native libraries..." -ForegroundColor Cyan
$firebirdSourcePath = "src/DatabaseMigrationTool/firebird"

if (Test-Path $firebirdSourcePath) {
    # Copy only essential DLL files to root directory (sufficient for runtime)
    $firebirdDlls = Get-ChildItem -Path $firebirdSourcePath -Filter "*.dll"
    foreach ($dll in $firebirdDlls) {
        Copy-Item -Path $dll.FullName -Destination $publishPath -Force
        Write-Host "  - Copied $($dll.Name)" -ForegroundColor Gray
    }
    
    Write-Host "[OK] Firebird native libraries deployed to root directory" -ForegroundColor Green
    Write-Host "     Note: Firebird subdirectory not needed for single-file deployment" -ForegroundColor Gray
} else {
    Write-Host "[!] Firebird source directory not found: $firebirdSourcePath" -ForegroundColor Yellow
}

# Check what files were created
$exeFiles = Get-ChildItem -Path $publishPath -Filter "*.exe"
$dllFiles = Get-ChildItem -Path $publishPath -Filter "*.dll"
$otherFiles = Get-ChildItem -Path $publishPath | Where-Object { $_.Extension -notin @('.exe', '.dll', '.pdb') }

Write-Host ""
Write-Host "Build Results:" -ForegroundColor Magenta
Write-Host "==============" -ForegroundColor Magenta

foreach ($exe in $exeFiles) {
    $exeSizeMB = [math]::Round($exe.Length / 1MB, 2)
    Write-Host "[OK] Main Executable: $($exe.Name) ($exeSizeMB MB)" -ForegroundColor Green
}

if ($dllFiles.Count -gt 0) {
    Write-Host "[*] Native library files (required for Firebird support):" -ForegroundColor Cyan
    foreach ($dll in $dllFiles) {
        $dllSizeMB = [math]::Round($dll.Length / 1MB, 2)
        Write-Host "  - $($dll.Name) ($dllSizeMB MB)" -ForegroundColor Gray
    }
}

if ($otherFiles.Count -gt 0) {
    Write-Host "[*] Support files:" -ForegroundColor Cyan
    foreach ($file in $otherFiles) {
        if ($file.PSIsContainer) {
            $itemCount = (Get-ChildItem -Path $file.FullName -Recurse).Count
            Write-Host "  - $($file.Name)/ ($itemCount items)" -ForegroundColor Gray
        } else {
            $fileSizeKB = [math]::Round($file.Length / 1KB, 1)
            Write-Host "  - $($file.Name) ($fileSizeKB KB)" -ForegroundColor Gray
        }
    }
}

# Calculate total size
$totalSize = (Get-ChildItem -Path $publishPath -Recurse -File | Measure-Object -Property Length -Sum).Sum
$totalSizeMB = [math]::Round($totalSize / 1MB, 2)

Write-Host ""
Write-Host "Summary:" -ForegroundColor Magenta
Write-Host "--------" -ForegroundColor Magenta
Write-Host "Total files: $((Get-ChildItem -Path $publishPath -Recurse -File).Count)" -ForegroundColor White
Write-Host "Total size: $totalSizeMB MB" -ForegroundColor White
Write-Host "Output directory: $publishPath" -ForegroundColor White

# Create deployment package
$zipPath = "$OutputPath/DatabaseMigrationTool-SingleFile-v$version.zip"
Write-Host ""
Write-Host "Creating deployment package..." -ForegroundColor Cyan

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $zipPath)
    $zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "[OK] Package created: $zipPath ($zipSizeMB MB)" -ForegroundColor Green
}
catch {
    Write-Host "Using PowerShell compression..." -ForegroundColor Yellow
    Compress-Archive -Path "$publishPath\*" -DestinationPath $zipPath -Force
    $zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "[OK] Package created: $zipPath ($zipSizeMB MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Single-file build completed successfully!" -ForegroundColor Green
if ($exeFiles.Count -gt 0) {
    Write-Host "Main executable: $($exeFiles[0].FullName)" -ForegroundColor Green
}