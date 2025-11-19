#!/bin/bash

# OmenCore GitHub Upload Script
# This script initializes the git repo and pushes to GitHub

echo "========================================="
echo "  OmenCore GitHub Upload Script"
echo "========================================="
echo ""

# Check if git is initialized
if [ ! -d ".git" ]; then
    echo "Initializing Git repository..."
    git init
    echo "✓ Git initialized"
else
    echo "✓ Git repository already initialized"
fi

# Add .gitignore
if [ ! -f ".gitignore" ]; then
    echo "Error: .gitignore not found!"
    exit 1
fi

# Stage all files
echo ""
echo "Staging files..."
git add .
echo "✓ Files staged"

# Check if there are changes to commit
if git diff --staged --quiet; then
    echo "⚠ No changes to commit"
else
    # Commit
    echo ""
    echo "Committing changes..."
    git commit -m "Initial OmenCore commit - v1.0.0

Features:
- Fan & thermal control with custom curves
- CPU undervolting support
- RGB lighting profiles
- Hardware monitoring
- Corsair/Logitech device integration
- Auto-update via GitHub releases
- HP Omen system detection
- System optimization tools"
    echo "✓ Changes committed"
fi

# Check if remote exists
if git remote get-url origin &> /dev/null; then
    echo "✓ Remote 'origin' already configured"
else
    echo ""
    echo "Adding remote repository..."
    git remote add origin https://github.com/theantipopau/omencore.git
    echo "✓ Remote added"
fi

# Set main branch
echo ""
echo "Setting main branch..."
git branch -M main
echo "✓ Branch set to main"

# Push to GitHub
echo ""
echo "Pushing to GitHub..."
echo "You may be prompted for your GitHub credentials..."
git push -u origin main

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================="
    echo "  ✓ Successfully uploaded to GitHub!"
    echo "========================================="
    echo ""
    echo "Repository: https://github.com/theantipopau/omencore"
    echo ""
    echo "Next steps:"
    echo "1. Create first release: git tag v1.0.0 && git push origin v1.0.0"
    echo "2. GitHub Actions will automatically build and publish"
    echo "3. Users can then auto-update from within the app"
    echo ""
else
    echo ""
    echo "========================================="
    echo "  ✗ Push failed"
    echo "========================================="
    echo ""
    echo "Possible issues:"
    echo "- GitHub credentials not configured"
    echo "- Repository doesn't exist (create it at github.com/theantipopau/omencore)"
    echo "- Network connectivity problems"
    echo ""
    echo "Try manually:"
    echo "  git push -u origin main"
    exit 1
fi
