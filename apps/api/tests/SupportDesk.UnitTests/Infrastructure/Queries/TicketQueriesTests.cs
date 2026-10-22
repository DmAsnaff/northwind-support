using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SupportDbContext _db;
    private readonly FixedClock _clock = new();
    private readonly TicketQueries _sut;

    public TicketQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();

        SeedTestData();
        _sut = new TicketQueries(_db, _clock);
    }

    [Fact]
    public async Task GetPagedAsync_WhenFilteringByPriority_ReturnsOnlyExactMatchesWithCorrectCount()
    {
        var result = await _sut.GetPagedAsync(
            new TicketQuery { Priority = TicketPriority.Critical },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.Critical, item.Priority));
    }

    [Fact]
    public async Task GetPagedAsync_WhenCombiningFilters_AppliesAllWithLogicalAnd()
    {
        var result = await _sut.GetPagedAsync(
            new TicketQuery
            {
                Status = TicketStatus.InProgress,
                Priority = TicketPriority.High,
                CategoryId = 2,
                AssignedAgentId = 1
            },
            CancellationToken.None);

        var single = Assert.Single(result.Items);
        Assert.Equal("TCK-0002", single.Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_WhenSearchingByCustomerName_MatchesCaseInsensitively()
    {
        var result = await _sut.GetPagedAsync(
            new TicketQuery { Search = "  fabrikam  " },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("Fabrikam Inc", item.Customer.Name));
    }

    [Fact]
    public async Task GetPagedAsync_WhenSearchingByReferenceOrTitle_MatchesBothAndIgnoresBlankSearch()
    {
        var byRef = await _sut.GetPagedAsync(
            new TicketQuery { Search = "TCK-0003" },
            CancellationToken.None);
        Assert.Single(byRef.Items);
        Assert.Equal("TCK-0003", byRef.Items[0].Reference);

        var blank = await _sut.GetPagedAsync(
            new TicketQuery { Search = "   " },
            CancellationToken.None);
        Assert.Equal(4, blank.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_WhenUnassignedOnlyIsTrue_ReturnsOnlyUnassignedTickets()
    {
        var result = await _sut.GetPagedAsync(
            new TicketQuery { UnassignedOnly = true },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
    }

    [Fact]
    public async Task GetPagedAsync_FiltersBeforeCountingAndPaging()
    {
        var result = await _sut.GetPagedAsync(
            new TicketQuery { CustomerId = 2, Page = 1, PageSize = 1 },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
    }

    private void SeedTestData()
    {
        var now = FixedClock.DefaultNow;

        var general = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var billing = new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false);
        _db.AddRange(general, billing);

        var contoso = new Customer("Contoso Ltd", "support@contoso.example", null, CustomerTier.Premium, now);
        var fabrikam = new Customer("Fabrikam Inc", "help@fabrikam.example", null, CustomerTier.Standard, now);
        _db.AddRange(contoso, fabrikam);

        var alex = new Agent("Alex Turner", "alex@northwind.example", 10, now);
        var sara = new Agent("Sara Lindqvist", "sara@northwind.example", 6, now);
        _db.AddRange(alex, sara);
        _db.SaveChanges();

        var t1 = Ticket.Raise("TCK-0001", "Login outage", "Cannot sign in.", contoso.Id, general.Id, TicketPriority.Critical, now);

        var t2 = Ticket.Raise("TCK-0002", "Invoice total mismatch", "Wrong VAT.", fabrikam.Id, billing.Id, TicketPriority.High, now.AddMinutes(5));
        t2.AssignTo(alex.Id, now.AddMinutes(5));
        t2.ChangeStatus(TicketStatus.InProgress, now.AddMinutes(5));

        var t3 = Ticket.Raise("TCK-0003", "Refund request", "Duplicate charge.", fabrikam.Id, billing.Id, TicketPriority.Low, now.AddMinutes(10));
        t3.AssignTo(sara.Id, now.AddMinutes(10));
        t3.ChangeStatus(TicketStatus.Open, now.AddMinutes(10));

        var t4 = Ticket.Raise("TCK-0004", "Production breach", "Keys exposed.", contoso.Id, general.Id, TicketPriority.Critical, now.AddMinutes(15));

        _db.AddRange(t1, t2, t3, t4);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}

