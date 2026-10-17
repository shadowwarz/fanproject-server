# GitHub Setup & Deployment Guide: Durango Custom Server

This repository contains the reconstructed and optimized **Durango Custom Server (v5.2.1)** with full Mobile & PC cross-platform compatibility, prepared for deployment on the Mulerun VPS.

---

## 1. Remote Repository Creation

If the repository `shadowwarz/durango-server` has not been created on GitHub yet, choose one of the following methods:

### Option A: Web Interface (Recommended)
1. Navigate directly to: **[https://github.com/new?name=durango-server](https://github.com/new?name=durango-server)**
2. Set **Repository name**: `durango-server`
3. Set Visibility: **Public** or **Private** (per preference)
4. ⚠️ **Do NOT check** "Add a README file", "Add .gitignore", or "Choose a license" (the repository is already initialized locally).
5. Click **Create repository**.

### Option B: GitHub CLI (if installed)
```bash
gh repo create shadowwarz/durango-server --public --source=. --remote=origin
```

---

## 2. Push to GitHub

Once the remote repository is created:

### Default HTTPS Push (One-liner):
```bash
git push -u origin main
```

*(If prompted, authenticate with your GitHub Personal Access Token or browser credentials).*

### SSH Alternative (if SSH keys are configured):
```bash
git remote set-url origin git@github.com:shadowwarz/durango-server.git
git push -u origin main
```

---

## 3. Mulerun VPS Deployment & Fetching

On your Linux / Windows VPS (Mulerun):

### Initial Setup (Clone):
```bash
# Clone the repository
git clone https://github.com/shadowwarz/durango-server.git
cd durango-server/server

# Run the server (.NET 9)
dotnet run --project DurangoServer.csproj
```

### Updating Existing Installation (Pull):
```bash
cd durango-server
git pull origin main
cd server
dotnet run --project DurangoServer.csproj
```

---

## Repository Configuration Summary
- **Default Branch**: `main`
- **HTTPS Remote**: `https://github.com/shadowwarz/durango-server.git`
- **SSH Remote**: `git@github.com:shadowwarz/durango-server.git`
- **Gateway Port**: `8190`
- **Game Server Port**: `8191`
