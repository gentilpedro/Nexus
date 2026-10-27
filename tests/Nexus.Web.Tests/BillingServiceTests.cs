using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;
using Nexus.Web.Services.Billing;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers starting a Pro subscription and keeping its seats in step (#50), and the Owner's
/// "Plano e cobrança" page — overview, invoices and Stripe's customer portal (#54) — with Stripe faked.
/// </summary>
public class BillingServiceTests
{
    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://nexus.test/", "https://nexus.test/");
    }

    private sealed class FakeGateway : IStripeGateway
    {
        public List<StripeCheckoutRequest> Checkouts { get; } = [];
        public List<(string SubscriptionId, int Seats)> SeatUpdates { get; } = [];
        public List<(string CustomerId, string ReturnUrl)> Portals { get; } = [];
        public List<StripeInvoice> Invoices { get; } = [];
        public bool Offline { get; set; }

        public Task<string> CreateCheckoutUrlAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
        {
            Checkouts.Add(request);
            return Task.FromResult("https://checkout.stripe.com/c/pay/cs_test");
        }

        public Task UpdateSeatsAsync(string subscriptionId, int seats, CancellationToken cancellationToken = default)
        {
            SeatUpdates.Add((subscriptionId, seats));
            return Task.CompletedTask;
        }

        public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl, CancellationToken cancellationToken = default)
        {
            Portals.Add((customerId, returnUrl));
            return Task.FromResult("https://billing.stripe.com/p/session/test");
        }

        public Task<IReadOnlyList<StripeInvoice>> ListInvoicesAsync(string customerId, int limit, CancellationToken cancellationToken = default) =>
            Offline
                ? throw new HttpRequestException("Stripe fora do ar")
                : Task.FromResult<IReadOnlyList<StripeInvoice>>(Invoices.Take(limit).ToList());
    }

    private static BillingService Service(IDbContextFactory<AppDbContext> factory, FakeGateway gateway, bool configured = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://nexus.aamora.com.br" })
            .Build();
        return new BillingService(factory, gateway,
            Options.Create(configured ? new StripeOptions { SecretKey = "sk_test_x", WebhookSecret = "whsec_x", ProPriceId = "price_pro" } : new StripeOptions()),
            new WorkspacePlanService(factory),
            new PublicUrlBuilder(config, new FakeNavigationManager()),
            NullLogger<BillingService>.Instance);
    }

    private static async Task<(Guid WorkspaceId, string OwnerId, string MemberId)> SeedAsync(IDbContextFactory<AppDbContext> factory, int extraMembers = 1, string? stripeSubscriptionId = null, int seats = 1, string? stripeCustomerId = null)
    {
        var workspaceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid().ToString();
        string memberId = "";
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "W",
            Slug = $"w-{workspaceId:N}",
            Subscription = new WorkspaceSubscription
            {
                WorkspaceId = workspaceId,
                Plan = stripeSubscriptionId is null ? WorkspacePlan.Starter : WorkspacePlan.Pro,
                Status = SubscriptionStatus.Active,
                Seats = seats,
                StripeSubscriptionId = stripeSubscriptionId,
                StripeCustomerId = stripeCustomerId,
            }
        });
        db.Users.Add(new ApplicationUser { Id = ownerId, Email = "dono@example.com", UserName = "dono@example.com", DisplayName = "Dono" });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = ownerId, Role = WorkspaceRole.Owner });
        for (var i = 0; i < extraMembers; i++)
        {
            memberId = Guid.NewGuid().ToString();
            db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = memberId, Role = WorkspaceRole.Member });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (workspaceId, ownerId, memberId);
    }

    [Fact]
    public async Task Owner_StartsCheckout_ForEveryMemberAsASeat()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, ownerId, _) = await SeedAsync(factory, extraMembers: 3);

        var start = await Service(factory, gateway).StartCheckoutAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test", start.Url);
        var request = Assert.Single(gateway.Checkouts);
        Assert.Equal((workspaceId, 4, "dono@example.com"), (request.WorkspaceId, request.Seats, request.CustomerEmail));
        Assert.StartsWith("https://nexus.aamora.com.br/", request.SuccessUrl);
        Assert.Contains($"/workspaces/{workspaceId}/billing", request.SuccessUrl);
        Assert.Contains("assinatura=ok", request.SuccessUrl);
    }

    [Fact]
    public async Task NonOwner_CannotCommitTheWorkspaceToACharge()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, _, memberId) = await SeedAsync(factory);

        var start = await Service(factory, gateway).StartCheckoutAsync(workspaceId, memberId, TestContext.Current.CancellationToken);

        Assert.Null(start.Url);
        Assert.NotNull(start.Error);
        Assert.Empty(gateway.Checkouts);
    }

    [Fact]
    public async Task BillingOff_DoesNotTalkToStripe()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, ownerId, _) = await SeedAsync(factory);
        var service = Service(factory, gateway, configured: false);

        Assert.False(service.IsEnabled);
        Assert.Null((await service.StartCheckoutAsync(workspaceId, ownerId, TestContext.Current.CancellationToken)).Url);
        await service.SyncSeatsAsync(workspaceId, TestContext.Current.CancellationToken);
        Assert.Empty(gateway.Checkouts);
        Assert.Empty(gateway.SeatUpdates);
    }

    [Fact]
    public async Task AlreadyPro_DoesNotStartAnotherSubscription()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, ownerId, _) = await SeedAsync(factory, stripeSubscriptionId: "sub_1", seats: 2);

        var start = await Service(factory, gateway).StartCheckoutAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.Null(start.Url);
        Assert.Empty(gateway.Checkouts);
    }

    [Fact]
    public async Task PayingWorkspace_SeatsFollowTheMemberCount()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, _, _) = await SeedAsync(factory, extraMembers: 11, stripeSubscriptionId: "sub_1", seats: 10);
        var service = Service(factory, gateway);
        var ct = TestContext.Current.CancellationToken;

        await service.SyncSeatsAsync(workspaceId, ct);
        await service.SyncSeatsAsync(workspaceId, ct); // already in step: no second call

        Assert.Equal(("sub_1", 12), Assert.Single(gateway.SeatUpdates));
        await using var db = factory.CreateDbContext();
        Assert.Equal(12, (await db.WorkspaceSubscriptions.SingleAsync(s => s.WorkspaceId == workspaceId, ct)).Seats);
    }

    [Fact]
    public async Task StarterWorkspace_HasNoSeatsToSync()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, _, _) = await SeedAsync(factory, extraMembers: 4);

        await Service(factory, gateway).SyncSeatsAsync(workspaceId, TestContext.Current.CancellationToken);

        Assert.Empty(gateway.SeatUpdates);
    }

    [Fact]
    public async Task Owner_OpensTheStripePortal_ComingBackToTheBillingPage()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, ownerId, _) = await SeedAsync(factory, stripeSubscriptionId: "sub_1", seats: 2, stripeCustomerId: "cus_1");

        var portal = await Service(factory, gateway).OpenPortalAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.Equal("https://billing.stripe.com/p/session/test", portal.Url);
        var (customerId, returnUrl) = Assert.Single(gateway.Portals);
        Assert.Equal("cus_1", customerId);
        Assert.Equal($"https://nexus.aamora.com.br/workspaces/{workspaceId}/billing", returnUrl);
    }

    [Fact]
    public async Task NonOwner_SeesNoBillingAndCannotOpenThePortal()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, _, memberId) = await SeedAsync(factory, stripeSubscriptionId: "sub_1", seats: 2, stripeCustomerId: "cus_1");
        var service = Service(factory, gateway);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await service.GetOverviewAsync(workspaceId, memberId, ct));
        var portal = await service.OpenPortalAsync(workspaceId, memberId, ct);

        Assert.Null(portal.Url);
        Assert.NotNull(portal.Error);
        Assert.Empty(gateway.Portals);
    }

    [Fact]
    public async Task WorkspaceThatNeverPaid_HasNothingToManage()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        var (workspaceId, ownerId, _) = await SeedAsync(factory);
        var service = Service(factory, gateway);
        var ct = TestContext.Current.CancellationToken;

        var overview = await service.GetOverviewAsync(workspaceId, ownerId, ct);
        var portal = await service.OpenPortalAsync(workspaceId, ownerId, ct);

        Assert.NotNull(overview);
        Assert.False(overview.CanManageInStripe);
        Assert.Equal(WorkspacePlan.Starter, overview.Plan.EffectivePlan);
        Assert.Null(portal.Url);
        Assert.Empty(gateway.Portals);
    }

    [Fact]
    public async Task Overview_ListsTheInvoices_AndCountsTheMembers()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway();
        gateway.Invoices.Add(new StripeInvoice("NX-0002", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 14900, "brl", "paid", "https://invoice.stripe.com/i/2"));
        var (workspaceId, ownerId, _) = await SeedAsync(factory, extraMembers: 2, stripeSubscriptionId: "sub_1", seats: 3, stripeCustomerId: "cus_1");

        var overview = await Service(factory, gateway).GetOverviewAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.NotNull(overview);
        Assert.True(overview.CanManageInStripe);
        Assert.Equal(3, overview.Members);
        Assert.Equal(WorkspacePlan.Pro, overview.Plan.EffectivePlan);
        Assert.Equal("NX-0002", Assert.Single(overview.Invoices!).Number);
    }

    // The page must still open when Stripe does not answer: the plan comes from our own table.
    [Fact]
    public async Task StripeDown_TheOverviewStillLoads_WithoutInvoices()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway { Offline = true };
        var (workspaceId, ownerId, _) = await SeedAsync(factory, stripeSubscriptionId: "sub_1", seats: 2, stripeCustomerId: "cus_1");

        var overview = await Service(factory, gateway).GetOverviewAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.NotNull(overview);
        Assert.Null(overview.Invoices);
        Assert.Equal(WorkspacePlan.Pro, overview.Plan.EffectivePlan);
    }

    [Fact]
    public async Task BillingOff_TheOverviewNeverCallsStripe()
    {
        var factory = TestDb.CreateFactory();
        var gateway = new FakeGateway { Offline = true };
        var (workspaceId, ownerId, _) = await SeedAsync(factory, stripeSubscriptionId: "sub_1", seats: 2, stripeCustomerId: "cus_1");

        var overview = await Service(factory, gateway, configured: false).GetOverviewAsync(workspaceId, ownerId, TestContext.Current.CancellationToken);

        Assert.NotNull(overview);
        Assert.False(overview.CanManageInStripe);
        Assert.Empty(overview.Invoices!);
    }

    [Theory]
    [InlineData(1, 149)]
    [InlineData(10, 149)]
    [InlineData(11, 163.90)]
    [InlineData(15, 223.50)]
    public void ProMonthlyAmount_IsTheBasePlusEachPersonOverTen(int seats, double expected) =>
        Assert.Equal((decimal)expected, BillingService.ProMonthlyAmount(seats));
}
