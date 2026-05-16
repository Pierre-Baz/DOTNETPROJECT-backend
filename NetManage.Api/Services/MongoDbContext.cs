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
    }

    public IMongoCollection<User> Users { get; }

    public IMongoCollection<Project> Projects { get; }

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
    }
}
