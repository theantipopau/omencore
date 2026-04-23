content = r"""name: Release Build

on:
  push:
    tags:
      - 'v*'

jobs:
  build-windows:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 8.0.x
        
    - name: Install Inno Setup
      run: choco install innosetup -y

    - name: Read VERSION
      id: version
      shell: pwsh
      run: |
        $version = (Get-Content VERSION.txt | Select-Object -First 1).Trim()
        if ([string]::IsNullOrWhiteSpace($version)) { throw "VERSION.txt must contain a version" }
        echo "value=$version" >> $env:GITHUB_OUTPUT

    - name: Build installer artifacts
      shell: pwsh
      run: ./build-installer.ps1 -Configuration Release -Runtime win-x64 -SingleFile
    
    - name: Verify installer artifact
      shell: pwsh
      run: |
        $installer = "artifacts/OmenCoreSetup-${{ steps.version.outputs.value }}.exe"
        if (-not (Test-Path $installer)) {
          throw "Expected installer was not created: $installer"
        }
        
    - name: Upload Windows artifacts
      uses: actions/upload-artifact@v4
      with:
        name: windows-artifacts
        path: |
          artifacts/OmenCore-${{ steps.version.outputs.value }}-win-x64.zip
          artifacts/OmenCoreSetup-${{ steps.version.outputs.value }}.exe

  build-linux:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 8.0.x

    - name: Read VERSION
      id: version
      run: |
        version=$(head -1 VERSION.txt | tr -d '\r\n ')
        echo "value=$version" >> $GITHUB_OUTPUT

    - name: Install PowerShell
      run: sudo apt-get install -y powershell

    - name: Build Linux artifacts via build-linux-package.ps1
      shell: pwsh
      run: ./build-linux-package.ps1 -Configuration Release -Runtime linux-x64
        
    - name: Upload Linux artifacts
      uses: actions/upload-artifact@v4
      with:
        name: linux-artifacts
        path: artifacts/OmenCore-${{ steps.version.outputs.value }}-linux-x64.zip

  release:
    needs: [build-windows, build-linux]
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Read VERSION
      id: version
      run: |
        version=$(head -1 VERSION.txt | tr -d '\r\n ')
        echo "value=$version" >> $GITHUB_OUTPUT
        
    - name: Download Windows artifacts
      uses: actions/download-artifact@v4
      with:
        name: windows-artifacts
        path: artifacts
        
    - name: Download Linux artifacts
      uses: actions/download-artifact@v4
      with:
        name: linux-artifacts
        path: artifacts
      
    - name: Create Release
      uses: softprops/action-gh-release@v1
      with:
        files: |
          artifacts/OmenCore-${{ steps.version.outputs.value }}-win-x64.zip
          artifacts/OmenCoreSetup-${{ steps.version.outputs.value }}.exe
          artifacts/OmenCore-${{ steps.version.outputs.value }}-linux-x64.zip
        body: |
          ## OmenCore ${{ github.ref_name }}
          
          ### Downloads
          - **Windows Installer**: `OmenCoreSetup-${{ steps.version.outputs.value }}.exe` (recommended)
          - **Windows Portable**: `OmenCore-${{ steps.version.outputs.value }}-win-x64.zip`
          - **Linux (CLI + Avalonia GUI)**: `OmenCore-${{ steps.version.outputs.value }}-linux-x64.zip`
          
          ### System Requirements
          - **Windows**: Windows 10/11 (x64), .NET 8 Runtime (included in installer)
          - **Linux**: x64, kernel modules: `ec_sys` (write_support=1), `hp-wmi`
          - HP OMEN or Victus gaming laptop
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
"""
with open('.github/workflows/release.yml', 'w', encoding='utf-8', newline='\n') as f:
    f.write(content)
print('Done')
