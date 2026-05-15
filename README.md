# NetManage Backend

ASP.NET Core Web API backend for NetManage.

## Stack

- ASP.NET Core Web API
- C#
- MongoDB.Driver
- JWT Bearer authentication
- BCrypt password hashing

## Phase 2 Features

- MongoDB-backed user storage
- `POST /api/auth/register` for account creation
- `POST /api/auth/login` for authentication
- Protected `GET /api/me` endpoint for the current user
- Swagger JWT authorize flow for testing secured endpoints

## Requirements

- MongoDB must be running locally at `mongodb://localhost:27017`
- Development database name is `netmanage`

## Configuration

The API reads settings from:

- `appsettings.json`
- `appsettings.Development.json`
- environment variables

Supported environment variables:

- `MONGODB_CONNECTION_STRING`
- `MONGODB_DATABASE_NAME`
- `JWT_SECRET`
- `JWT_ISSUER`
- `JWT_AUDIENCE`
- `FRONTEND_URL`

## Run Locally

```powershell
cd .\NetManage.Api
dotnet restore
dotnet run
```

## Local URLs

- Swagger: http://localhost:5000/swagger
- Health endpoint: http://localhost:5000/api/health
- Register: `POST http://localhost:5000/api/auth/register`
- Login: `POST http://localhost:5000/api/auth/login`
- Current user: `GET http://localhost:5000/api/me`

## Swagger JWT Usage

1. Register or log in from Swagger.
2. Copy the returned JWT token.
3. Click `Authorize` in Swagger UI.
4. Enter `Bearer <token>`.
5. Call `GET /api/me`.
