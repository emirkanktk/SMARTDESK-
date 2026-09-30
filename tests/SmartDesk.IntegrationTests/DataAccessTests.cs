using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.IntegrationTests;

// How the API's DbContext is set up, as AddInfrastructure registers it.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DataAccessTests(PostgresApiFixture fixture)
{
    [Fact]
    public void Collections_load_one_query_each()
    {
        // In one query, a resource's features would come back once per team it has.
        using var scope = fixture.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>().GetService<IDbContextOptions>();

        RelationalOptionsExtension.Extract(options).QuerySplittingBehavior.Should().Be(QuerySplittingBehavior.SplitQuery);
    }
}
