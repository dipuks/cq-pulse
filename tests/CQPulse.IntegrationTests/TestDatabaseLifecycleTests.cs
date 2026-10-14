using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.IntegrationTests;

public sealed class TestDatabaseLifecycleTests
{
    [Fact]
    public async Task Create_migrates_a_new_database_and_dispose_drops_it()
    {
        var database = await TestDatabase.CreateAsync();
        string name = database.Name;
        try
        {
            Assert.True(await TestDatabase.ExistsAsync(name));
            await using var db = database.CreateDbContext();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }

        Assert.False(await TestDatabase.ExistsAsync(name));
    }

    [Fact]
    public async Task Dispose_is_idempotent()
    {
        var database = await TestDatabase.CreateAsync();

        await database.DisposeAsync();
        await database.DisposeAsync();

        Assert.False(await TestDatabase.ExistsAsync(database.Name));
    }

    [Theory]
    [InlineData("CQPulse")]
    [InlineData("cqpulse")]
    [InlineData("master")]
    [InlineData("CQPulse_Tests_")]
    [InlineData("CQPulse_Tests_not-a-guid")]
    [InlineData("CQPulse_Tests_0123456789abcdef0123456789abcdef];DROP DATABASE CQPulse;--")]
    public void Guard_refuses_non_test_database_names(string name)
    {
        Assert.False(TestDatabase.IsTestDatabaseName(name));
        Assert.Throws<InvalidOperationException>(() => TestDatabase.EnsureSafe(name));
    }

    [Fact]
    public void Guard_accepts_generated_test_database_names()
    {
        var name = TestDatabase.Prefix + Guid.NewGuid().ToString("N");

        Assert.True(TestDatabase.IsTestDatabaseName(name));
        TestDatabase.EnsureSafe(name);
    }
}
