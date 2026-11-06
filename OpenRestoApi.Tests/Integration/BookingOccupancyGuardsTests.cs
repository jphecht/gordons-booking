using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRestoApi.Core.Domain;
using OpenRestoApi.Infrastructure.Persistence;

namespace OpenRestoApi.Tests.Integration;

/// <summary>HTTP regressions against a fresh SQLite file with independent request connections.</summary>
public sealed class BookingOccupancyGuardsTests
{
    private static readonly DateTime Future = DateTime.UtcNow.Date.AddDays(10).AddHours(18);

    [Fact]
    public async Task StaffBooking_RejectsAnOverlappingCombinedTableBooking()
    {
        using var fixture = new Fixture();
        await fixture.SeedBookingAsync(Future, Future.AddMinutes(150), group: true);

        using HttpResponseMessage response = await fixture.CreateAsync(Future.AddMinutes(30));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await fixture.CountBookingsAsync());
    }

    [Fact]
    public async Task StaffBooking_AllowsAnExactlyAdjacentCombinedTableBooking()
    {
        using var fixture = new Fixture();
        await fixture.SeedBookingAsync(Future, Future.AddMinutes(150), group: true);

        using HttpResponseMessage response = await fixture.CreateAsync(Future.AddMinutes(150));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task StaffBooking_AllowsSeparateBookingsOnDifferentMembersOfAGroup()
    {
        using var fixture = new Fixture();
        await fixture.SeedBookingAsync(Future, Future.AddMinutes(150));

        using HttpResponseMessage response = await fixture.CreateAsync(Future, tableIndex: 1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extension_RejectsAFollowingBookingAndKeepsTheOriginalEnd(bool group)
    {
        using var fixture = new Fixture();
        int id = await fixture.SeedBookingAsync(Future, Future.AddMinutes(150), group: group);
        await fixture.SeedBookingAsync(Future.AddMinutes(150), Future.AddMinutes(300));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/extend", new { minutes = 30 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Future.AddMinutes(150), (await fixture.ReadBookingAsync(id)).EndTime);
    }

    [Fact]
    public async Task Extension_AllowsAnExactlyAdjacentFollowingBooking()
    {
        using var fixture = new Fixture();
        int id = await fixture.SeedBookingAsync(Future, Future.AddMinutes(120));
        await fixture.SeedBookingAsync(Future.AddMinutes(150), Future.AddMinutes(300));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/extend", new { minutes = 30 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Future.AddMinutes(150), (await fixture.ReadBookingAsync(id)).EndTime);
    }

    [Fact]
    public async Task Extension_DoesNotRoundAwayASubMinuteOverlap()
    {
        using var fixture = new Fixture();
        int id = await fixture.SeedBookingAsync(Future.AddSeconds(30), Future.AddMinutes(120).AddSeconds(30));
        await fixture.SeedBookingAsync(Future.AddMinutes(150).AddSeconds(15), Future.AddMinutes(300));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/extend", new { minutes = 30 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task BulkExtension_RejectsTheWholeChangeWhenOneTableWouldOverlap()
    {
        using var fixture = new Fixture();
        DateTime start = DateTime.UtcNow.AddMinutes(-20);
        DateTime end = start.AddMinutes(150);
        int first = await fixture.SeedBookingAsync(start, end);
        int second = await fixture.SeedBookingAsync(start, end, tableIndex: 2);
        await fixture.SeedBookingAsync(end, end.AddMinutes(150));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/restaurants/{fixture.RestaurantId}/extend", new { minutes = 30 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(end, (await fixture.ReadBookingAsync(first)).EndTime);
        Assert.Equal(end, (await fixture.ReadBookingAsync(second)).EndTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RejectsAReplacementBookingAndRemainsCancelled(bool group)
    {
        using var fixture = new Fixture();
        int id = await fixture.SeedBookingAsync(Future, Future.AddMinutes(150), group: group, cancelled: true);
        await fixture.SeedBookingAsync(Future, Future.AddMinutes(150));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/restore", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True((await fixture.ReadBookingAsync(id)).IsCancelled);
    }

    [Fact]
    public async Task StatusUndo_RejectsAReplacementBookingAndPreservesTheFinishedStatus()
    {
        using var fixture = new Fixture();
        DateTime start = DateTime.UtcNow.AddMinutes(-20);
        DateTime originalEnd = start.AddMinutes(150);
        int id = await fixture.SeedBookingAsync(start, originalEnd, group: true, status: BookingStatus.Seated);
        using HttpResponseMessage finished = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/status", new { status = "Finished" });
        finished.EnsureSuccessStatusCode();
        await fixture.SeedBookingAsync(DateTime.UtcNow.AddMinutes(1), originalEnd);

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync($"/api/admin/bookings/{id}/status", new { status = "Seated" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Booking persisted = await fixture.ReadBookingAsync(id);
        Assert.Equal(BookingStatus.Finished, persisted.Status);
        Assert.Equal(originalEnd, persisted.OriginalEndTime);
        Assert.True(persisted.EndTime < originalEnd);
    }

    [Fact]
    public async Task StaffBooking_UsesTheExistingRestaurantsDurationForALegacyBooking()
    {
        using var fixture = new Fixture();
        await fixture.SeedBookingAsync(Future, null);

        using HttpResponseMessage response = await fixture.CreateAsync(Future.AddMinutes(90));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StaffBooking_DoesNotShortenLegacyOccupancyToTheNewPartyTurnTime()
    {
        using var fixture = new Fixture(shorterNewStay: true);
        await fixture.SeedBookingAsync(Future, null);

        using HttpResponseMessage response = await fixture.CreateAsync(Future.AddMinutes(90));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_DoesNotRoundAwayASubMinuteOverlap()
    {
        using var fixture = new Fixture();
        int id = await fixture.SeedBookingAsync(Future, Future.AddMinutes(150).AddSeconds(30));
        await fixture.SeedBookingAsync(Future.AddMinutes(180).AddSeconds(15), Future.AddMinutes(330));

        using HttpResponseMessage response = await fixture.Client.PutAsJsonAsync($"/api/admin/bookings/{id}", new { date = Future.AddMinutes(30) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Future, (await fixture.ReadBookingAsync(id)).Date);
    }

    [Fact]
    public async Task StaffBooking_IgnoresAnEmptyFinishedInterval()
    {
        using var fixture = new Fixture();
        await fixture.SeedBookingAsync(Future.AddMinutes(30), Future.AddMinutes(30), status: BookingStatus.Finished);

        using HttpResponseMessage response = await fixture.CreateAsync(Future);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FileDatabaseFactory _factory = new();
        private readonly int _sectionId;
        private readonly int[] _tableIds;
        private readonly int _groupId;
        public HttpClient Client { get; }
        public int RestaurantId { get; }

        public Fixture(bool shorterNewStay = false)
        {
            Client = _factory.CreateAuthenticatedClient();
            using IServiceScope scope = _factory.Services.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restaurant = new Restaurant { Name = "Synthetic occupancy tests", Timezone = "UTC", DefaultBookingDurationMinutes = 150 };
            if (shorterNewStay) restaurant.TurnTimesJson = TurnTimes.Serialize([new TurnTime(2, 60)]);
            var section = new Section { Name = "Test area", Restaurant = restaurant };
            var tables = new[] { new Table { Name = "A", Seats = 4, Section = section }, new Table { Name = "B", Seats = 4, Section = section }, new Table { Name = "C", Seats = 4, Section = section } };
            db.Tables.AddRange(tables);
            db.SaveChanges();
            RestaurantId = restaurant.Id;
            _sectionId = section.Id;
            _tableIds = tables.Select(t => t.Id).ToArray();
            var group = new TableGroup { Name = "A+B", RestaurantId = RestaurantId, CombinedSeats = 8,
                Members = [new TableGroupMembership { TableId = _tableIds[0] }, new TableGroupMembership { TableId = _tableIds[1] }] };
            db.TableGroups.Add(group);
            db.SaveChanges();
            _groupId = group.Id;
        }

        public Task<HttpResponseMessage> CreateAsync(DateTime date, int tableIndex = 0) => Client.PostAsJsonAsync("/api/admin/bookings", new
        {
            restaurantId = RestaurantId, sectionId = _sectionId, tableId = _tableIds[tableIndex], date,
            customerName = "Synthetic guest", customerEmail = "guest@example.test", seats = 2,
        });

        public async Task<int> SeedBookingAsync(DateTime start, DateTime? end, bool group = false, bool cancelled = false, int tableIndex = 0, BookingStatus status = BookingStatus.Booked)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = new Booking { RestaurantId = RestaurantId, SectionId = _sectionId,
                TableId = group ? null : _tableIds[tableIndex], TableGroupId = group ? _groupId : null,
                Date = start, EndTime = end, Seats = 2, CustomerEmail = "guest@example.test",
                BookingRef = Guid.NewGuid().ToString("N"), IsCancelled = cancelled, Status = status };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        }

        public async Task<Booking> ReadBookingAsync(int id)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
        }

        public async Task<int> CountBookingsAsync()
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.CountAsync(b => b.RestaurantId == RestaurantId);
        }

        public void Dispose() { Client.Dispose(); _factory.Dispose(); }
    }

    private sealed class FileDatabaseFactory : TestWebAppFactory
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gordons-occupancy-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            Directory.CreateDirectory(_directory);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options
                    .UseSqlite($"Data Source={Path.Combine(_directory, "bookings.db")};Pooling=False")
                    .AddInterceptors(new SqlitePragmaInterceptor()));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
