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

## Phase 4 Features

- MongoDB-backed project task storage
- Owner-only task creation, full updates, assignment, and deletion
- Member task viewing inside projects
- Member task status updates for the Kanban board

## Requirements

- MongoDB must be running locally at `mongodb://localhost:27017`
- Development database name is `netmanage`

## Configuration

The API reads settings from:

- `appsettings.json`
- `appsettings.Development.json`
- local `NetManage.Api/.env`
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

Projects are the workspace area where tasks live. The user who creates a project becomes the owner. The owner is also stored as a member. Project members can view the project, while only the owner can update or delete the project and add or remove members.

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
2. Run the backend with `dotnet run` from `NetManage.Api`.
3. Register two users from Swagger.
4. Login as user A and authorize Swagger with `Bearer <token>`.
5. Create a project with `POST /api/projects`.
6. Add user B with `POST /api/projects/{id}/members`.
7. Login as user B and confirm `GET /api/projects` includes the project.
8. Confirm user B gets `403 Forbidden` when trying to update or delete it.
9. Login again as user A and remove user B from the project.

## Task API

Tasks belong to projects and power the Kanban board. Project members can view tasks and update task status. Project owners can create tasks, update full task details, assign tasks to project members, and delete tasks.

Task statuses:

- `Todo`
- `Started`
- `Testing`
- `Finishing`
- `Done`

Task priorities:

- `Low`
- `Medium`
- `High`
- `Critical`

Task endpoints:

- `GET /api/projects/{projectId}/tasks` - list project tasks as a project member
- `POST /api/projects/{projectId}/tasks` - create a task as the project owner
- `GET /api/projects/{projectId}/tasks/{taskId}` - view one project task as a project member
- `PUT /api/projects/{projectId}/tasks/{taskId}` - update full task details as the project owner
- `PATCH /api/projects/{projectId}/tasks/{taskId}/status` - update only task status as a project member
- `DELETE /api/projects/{projectId}/tasks/{taskId}` - delete a task as the project owner

The `PATCH /api/projects/{projectId}/tasks/{taskId}/status` endpoint is used by the Kanban drag-and-drop board.

Task list filters:

- `status`
- `assignedToUserId`
- `priority`

Short local task testing flow:

1. Start MongoDB at `mongodb://localhost:27017`.
2. Run the backend with `dotnet run` from `NetManage.Api`.
3. Register two users from Swagger.
4. Login as user A and authorize Swagger with `Bearer <token>`.
5. Create a project with `POST /api/projects`.
6. Add user B with `POST /api/projects/{id}/members`.
7. Create a task assigned to user B with `POST /api/projects/{id}/tasks`.
8. Confirm user A and user B can list tasks with `GET /api/projects/{id}/tasks`.
9. Login as user B and update status with `PATCH /api/projects/{id}/tasks/{taskId}/status`.
10. Confirm user B gets `403 Forbidden` when trying to update full task details.
11. Login again as user A and update or delete the task.

## Swagger JWT Usage

1. Register or log in from Swagger.
2. Copy the returned JWT token.
3. Click `Authorize` in Swagger UI.
4. Enter `Bearer <token>`.
5. Call `GET /api/me`.
