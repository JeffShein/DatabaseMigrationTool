#!/usr/bin/env pwsh
#
# MSI Installer Build Script for Database Migration Tool
# Builds professional Windows installer using WiX Toolset
#

param(
    [string]$Configuration = "Release",
    [string]$PublishDir = "../bin/Release/net9.0-windows/win-x86/publish",
    [string]$SourceDir = "..",
    [string]$OutputDir = "../dist",
    [switch]$SkipPublish,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

function Write-Status($Message, $Color = "White") {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Test-WixInstallation {
    $wixPath = $null
    
    # Check common installation paths
    $commonPaths = @(
        "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\candle.exe",
        "${env:ProgramFiles}\WiX Toolset v3.11\bin\candle.exe",
        "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\v7.1A\Bin\candle.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            $wixPath = Split-Path $path
            break
        }
    }
    
    # Check PATH
    if (-not $wixPath) {
        $candlePath = Get-Command "candle.exe" -ErrorAction SilentlyContinue
        if ($candlePath) {
            $wixPath = Split-Path $candlePath.Source
        }
    }
    
    return $wixPath
}

function Install-WixToolset {
    Write-Status "WiX Toolset not found. Attempting to install..." "Yellow"
    
    # Check for chocolatey
    $chocoPath = Get-Command "choco.exe" -ErrorAction SilentlyContinue
    if ($chocoPath) {
        Write-Status "Installing WiX via Chocolatey..." "Cyan"
        try {
            & choco install wixtoolset -y
            return Test-WixInstallation
        }
        catch {
            Write-Status "Chocolatey installation failed: $($_.Exception.Message)" "Red"
        }
    }
    
    # Manual download instructions
    Write-Status "Please install WiX Toolset manually:" "Red"
    Write-Status "1. Download from: https://wixtoolset.org/releases/" "Red"
    Write-Status "2. Or use Chocolatey: choco install wixtoolset" "Red"
    Write-Status "3. Or use winget: winget install WiXToolset.WiX" "Red"
    
    return $null
}

# Main execution starts here
Write-Status "Database Migration Tool - MSI Installer Builder" "Magenta"
Write-Status "=================================================" "Magenta"

# Resolve paths
$PublishDir = Resolve-Path $PublishDir -ErrorAction SilentlyContinue
$SourceDir = Resolve-Path $SourceDir
$OutputDir = if (Test-Path $OutputDir) { Resolve-Path $OutputDir } else { New-Item -Path $OutputDir -ItemType Directory -Force }

Write-Status "Source Directory: $SourceDir" "Gray"
Write-Status "Publish Directory: $PublishDir" "Gray"
Write-Status "Output Directory: $OutputDir" "Gray"

# Check WiX installation
$wixBinPath = Test-WixInstallation
if (-not $wixBinPath) {
    $wixBinPath = Install-WixToolset
    if (-not $wixBinPath) {
        Write-Status "WiX Toolset installation required. Exiting." "Red"
        exit 1
    }
}

Write-Status "Using WiX Toolset at: $wixBinPath" "Green"

# Publish application if not skipped
if (-not $SkipPublish) {
    Write-Status "Publishing application for packaging..." "Cyan"
    
    $projectFile = Join-Path $SourceDir "src\DatabaseMigrationTool\DatabaseMigrationTool.csproj"
    if (-not (Test-Path $projectFile)) {
        Write-Status "Project file not found: $projectFile" "Red"
        exit 1
    }
    
    $publishCmd = "dotnet publish `"$projectFile`" -c $Configuration --self-contained true -r win-x86 -o `"$PublishDir`""
    if ($Verbose) {
        Write-Status "Executing: $publishCmd" "Gray"
    }
    
    Invoke-Expression $publishCmd
    if ($LASTEXITCODE -ne 0) {
        Write-Status "Application publish failed!" "Red"
        exit 1
    }
    
    Write-Status "Application published successfully" "Green"
}

# Verify publish directory exists
if (-not (Test-Path $PublishDir)) {
    Write-Status "Publish directory not found: $PublishDir" "Red"
    Write-Status "Run with -SkipPublish `$false or ensure directory exists" "Red"
    exit 1
}

# Create installer assets if they don't exist
$installerDir = Join-Path $SourceDir "installer"
$licenseFile = Join-Path $installerDir "License.rtf"
$bannerFile = Join-Path $installerDir "Banner.bmp"
$dialogFile = Join-Path $installerDir "Dialog.bmp"

if (-not (Test-Path $licenseFile)) {
    Write-Status "Creating default license file..." "Yellow"
    $licenseContent = @"
{\rtf1\ansi\deff0 {\fonttbl {\f0 Times New Roman;}}
\f0\fs20 Database Migration Tool License Agreement

Copyright (c) $(Get-Date -Format yyyy) Your Company Name. All rights reserved.

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the use of this software.

Permission is granted to anyone to use this software for any purpose, including commercial applications.
}
"@
    $licenseContent | Out-File -FilePath $licenseFile -Encoding ASCII
}

# Build MSI
Write-Status "Compiling WiX source files..." "Cyan"

$wxsFile = Join-Path $installerDir "DatabaseMigrationTool.wxs"
$wixObjFile = Join-Path $OutputDir "DatabaseMigrationTool.wixobj"
$msiFile = Join-Path $OutputDir "DatabaseMigrationTool-Setup.msi"

# Candle (compile)
$candleArgs = @(
    "`"$wxsFile`"",
    "-out", "`"$wixObjFile`"",
    "-dPublishDir=`"$PublishDir`"",
    "-dSourceDir=`"$SourceDir`""
)

if ($Verbose) {
    $candleArgs += "-v"
}

$candleCmd = "`"$wixBinPath\candle.exe`" " + ($candleArgs -join " ")
if ($Verbose) {
    Write-Status "Executing: $candleCmd" "Gray"
}

$candleProcess = Start-Process -FilePath "$wixBinPath\candle.exe" -ArgumentList $candleArgs -Wait -PassThru -NoNewWindow
if ($candleProcess.ExitCode -ne 0) {
    Write-Status "WiX compilation (candle) failed with exit code $($candleProcess.ExitCode)" "Red"
    exit 1
}

Write-Status "WiX compilation completed successfully" "Green"

# Light (link)
Write-Status "Linking MSI installer..." "Cyan"

$lightArgs = @(
    "`"$wixObjFile`"",
    "-out", "`"$msiFile`"",
    "-ext", "WixUIExtension"
)

if ($Verbose) {
    $lightArgs += "-v"
}

$lightCmd = "`"$wixBinPath\light.exe`" " + ($lightArgs -join " ")
if ($Verbose) {
    Write-Status "Executing: $lightCmd" "Gray"
}

$lightProcess = Start-Process -FilePath "$wixBinPath\light.exe" -ArgumentList $lightArgs -Wait -PassThru -NoNewWindow
if ($lightProcess.ExitCode -ne 0) {
    Write-Status "MSI linking (light) failed with exit code $($lightProcess.ExitCode)" "Red"
    exit 1
}

Write-Status "MSI installer created successfully!" "Green"

# File information
if (Test-Path $msiFile) {
    $msiInfo = Get-Item $msiFile
    $msiSizeMB = [math]::Round($msiInfo.Length / 1MB, 2)
    
    Write-Status "Installer Details:" "Magenta"
    Write-Status "  File: $($msiInfo.FullName)" "Gray"
    Write-Status "  Size: $msiSizeMB MB" "Gray"
    Write-Status "  Created: $($msiInfo.CreationTime)" "Gray"
    
    # Try to get version info
    try {
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($msiFile).ProductVersion
        if ($version) {
            Write-Status "  Version: $version" "Gray"
        }
    }
    catch {
        # Version info not available for MSI files
    }
    
    Write-Status "`nInstaller ready for distribution: $msiFile" "Green"
} else {
    Write-Status "MSI file was not created successfully" "Red"
    exit 1
}

# Cleanup
if (Test-Path $wixObjFile) {
    Remove-Item $wixObjFile -Force
    if ($Verbose) {
        Write-Status "Cleaned up temporary files" "Gray"
    }
}

Write-Status "MSI build completed successfully!" "Green"