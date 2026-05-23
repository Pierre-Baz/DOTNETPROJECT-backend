using MongoDB.Driver;
using NetManage.Api.Configuration;
using NetManage.Api.Models;

namespace NetManage.Api.Services;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(MongoDbSettings settings)
    {
        var client = new MongoClient(settings.ConnectionString);
        _database = client.GetDatabase(settings.DatabaseName);
        Users = _database.GetCollection<User>("users");
        Projects = _database.GetCollection<Project>("projects");
        Tasks = _database.GetCollection<ProjectTask>("tasks");
        TaskComments = _database.GetCollection<TaskComment>("taskComments");
    }

    public IMongoCollection<User> Users { get; }

    public IMongoCollection<Project> Projects { get; }

    public IMongoCollection<ProjectTask> Tasks { get; }

    public IMongoCollection<TaskComment> TaskComments { get; }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexKeys = Builders<User>.IndexKeys.Ascending(user => user.Email);
        var indexOptions = new CreateIndexOptions
        {
            Unique = true,
            Name = "ux_users_email"
        };

        var indexModel = new CreateIndexModel<User>(indexKeys, indexOptions);
        await Users.Indexes.CreateOneAsync(indexModel, cancellationToken: cancellationToken);

        var projectIndexModels = new[]
        {
            new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending(project => project.OwnerId),
                new CreateIndexOptions { Name = "ix_projects_ownerId" }),
            new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending(project => project.MemberIds),
                new CreateIndexOptions { Name = "ix_projects_memberIds" })
        };

        await Projects.Indexes.CreateManyAsync(projectIndexModels, cancellationToken: cancellationToken);

        var taskIndexModels = new[]
        {
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.ProjectId),
                new CreateIndexOptions { Name = "ix_tasks_projectId" }),
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.AssignedToUserId),
                new CreateIndexOptions { Name = "ix_tasks_assignedToUserId" }),
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.Status),
                new CreateIndexOptions { Name = "ix_tasks_status" }),
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.DueDate),
                new CreateIndexOptions { Name = "ix_tasks_dueDate" })
        };

        await Tasks.Indexes.CreateManyAsync(taskIndexModels, cancellationToken: cancellationToken);

        var taskCommentIndexModels = new[]
        {
            new CreateIndexModel<TaskComment>(
                Builders<TaskComment>.IndexKeys
                    .Ascending(comment => comment.ProjectId)
                    .Ascending(comment => comment.TaskId)
                    .Ascending(comment => comment.CreatedAt),
                new CreateIndexOptions { Name = "ix_taskComments_projectId_taskId_createdAt" }),
            new CreateIndexModel<TaskComment>(
                Builders<TaskComment>.IndexKeys.Ascending(comment => comment.CreatedByUserId),
                new CreateIndexOptions { Name = "ix_taskComments_createdByUserId" })
        };

        await TaskComments.Indexes.CreateManyAsync(taskCommentIndexModels, cancellationToken: cancellationToken);
    }
}
