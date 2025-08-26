using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Tests;

public abstract class TestBase
{
    protected AppDbContext Db { get; private set; }

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        Db.Dispose();
    }
}

