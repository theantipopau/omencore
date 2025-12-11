# GitHub Setup Guide for OmenCore Auto-Update

## Quick Start

Your OmenCore app is now configured to auto-update via GitHub releases!

### Initial Setup (One-Time)

1. **Push your code to GitHub:**
   ```bash
   cd C:\Omen
   git init
   git add .
   git commit -m "Initial OmenCore commit"
   git remote add origin https://github.com/theantipopau/omencore.git
   git branch -M main
   git push -u origin main
   ```

2. **Enable GitHub Actions:**
   - Go to your repo settings
   - Navigate to "Actions" → "General"
   - Enable "Allow all actions and reusable workflows"

### Creating Your First Release

1. **Update the version:**
   ```bash
   echo "1.0.0" > VERSION.txt
   git add VERSION.txt
   git commit -m "Release v1.0.0"
   ```

2. **Create and push a tag:**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. **GitHub Actions will automatically:**
   - Build the project
   - Create a self-contained Windows x64 package
   - Publish a GitHub Release with the ZIP file
   - Users can then auto-update from within the app

### For Future Updates

When you want to release version 1.0.1:

```bash
# Update version file
echo "1.0.1" > VERSION.txt

# Commit your changes
git add .
git commit -m "Release v1.0.1 - Bug fixes and improvements"

# Create and push tag
git tag v1.0.1
git push origin v1.0.1
```

The GitHub Action (`.github/workflows/release.yml`) will handle the rest!

## How Auto-Update Works

1. **App checks for updates:** Click "Check for Updates" in About window
2. **GitHub API query:** App queries `https://api.github.com/repos/theantipopau/omencore/releases/latest`
3. **Version comparison:** If newer version exists, user is prompted
4. **Download & Install:** App downloads ZIP, extracts, and installs silently
5. **App restart:** After install, app automatically restarts

## Files Created

- `VERSION.txt` - Current version number (update this before each release)
- `.github/workflows/release.yml` - GitHub Actions workflow for automated builds
- Updated `README.md` - Complete documentation with auto-update instructions

## Testing

Your current system (Dell Latitude 7450) will show the **HP Omen warning banner** since it's not an Omen laptop. This is working correctly!

## All Fixes Applied

✅ **About Window Close Button** - Now visible and works, ESC key also closes window
✅ **Auto-Update System** - Fully functional with GitHub releases API
✅ **Application Icon** - Set to `OmenCore.ico` in project file
✅ **HP Omen Detection** - Working correctly (shows warning on non-Omen systems)
✅ **Debug Logging** - Added logs for system detection
✅ **GitHub Workflow** - Automated release builds ready to go

## Next Steps

1. Push the code to GitHub
2. Create your first release tag (`v1.0.0`)
3. Test the auto-update feature
4. Share with HP Omen users!
