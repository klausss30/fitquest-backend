# FitQuest API — Backend

ASP.NET Core (.NET 10) REST API for the FitQuest Reasoning Agent fitness platform. Handles authentication, AI workout generation via a multi-agent architecture, session logging, daily check-ins, and nutrition planning.

## Tech Stack

- **Runtime**: .NET 10 / ASP.NET Core
- **ORM**: Entity Framework Core
- **Databases**: SQLite (development) · PostgreSQL / Supabase (production)
- **Auth**: JWT Bearer tokens
- **AI**: Azure AI Foundry or DeepSeek AI (OpenAI-compatible API, auto-detected)
- **Deployment**: Render · Cloudflare Tunnel (local dev)

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run locally

```bash
git clone <repo>
cd "fitness app backend"
cp .env.example .env        # fill in your values
dotnet run
# API available at http://localhost:3001
```

### Environment variables

Copy `.env.example` to `.env` and fill in the three required sections:

| Variable | Description |
|---|---|
| `DATABASE_PROVIDER` | `sqlite` (default) or `postgres` |
| `DATABASE_URL` | SQLite path or full PostgreSQL connection string |
| `AI_API_KEY` | API key for the active AI provider |
| `AI_BASE_URL` | Base URL for the AI provider |
| `AI_MODEL` | Model name (e.g. `deepseek-v4-flash`, `DeepSeek-V4-Flash`) |
| `FRONTEND_ORIGINS` | Comma-separated allowed CORS origins |
| `PORT` | Server port (default `3001`) |

**Switching AI providers** — replace only these three variables:

```env
# DeepSeek
AI_API_KEY=your-deepseek-key
AI_BASE_URL=https://api.deepseek.com
AI_MODEL=deepseek-v4-flash

# Azure AI Foundry (auto-detected from the services.ai.azure.com URL)
AI_API_KEY=your-azure-key
AI_BASE_URL=https://your-resource.services.ai.azure.com/models
AI_MODEL=DeepSeek-V4-Flash
```

No code changes needed — the backend detects the provider from `AI_BASE_URL` and adjusts the API path automatically.

## API Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a new user |
| POST | `/api/auth/login` | Login and receive JWT |
| GET | `/api/me` | Get current user + profile |
| PUT | `/api/me/profile` | Update user profile |
| POST | `/api/plan/generate` | Generate AI workout plan |
| POST | `/api/plan/adjust` | Adjust existing plan with AI |
| POST | `/api/training-sessions` | Save completed session |
| GET | `/api/training-sessions` | Get session history |
| GET | `/api/training-sessions/{id}` | Get session detail |
| GET | `/api/training-sessions/week` | Get this week's sessions |
| POST | `/api/checkin` | Submit daily check-in |
| GET | `/api/checkin/today` | Get today's check-in |
| GET | `/api/checkin/history` | Get check-in history |
| GET | `/api/nutrition` | Generate AI nutrition advice |
| GET | `/api/stats` | Get training stats (streak, weekly count) |
| GET | `/api/week-plan` | Get weekly training plan |
| GET | `/health` | Health check + AI provider info |

All endpoints except `/auth/*` and `/health` require `Authorization: Bearer <token>`.

## Multi-Agent Architecture

The API implements a two-agent reasoning pipeline:

**Agent A — Training Plan Agent** (`/api/plan/generate`)
Reads user profile, training history (last 14 sessions), and today's check-in data. Returns a structured workout plan with a `reasoning` object explaining each decision step.

**Agent B — Nutrition Agent** (`/api/nutrition`)
Reads user body metrics, training frequency, today's check-in, and — if already generated — Agent A's training output. Computes BMR/TDEE and returns macro targets with meal suggestions. The cross-agent data link is what enables context-aware nutrition advice from a single daily check-in.

Both agents output a structured `reasoning` block that the frontend displays as a live reasoning chain.

## Rate Limiting

AI endpoints (`/api/plan/generate`, `/api/plan/adjust`, `/api/nutrition`) are rate-limited to **10 requests per user per hour** using a fixed-window limiter keyed on JWT user ID.

## Database

The schema is created automatically on startup:
- **SQLite**: `db.Database.EnsureCreated()` — no migration needed
- **PostgreSQL**: raw `CREATE TABLE IF NOT EXISTS` statements run on startup

To use Supabase, set `DATABASE_PROVIDER=postgres` and `DATABASE_URL` to the full connection string from the Supabase dashboard (Session Mode pooler recommended).

## Project Structure

```
Controllers/       — API endpoints
  AuthController       Register / Login
  PlanController       AI plan generation + adjustment
  CheckInController    Daily check-in CRUD + recovery score
  NutritionController  AI nutrition advice
  TrainingSessionsController  Session logging
  WeekPlanController   Weekly schedule view
  StatsController      Streak + weekly count
  MeController         User profile
  ControllerHelpers    Shared validation + utilities
Data/              — EF Core DbContext + entity models
Services/
  DeepSeekService      OpenAI-compatible AI client (DeepSeek + Azure)
  JwtTokenService      JWT creation + validation
Models/            — Request/response DTOs
Program.cs         — App startup, middleware, DB init
```
