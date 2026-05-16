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

## Phase 3 Features

- MongoDB-backed project/workspace storage
- Project ownership for the user who creates the project
- Project membership by registered user email
- Owner-only project updates, deletion, and member management
- Member-only project viewing

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

## Project/Workspace API

Projects are the workspace area where tasks will be added in a later phase. The user who creates a project becomes the owner. The owner is also stored as a member. Project members can view the project, while only the owner can update or delete the project and add or remove members.

Project endpoints:

- `GET /api/projects` - list projects where the current user is a member
- `POST /api/projects` - create a project as the current user
- `GET /api/projects/{id}` - view one project as a member
- `PUT /api/projects/{id}` - update a project as the owner
- `DELETE /api/projects/{id}` - delete a project as the owner
- `GET /api/projects/{id}/members` - list project members as a member
- `POST /api/projects/{id}/members` - add a registered user by email as the owner
- `DELETE /api/projects/{id}/members/{userId}` - remove a member as the owner

Short local testing flow:

1. Start MongoDB at `mongodb://localhost:27017`.
2. Run the backend with `dotnet run` from `Backend/NetManage.Api`.
3. Register two users from Swagger.
4. Login as user A and authorize Swagger with `Bearer <token>`.
5. Create a project with `POST /api/projects`.
6. Add user B with `POST /api/projects/{id}/members`.
7. Login as user B and confirm `GET /api/projects` includes the project.
8. Confirm user B gets `403 Forbidden` when trying to update or delete it.
9. Login again as user A and remove user B from the project.

## Swagger JWT Usage

1. Register or log in from Swagger.
2. Copy the returned JWT token.
3. Click `Authorize` in Swagger UI.
4. Enter `Bearer <token>`.
5. Call `GET /api/me`.
