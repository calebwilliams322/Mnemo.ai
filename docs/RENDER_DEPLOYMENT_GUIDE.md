# Render Deployment Guide

> Complete guide to deploying Mnemo Insurance on Render with Supabase

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                        RENDER                           │
│  ┌─────────────────┐      ┌─────────────────┐          │
│  │  mnemo-frontend │ ───► │   mnemo-api     │          │
│  │  (static site)  │      │   (Docker)      │          │
│  └─────────────────┘      └────────┬────────┘          │
└────────────────────────────────────┼────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────┐
│                      SUPABASE                           │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐              │
│  │ Postgres │  │   Auth   │  │ Storage  │              │
│  │ Database │  │  (JWT)   │  │ (files)  │              │
│  └──────────┘  └──────────┘  └──────────┘              │
└─────────────────────────────────────────────────────────┘
```

**Render hosts:**
- `mnemo-api` - .NET 9 API in Docker container
- `mnemo-frontend` - Static React/Vite site

**Supabase provides:**
- PostgreSQL database (with pgvector for embeddings)
- Authentication (JWT tokens, password reset)
- Storage (PDF file uploads)

---

## Prerequisites

Before starting, ensure you have:

1. **GitHub repo** connected: `github.com/calebwilliams322/Mnemo.ai`
2. **Supabase project** already set up with:
   - Database schema migrated
   - Storage bucket named `documents`
   - Auth configured
3. **API keys ready:**
   - Anthropic API key (Claude)
   - OpenAI API key (embeddings)

---

## Step 1: Connect Repository to Render

1. Go to [render.com](https://render.com) and sign in
2. Click **New** → **Blueprint**
3. Connect your GitHub account if not already connected
4. Select the repository: `calebwilliams322/Mnemo.ai`
5. Render will auto-detect `render.yaml` and show two services:
   - `mnemo-api` (Web Service - Docker)
   - `mnemo-frontend` (Static Site)
6. Click **Apply** to create both services

> Note: The services will fail initially because environment variables aren't set yet. That's expected.

---

## Step 2: Configure API Environment Variables

Go to **Render Dashboard** → **mnemo-api** → **Environment**

Add these environment variables:

### Database
```
DATABASE_CONNECTION_STRING = Host=db.xxx.supabase.co;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;Port=5432;SSL Mode=Require;Trust Server Certificate=true
```

Get this from: **Supabase Dashboard** → **Settings** → **Database** → **Connection string** → **URI**

Convert the URI format to the connection string format above.

### Supabase Auth
```
SUPABASE_URL = https://xxx.supabase.co
SUPABASE_JWT_SECRET = your-jwt-secret
```

Get these from: **Supabase Dashboard** → **Settings** → **API**
- `SUPABASE_URL` = Project URL
- `SUPABASE_JWT_SECRET` = JWT Secret (under JWT Settings)

### Supabase Storage
```
SUPABASE_STORAGE_URL = https://xxx.supabase.co
SUPABASE_SERVICE_KEY = your-service-role-key
```

Get from: **Supabase Dashboard** → **Settings** → **API**
- `SUPABASE_STORAGE_URL` = Same as SUPABASE_URL
- `SUPABASE_SERVICE_KEY` = `service_role` key (keep secret!)

### AI APIs
```
ANTHROPIC_API_KEY = sk-ant-xxx
OPENAI_API_KEY = sk-xxx
```

Get from:
- [console.anthropic.com](https://console.anthropic.com) → API Keys
- [platform.openai.com](https://platform.openai.com) → API Keys

### Frontend URL (for CORS)
```
FRONTEND_URL = https://mnemo-frontend.onrender.com
```

Set this AFTER the frontend deploys - use the actual URL Render assigns.

---

## Step 3: Configure Frontend Environment Variables

Go to **Render Dashboard** → **mnemo-frontend** → **Environment**

Add these environment variables (used at build time):

```
VITE_API_URL = https://mnemo-api.onrender.com
VITE_SUPABASE_URL = https://xxx.supabase.co
VITE_SUPABASE_ANON_KEY = your-anon-key
```

Get `VITE_SUPABASE_ANON_KEY` from: **Supabase Dashboard** → **Settings** → **API** → `anon` `public` key

> Note: Set `VITE_API_URL` after the API deploys - use the actual URL Render assigns.

---

## Step 4: Deploy Services

1. Go to each service in Render dashboard
2. Click **Manual Deploy** → **Deploy latest commit**
3. Wait for builds to complete (5-10 minutes)

**Deploy order:**
1. Deploy `mnemo-api` first
2. Copy the API URL (e.g., `https://mnemo-api.onrender.com`)
3. Set `VITE_API_URL` in frontend environment
4. Deploy `mnemo-frontend`
5. Copy frontend URL (e.g., `https://mnemo-frontend.onrender.com`)
6. Set `FRONTEND_URL` in API environment
7. Redeploy API

---

## Step 5: Configure Supabase for Production

### Update Site URL

1. Go to **Supabase Dashboard** → **Authentication** → **URL Configuration**
2. Set **Site URL** to your frontend URL: `https://mnemo-frontend.onrender.com`
3. Add to **Redirect URLs**:
   - `https://mnemo-frontend.onrender.com`
   - `https://mnemo-frontend.onrender.com/*`

This ensures password reset emails and auth redirects go to the right place.

### Verify Storage Bucket

1. Go to **Supabase Dashboard** → **Storage**
2. Ensure `documents` bucket exists
3. Check bucket policies allow authenticated uploads

---

## Step 6: Verify Deployment

### Health Check
```bash
curl https://mnemo-api.onrender.com/health
```

Expected response:
```json
{"status":"healthy","timestamp":"2024-..."}
```

### Test Frontend
1. Navigate to `https://mnemo-frontend.onrender.com`
2. You should see the landing page
3. Try signing up / logging in

### Test Full Flow
1. Sign up for a new account
2. Upload a PDF document
3. Chat with the document
4. Generate a proposal

---

## Step 7: Enable Auto-Deploy (Optional)

To automatically deploy when you push to `main`:

### Option A: Render Native Auto-Deploy
1. Go to **Render Dashboard** → **mnemo-api** → **Settings**
2. Under **Build & Deploy**, enable **Auto-Deploy**
3. Repeat for `mnemo-frontend`

### Option B: CI-Triggered Deploy (via GitHub Actions)
1. Go to **Render Dashboard** → **mnemo-api** → **Settings**
2. Under **Build & Deploy**, click **Create Deploy Hook**
3. Copy the URL
4. Go to **GitHub** → **Your Repo** → **Settings** → **Secrets and variables** → **Actions**
5. Click **New repository secret**
6. Name: `RENDER_DEPLOY_HOOK`
7. Value: paste the deploy hook URL
8. Save

Now pushes to `main` will: build → test → deploy automatically via CI.

---

## Environment Variables Summary

### API Service (`mnemo-api`)

| Variable | Description | Where to get it |
|----------|-------------|-----------------|
| `DATABASE_CONNECTION_STRING` | PostgreSQL connection | Supabase → Settings → Database |
| `SUPABASE_URL` | Supabase project URL | Supabase → Settings → API |
| `SUPABASE_JWT_SECRET` | JWT signing secret | Supabase → Settings → API |
| `SUPABASE_STORAGE_URL` | Same as SUPABASE_URL | Supabase → Settings → API |
| `SUPABASE_SERVICE_KEY` | Service role key | Supabase → Settings → API |
| `ANTHROPIC_API_KEY` | Claude API key | console.anthropic.com |
| `OPENAI_API_KEY` | OpenAI API key | platform.openai.com |
| `FRONTEND_URL` | Frontend URL for CORS | Render (after frontend deploys) |

### Frontend Service (`mnemo-frontend`)

| Variable | Description | Where to get it |
|----------|-------------|-----------------|
| `VITE_API_URL` | API URL | Render (after API deploys) |
| `VITE_SUPABASE_URL` | Supabase project URL | Supabase → Settings → API |
| `VITE_SUPABASE_ANON_KEY` | Supabase anon key | Supabase → Settings → API |

---

## Troubleshooting

### API won't start
- Check logs in Render dashboard
- Verify all environment variables are set
- Ensure `DATABASE_CONNECTION_STRING` format is correct

### CORS errors in browser
- Ensure `FRONTEND_URL` is set correctly in API
- Must include `https://` prefix
- Redeploy API after changing

### Auth redirects to wrong URL
- Update Site URL in Supabase Authentication settings
- Add frontend URL to Redirect URLs list

### File uploads fail
- Check `documents` bucket exists in Supabase Storage
- Verify `SUPABASE_SERVICE_KEY` is correct
- Check bucket policies

### Database connection fails
- Verify connection string format
- Check Supabase is not paused (free tier pauses after inactivity)
- Ensure SSL mode is set correctly

---

## Cost Estimate

| Service | Plan | Monthly Cost |
|---------|------|--------------|
| Render API | Starter | $7 |
| Render API | Standard (400+ users) | $25 |
| Render Frontend | Static | Free |
| Supabase | Free tier | $0 |
| Supabase | Pro (if needed) | $25 |
| **Total (small)** | | **~$7-32/month** |
| **Total (400+ users)** | | **~$50/month** |

> Note: AI API costs (Anthropic/OpenAI) are separate and usage-based. Expect $50-300/month at scale.

---

## Files Reference

| File | Purpose |
|------|---------|
| `render.yaml` | Render Blueprint - defines services |
| `Dockerfile` | API container build |
| `.github/workflows/ci.yml` | CI/CD pipeline |
| `frontend/Dockerfile.dev` | Local development only |
