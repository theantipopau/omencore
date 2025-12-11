# Quick Upload Guide

## Step 1: Create GitHub Repository

1. Go to https://github.com/new
2. Repository name: `omencore`
3. Description: "Advanced control center for HP Omen gaming laptops"
4. Make it **Public** (required for GitHub Actions and releases)
5. **DO NOT** initialize with README, .gitignore, or license (we have them)
6. Click "Create repository"

## Step 2: Upload Code

**Option A - Windows (Easy):**
```cmd
cd C:\Omen
upload-to-github.bat
```

**Option B - Git Bash:**
```bash
cd /c/Omen
bash upload-to-github.sh
```

**Option C - Manual:**
```bash
cd C:\Omen
git init
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/theantipopau/omencore.git
git branch -M main
git push -u origin main
```

## Step 3: Create First Release

After successful upload:

```bash
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will automatically:
- Build the app
- Create a ZIP package
- Publish a GitHub Release

## Step 4: Test Auto-Update

1. Build and run the app: `dotnet run --project src/OmenCoreApp`
2. Click "ℹ About" button in sidebar
3. Click "Check for Updates" button
4. Should connect to GitHub and check for releases

## Troubleshooting

**"Authentication failed":**
- Use Git Credential Manager
- Or generate a Personal Access Token at https://github.com/settings/tokens

**"Repository not found":**
- Make sure the repo exists at https://github.com/theantipopau/omencore
- Check the remote URL: `git remote -v`

**"Push rejected":**
- The repo might have existing content
- Force push (careful!): `git push -u origin main --force`

## After First Upload

Update `VERSION.txt` and create new releases:

```bash
echo "1.0.1" > VERSION.txt
git add .
git commit -m "Release v1.0.1 - Bug fixes"
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

Users will automatically be notified of the update!
