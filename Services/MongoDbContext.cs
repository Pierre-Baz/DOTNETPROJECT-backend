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
        WikiPages = _database.GetCollection<WikiPage>("wikiPages");
        Sprints = _database.GetCollection<ProjectSprint>("sprints");
        Epics = _database.GetCollection<ProjectEpic>("epics");
    }

    public IMongoCollection<User> Users { get; }

    public IMongoCollection<Project> Projects { get; }

    public IMongoCollection<ProjectTask> Tasks { get; }

    public IMongoCollection<WikiPage> WikiPages { get; }

    public IMongoCollection<ProjectSprint> Sprints { get; }

    public IMongoCollection<ProjectEpic> Epics { get; }

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
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.EpicId),
                new CreateIndexOptions { Name = "ix_tasks_epicId" }),
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.Status),
                new CreateIndexOptions { Name = "ix_tasks_status" }),
            new CreateIndexModel<ProjectTask>(
                Builders<ProjectTask>.IndexKeys.Ascending(task => task.DueDate),
                new CreateIndexOptions { Name = "ix_tasks_dueDate" })
        };

        await Tasks.Indexes.CreateManyAsync(taskIndexModels, cancellationToken: cancellationToken);

        var wikiIndexModels = new[]
        {
            new CreateIndexModel<WikiPage>(
                Builders<WikiPage>.IndexKeys.Ascending(page => page.ProjectId),
                new CreateIndexOptions { Name = "ix_wikiPages_projectId" }),
            new CreateIndexModel<WikiPage>(
                Builders<WikiPage>.IndexKeys.Ascending(page => page.CreatedByUserId),
                new CreateIndexOptions { Name = "ix_wikiPages_createdByUserId" })
        };

        await WikiPages.Indexes.CreateManyAsync(wikiIndexModels, cancellationToken: cancellationToken);

        var sprintIndexModels = new[]
        {
            new CreateIndexModel<ProjectSprint>(
                Builders<ProjectSprint>.IndexKeys.Ascending(sprint => sprint.ProjectId),
                new CreateIndexOptions { Name = "ix_sprints_projectId" }),
            new CreateIndexModel<ProjectSprint>(
                Builders<ProjectSprint>.IndexKeys
                    .Ascending(sprint => sprint.ProjectId)
                    .Ascending(sprint => sprint.Status),
                new CreateIndexOptions { Name = "ix_sprints_projectId_status" })
        };

        await Sprints.Indexes.CreateManyAsync(sprintIndexModels, cancellationToken: cancellationToken);

        var epicIndexModels = new[]
        {
            new CreateIndexModel<ProjectEpic>(
                Builders<ProjectEpic>.IndexKeys.Ascending(epic => epic.ProjectId),
                new CreateIndexOptions { Name = "ix_epics_projectId" }),
            new CreateIndexModel<ProjectEpic>(
                Builders<ProjectEpic>.IndexKeys.Ascending(epic => epic.SprintId),
                new CreateIndexOptions { Name = "ix_epics_sprintId" })
        };

        await Epics.Indexes.CreateManyAsync(epicIndexModels, cancellationToken: cancellationToken);
    }
}
