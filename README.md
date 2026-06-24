# FitQuest API

Backend API for an AI-powered fitness app. It handles authentication, user profiles, daily check-ins, workout planning, training history, weekly scheduling, stats, and nutrition advice.

## Highlights

- JWT authentication with protected user-specific data.
- AI workout generation and adjustment with structured reasoning output.
- AI nutrition advice that uses profile data, recent training frequency, today's check-in, and today's workout when available.
- Daily check-in recovery scoring from sleep, energy, stress, body weight, and recent training load.
- Weekly plan generation based on experience level, goal, completed sessions, and muscle-group rotation.
- SQLite for local development and PostgreSQL/Supabase for production.

## Tech Stack

- .NET 10 / ASP.NET Core
- Entity Framework Core
- SQLite, PostgreSQL / Supabase
- JWT Bearer auth
- OpenAI-compatible chat completions via DeepSeek or Azure AI Foundry
- Render-ready deployment config

## AI Flow

1. `POST /api/plan/generate`
   Builds a workout plan from profile, requested muscle group, recent sessions, completed muscle groups today, and optional daily check-in.

2. `POST /api/plan/adjust`
   Rewrites the current plan for cases like low energy, short time, exercise swaps, or higher intensity.

3. `GET /api/nutrition`
   Calculates BMR/TDEE, macros, calories, and meal suggestions. If today's workout exists, nutrition is adjusted around that training plan.

AI responses are constrained to JSON so the frontend can render plans, explanations, and nutrition data reliably.

## Run Locally

```bash
cp .env.example .env
dotnet run
```

The API runs on `http://localhost:3001` by default.

Typical local `.env`:

```env
DATABASE_PROVIDER=sqlite
DATABASE_URL=Data Source=fitness.db
AI_API_KEY=your-api-key
AI_BASE_URL=https://api.deepseek.com
AI_MODEL=deepseek-v4-flash
FRONTEND_ORIGINS=http://localhost:5173
```

For production, set `JWT_SECRET` to a long random value.

Azure AI Foundry is also supported. Set `AI_BASE_URL` to a `services.ai.azure.com` endpoint and the backend switches to Azure's chat-completions path automatically.

## Main Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/register` | Create account |
| `POST` | `/api/auth/login` | Login and receive JWT |
| `GET` | `/api/me` | Current user and profile |
| `PUT` | `/api/profile` | Update profile |
| `POST` | `/api/checkin` | Save daily check-in |
| `GET` | `/api/checkin/today` | Today's check-in |
| `POST` | `/api/plan/generate` | Generate workout plan |
| `POST` | `/api/plan/adjust` | Adjust workout plan |
| `POST` | `/api/training-sessions` | Save completed session |
| `GET` | `/api/training-sessions` | Session history |
| `GET` | `/api/week-plan` | Weekly schedule |
| `GET` | `/api/nutrition` | AI nutrition advice |
| `GET` | `/api/stats` | Streak and weekly stats |
| `GET` | `/health` | Health and AI config |

All endpoints except auth and health require `Authorization: Bearer <token>`.

## Project Structure

```text
Controllers/   API endpoints and request handling
Data/          EF Core DbContext and entities
Models/        DTOs and AI response models
Services/      AI client and JWT token service
Program.cs     App startup, middleware, rate limiting, database init
```

## Notes

- AI endpoints are rate-limited to 10 requests per user per hour.
- Database tables are created automatically on startup.
- User-facing backend responses and AI-generated content are English-only.
