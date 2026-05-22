# FitQuest Backend

ASP.NET Core backend for an AI-powered fitness coaching app. The API supports JWT authentication, user profiles, completed training history, AI-generated temporary workout plans, weekly planning, and coach messages.

## Tech Stack

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core
- SQLite for local development
- PostgreSQL/Supabase for production
- JWT authentication
- BCrypt password hashing
- DeepSeek-compatible chat completions API

## Core Concepts

- AI-generated plans are temporary and are not saved as completed training.
- Completed training is saved only when the client calls `POST /api/training-sessions`.
- Training history endpoints only return records owned by the authenticated user.
- AI output language follows `Accept-Language`, with profile language as fallback.

## Local Setup

Install dependencies:

```bash
dotnet restore
```

Create a local `.env` file:

```env
DATABASE_PROVIDER=sqlite
DATABASE_URL=Data Source=fitness.db
JWT_SECRET=change_me_to_a_long_random_secret
JWT_ISSUER=FitQuest.Api
JWT_AUDIENCE=FitQuest.Client
AI_PROVIDER=DeepSeek
AI_API_KEY=your_api_key_here
AI_BASE_URL=https://api.deepseek.com
AI_MODEL=deepseek-v4-flash
FRONTEND_ORIGIN=http://localhost:5173
```

Run the API:

```bash
dotnet run
```

Default local URL:

```text
http://localhost:5000
```

Health check:

```http
GET /api/health
```

## Database

Local development uses SQLite:

```text
fitness.db
```

Production can use PostgreSQL, including Supabase. Set:

```env
DATABASE_PROVIDER=postgres
DATABASE_URL=postgresql://user:password@host:6543/postgres
```

For Supabase, the session pooler connection string is recommended.

The app creates the database schema automatically on startup.

Main tables:

- `users`
- `user_profiles`
- `training_sessions`
- `session_exercises`
- `ai_plan_requests`

Local SQLite files are ignored by Git.

## Environment Variables

| Variable | Description |
| --- | --- |
| `DATABASE_PROVIDER` | `sqlite` locally or `postgres` for PostgreSQL/Supabase. |
| `DATABASE_URL` | SQLite or PostgreSQL connection string. |
| `JWT_SECRET` | Secret used to sign JWT tokens. Use a long random value. |
| `JWT_ISSUER` | JWT issuer. |
| `JWT_AUDIENCE` | JWT audience. |
| `AI_PROVIDER` | AI provider label, currently `DeepSeek`. |
| `AI_API_KEY` | AI API key. |
| `AI_BASE_URL` | AI API base URL. |
| `AI_MODEL` | Model name, currently `deepseek-v4-flash`. |
| `FRONTEND_ORIGIN` | Allowed frontend origin for CORS. |

Legacy DeepSeek variables are also supported:

```env
DEEPSEEK_API_KEY=your_api_key_here
DEEPSEEK_BASE_URL=https://api.deepseek.com
DEEPSEEK_MODEL=deepseek-v4-flash
```

## Authentication

Register:

```http
POST /api/auth/register
Content-Type: application/json
```

```json
{
  "name": "Klaus",
  "email": "klaus@example.com",
  "password": "123456"
}
```

Login:

```http
POST /api/auth/login
Content-Type: application/json
```

```json
{
  "email": "klaus@example.com",
  "password": "123456"
}
```

Authenticated requests must include:

```http
Authorization: Bearer <token>
```

## Profile API

Get current user:

```http
GET /api/me
Authorization: Bearer <token>
```

Create or update profile:

```http
PUT /api/profile
Authorization: Bearer <token>
Content-Type: application/json
```

```json
{
  "experience_level": "beginner",
  "goal": "muscle_gain",
  "gender": "male",
  "height_cm": 178,
  "weight_kg": 75,
  "language": "zh-CN"
}
```

Allowed values:

- `experience_level`: `beginner`, `intermediate`, `advanced`
- `goal`: `muscle_gain`, `fat_loss`, `strength`
- `gender`: `male`, `female`, `not_specified`
- `language`: `system`, `zh-CN`, `en-US`

## AI Plan APIs

Generate a temporary plan:

```http
POST /api/plan/generate
Authorization: Bearer <token>
Accept-Language: zh-CN
Content-Type: application/json
```

```json
{
  "session_date": "2026-05-22",
  "muscle_group": "legs",
  "duration_minutes": 55
}
```

Notes:

- `session_date` is optional.
- `muscle_group` is optional. If omitted, the backend selects a suitable muscle group from recent history.
- This endpoint does not save `training_sessions` or `session_exercises`.

Adjust a temporary plan:

```http
POST /api/plan/adjust
Authorization: Bearer <token>
Accept-Language: zh-CN
Content-Type: application/json
```

```json
{
  "current_plan": {
    "session_date": "2026-05-22",
    "muscle_group": "legs",
    "day_type": "腿部 · 力量日",
    "duration_minutes": 55,
    "ai_note": "今天稳住动作质量。"
  },
  "exercises": [
    {
      "exercise_name": "动态热身",
      "category": "warmup",
      "sets": 1,
      "reps": 1,
      "weight": null,
      "unit": null,
      "rationale": "激活下肢和核心。",
      "sort_order": 1
    }
  ],
  "adjust_type": "short_time",
  "custom_message": "今天只有 30 分钟"
}
```

Allowed exercise categories:

- `warmup`
- `main`
- `accessory`
- `finisher`
- `cooldown`

## Completed Training APIs

Save a completed training session:

```http
POST /api/training-sessions
Authorization: Bearer <token>
Content-Type: application/json
```

```json
{
  "session_date": "2026-05-22",
  "muscle_group": "legs",
  "day_type": "腿部 · 力量日",
  "duration_minutes": 55,
  "ai_note": "今天稳住动作质量。",
  "exercises": [
    {
      "exercise_name": "深蹲",
      "category": "main",
      "sets": 4,
      "reps": 8,
      "weight": 85,
      "unit": "kg",
      "rationale": "主要力量动作。"
    }
  ]
}
```

Get history:

```http
GET /api/training-sessions?limit=20
Authorization: Bearer <token>
```

Get current week:

```http
GET /api/training-sessions/week?start_date=2026-05-18
Authorization: Bearer <token>
```

Get session detail:

```http
GET /api/training-sessions/{id}
Authorization: Bearer <token>
```

All training history queries are scoped to the current JWT user.

## Weekly Planning APIs

Get this week’s training direction plan:

```http
GET /api/week-plan?start_date=2026-05-18
Authorization: Bearer <token>
Accept-Language: zh-CN
```

This endpoint returns training directions only. It does not generate exercise details and does not write to the database.

Coach message:

```http
GET /api/coach/week-message
Authorization: Bearer <token>
Accept-Language: zh-CN
```

Returns a short motivational message for the weekly training page.

## Deployment Notes

SQLite is fine for local development, but it is not recommended for production on Render or similar platforms. Use PostgreSQL for persistent production data.

Render does not need a native .NET runtime for this project. Deploy it as a Docker web service using the included `Dockerfile`.

Recommended production setup:

- Set `DATABASE_PROVIDER=postgres`.
- Set `DATABASE_URL` to a PostgreSQL connection string.
- Store `JWT_SECRET` and `AI_API_KEY` as environment variables.
- Do not commit `.env`, SQLite database files, or real API keys.

Render environment variables:

```env
DATABASE_PROVIDER=postgres
DATABASE_URL=postgresql://user:password@host:6543/postgres
JWT_SECRET=replace_with_a_long_random_secret
JWT_ISSUER=FitQuest.Api
JWT_AUDIENCE=FitQuest.Client
AI_PROVIDER=DeepSeek
AI_API_KEY=replace_with_your_ai_key
AI_BASE_URL=https://api.deepseek.com
AI_MODEL=deepseek-v4-flash
FRONTEND_ORIGIN=https://your-frontend-domain.example
```

Render sets `PORT` automatically. The API listens on that port in production.

## Build

```bash
dotnet build
```
