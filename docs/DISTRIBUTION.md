# Database Migration Tool - Distribution Guide

This document provides comprehensive instructions for packaging and distributing the Database Migration Tool on Windows platforms.

## Overview

The Database Migration Tool supports two professional distribution methods:

1. **ZIP Archive**: Portable, self-contained distribution
2. **MSI Installer**: Professional Windows installer with Start Menu integration, file associations, and uninstall support

Both methods create self-contained deployments with all dependencies included.

## Prerequisites

### Development Machine Requirements

- Windows 10/11 or Windows Server 2019/2022
- .NET 9.0 SDK (specified in `global.json`)
- PowerShell 5.1 or PowerShell 7+
- Git (for version control)

### MSI Installer Additional Requirements

- **WiX Toolset v3.11+**: Download from [wixtoolset.org](https://wixtoolset.org/releases/)
- **Alternative Installation Methods**:
  ```powershell
  # Via Chocolatey
  choco install wixtoolset
  
  # Via Windows Package Manager
  winget install WiXToolset.WiX
  ```

## Quick Start

### Option 1: Automated Build (Both Distributions)

Use the comprehensive build script that creates both ZIP and MSI distributions:

```powershell
# Build both distributions
.\build-distribution.ps1

# Build only ZIP
.\build-distribution.ps1 -ZipOnly

# Build only MSI  
.\build-distribution.ps1 -MsiOnly

# Clean build with custom output
.\build-distribution.ps1 -Clean -OutputPath "./release"
```

### Option 2: Manual ZIP Distribution

```powershell
# 1. Publish self-contained executable
dotnet publish src/DatabaseMigrationTool/DatabaseMigrationTool.csproj -c Release --self-contained true -r win-x86 -o ./dist/publish

# 2. Create ZIP archive
Compress-Archive -Path "./dist/publish/*" -DestinationPath "./dist/DatabaseMigrationTool-Windows-x86.zip"
```

### Option 3: Manual MSI Installer

```powershell
# 1. Navigate to installer directory
cd installer

# 2. Run MSI build script
.\build-msi.ps1

# 3. Optional: Skip republishing if already done
.\build-msi.ps1 -SkipPublish
```

## Distribution Details

### ZIP Archive Distribution

**File**: `DatabaseMigrationTool-v{version}-Windows-x86.zip`

**Contents**:
- `DatabaseMigrationTool.exe` - Main application
- `*.dll` - All runtime libraries and dependencies
- `firebird/` - Firebird database engine files
- `README.txt` - Installation and usage instructions
- `USER_GUIDE.md` - Comprehensive user documentation

**Advantages**:
- ✅ No installation required
- ✅ Portable - can run from any directory
- ✅ No registry modifications
- ✅ Easy to distribute via file sharing
- ✅ Quick deployment for testing

**Target Use Cases**:
- Portable deployments
- Testing and evaluation
- Network share deployments
- USB stick distributions
- Environments where MSI installation is restricted

### MSI Installer Distribution

**File**: `DatabaseMigrationTool-Setup.msi`

**Features**:
- Professional Windows Installer experience
- Start Menu shortcuts (GUI and Console modes)
- Optional desktop shortcut
- File association for `.dbmconfig` files
- Add/Remove Programs integration
- Automatic uninstallation
- Version checking and upgrade support
- Registry entries for proper integration

**Installation Components**:

1. **Main Application** (Required)
   - Core executable and libraries
   - Documentation files
   - Registry entries

2. **Firebird Support** (Required)
   - Firebird database engine
   - Configuration files
   - Unicode support libraries

3. **Shortcuts** (Optional)
   - Start Menu entries
   - Desktop shortcut (user selectable)

**Advantages**:
- ✅ Professional deployment experience
- ✅ Windows integration (Start Menu, file associations)
- ✅ Proper uninstall support
- ✅ Version management and upgrades
- ✅ Corporate deployment friendly
- ✅ Digital signing support (with code signing certificate)

**Target Use Cases**:
- Corporate deployments
- End-user installations
- Software distribution via SCCM/Intune
- Professional software delivery
- Long-term installations requiring integration

## Build Scripts Reference

### `build-distribution.ps1` (Main Script)

Comprehensive script that handles both distribution types.

**Parameters**:
- `-Configuration` - Build configuration (Debug/Release, default: Release)
- `-OutputPath` - Output directory (default: ./dist)
- `-ZipOnly` - Build only ZIP distribution
- `-MsiOnly` - Build only MSI installer  
- `-Clean` - Clean previous builds before starting

**Example Usage**:
```powershell
# Production build with cleanup
.\build-distribution.ps1 -Configuration Release -Clean -OutputPath "./release"

# Development ZIP build
.\build-distribution.ps1 -Configuration Debug -ZipOnly

# Enterprise MSI build
.\build-distribution.ps1 -MsiOnly -OutputPath "./enterprise-dist"
```

### `installer/build-msi.ps1` (MSI-Specific)

Dedicated script for MSI installer creation with advanced options.

**Parameters**:
- `-Configuration` - Build configuration (default: Release)
- `-PublishDir` - Published application directory
- `-SourceDir` - Source code directory  
- `-OutputDir` - MSI output directory
- `-SkipPublish` - Skip application republishing
- `-Verbose` - Enable verbose WiX output

**Example Usage**:
```powershell
# Standard MSI build
.\build-msi.ps1

# Use existing publish directory
.\build-msi.ps1 -SkipPublish -PublishDir "../bin/Release/net9.0-windows/win-x86/publish"

# Debug build with verbose output
.\build-msi.ps1 -Configuration Debug -Verbose
```

## File Structure

### ZIP Distribution Structure
```
DatabaseMigrationTool-v1.0.0-Windows-x86.zip
├── DatabaseMigrationTool.exe
├── README.txt
├── USER_GUIDE.md
├── *.dll (runtime libraries)
└── firebird/
    ├── *.dll (Firebird libraries)
    ├── *.conf (configuration files)
    └── firebird.msg
```

### MSI Installation Structure
```
C:\Program Files\Database Migration Tool\
├── DatabaseMigrationTool.exe
├── README.md
├── USER_GUIDE.md
├── *.dll (runtime libraries)
└── firebird/
    ├── *.dll (Firebird libraries)
    ├── *.conf (configuration files)
    └── firebird.msg

Start Menu:
└── Database Migration Tool/
    ├── Database Migration Tool
    └── Database Migration Tool (Console)
```

## Advanced Configuration

### Customizing MSI Installer

Edit `installer/DatabaseMigrationTool.wxs` to customize:

1. **Product Information**:
   ```xml
   <Product Id="*" 
            Name="Your Custom Name" 
            Manufacturer="Your Company"
            Version="1.0.0" />
   ```

2. **Installation Directory**:
   ```xml
   <Directory Id="INSTALLFOLDER" Name="Your Application Name" />
   ```

3. **File Associations**:
   ```xml
   <RegistryValue Root="HKLM" 
                  Key="Software\Classes\.yourext" 
                  Value="YourApp.FileType" />
   ```

4. **Custom UI**:
   - Replace `installer/License.rtf` with your license
   - Replace `installer/Banner.bmp` (493×58 pixels)
   - Replace `installer/Dialog.bmp` (493×312 pixels)

### Digital Code Signing

For production MSI distributions, add code signing:

```powershell
# Sign MSI after creation
signtool sign /f "certificate.pfx" /p "password" /t "http://timestamp.digicert.com" "DatabaseMigrationTool-Setup.msi"
```

### Automated Builds

Integrate with CI/CD pipelines:

**Azure DevOps Pipeline** (`azure-pipelines.yml`):
```yaml
steps:
- task: DotNetCoreCLI@2
  displayName: 'Restore Dependencies'
  inputs:
    command: 'restore'
    projects: 'src/**/*.csproj'

- task: PowerShell@2
  displayName: 'Build Distributions'
  inputs:
    filePath: 'build-distribution.ps1'
    arguments: '-Configuration Release -Clean'

- task: PublishBuildArtifacts@1
  displayName: 'Publish Distributions'
  inputs:
    pathToPublish: './dist'
    artifactName: 'DatabaseMigrationTool-Distributions'
```

**GitHub Actions** (`.github/workflows/build.yml`):
```yaml
- name: Build Distributions
  run: .\build-distribution.ps1 -Configuration Release -Clean
  shell: pwsh

- name: Upload Distributions
  uses: actions/upload-artifact@v3
  with:
    name: DatabaseMigrationTool-Distributions
    path: ./dist/
```

## Distribution Checklist

### Pre-Release Testing

- [ ] Test ZIP extraction and execution on clean Windows machine
- [ ] Test MSI installation, execution, and uninstallation
- [ ] Verify all Firebird DLLs are included and functional
- [ ] Test both GUI and console modes
- [ ] Verify file associations work (MSI only)
- [ ] Test on Windows 7 SP1, 10, and 11
- [ ] Verify antivirus compatibility

### Release Preparation

- [ ] Update version numbers in project files
- [ ] Update CHANGELOG.md with release notes
- [ ] Generate both ZIP and MSI distributions
- [ ] Code sign MSI installer (production)
- [ ] Create release notes
- [ ] Test installation on target environments

### Distribution

- [ ] Upload to GitHub Releases
- [ ] Distribute via corporate channels
- [ ] Update download links in documentation
- [ ] Notify users of new version availability

## Troubleshooting

### Common Issues

**"WiX Toolset not found"**
```powershell
# Install via Chocolatey
choco install wixtoolset

# Or download from https://wixtoolset.org/releases/
```

**"Firebird DLLs missing"**
- Ensure MSBuild target `CopyFirebirdDllsToRoot` executed
- Check `firebird/` directory contains all required DLLs
- Verify Visual C++ runtime DLLs are included

**"Application won't start on target machine"**
- Ensure self-contained deployment is used
- Check Windows version compatibility (Windows 7 SP1+)
- Verify x86 architecture compatibility

**MSI installation fails**
- Run MSI with logging: `msiexec /i installer.msi /l*v install.log`
- Check Windows Installer version (3.1+)
- Verify administrative privileges for system-wide installation

### Getting Help

- **Documentation**: See `USER_GUIDE.md` for application usage
- **Issues**: Report problems via GitHub Issues
- **Build Problems**: Check PowerShell execution policy and .NET SDK installation

## Version History

- **v1.0.0**: Initial distribution setup with ZIP and MSI support
- Added comprehensive build scripts and documentation
- Implemented professional MSI installer with full Windows integration
- Self-contained deployment with all dependencies included

---

*This distribution system ensures professional deployment options suitable for both portable use cases and enterprise installations.*