using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Models;
using SmartAssistApi.Services;
using Stripe;

namespace SmartAssistApi.Tests;

public class StripeServiceTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"] = "https://fake.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
                ["Stripe:SecretKey"] = "sk_test_dummy",
                ["Stripe:PremiumPriceId"] = "price_test_premium",
                ["Stripe:ProPriceId"] = "price_test_pro",
                ["Stripe:WebhookSecret"] = "whsec_dummy",
                ["Frontend:SuccessUrl"] = "https://frontend.test/profile?upgraded=true",
                ["Frontend:CancelUrl"] = "https://frontend.test/pricing?cancelled=true",
                ["Frontend:PortalReturnUrl"] = "https://frontend.test/profile",
            })
            .Build();

    [Fact]
    public async Task CreateCheckoutSessionAsync_ValidInput_WritesExpectedMetadata()
    {
        var config = BuildConfig();
        var usageMock = new Mock<UsageService>(config, new HttpClient());
        var apiMock = new Mock<IStripeApiClient>();
        var loggerMock = new Mock<ILogger<StripeService>>();

        Stripe.Checkout.SessionCreateOptions? captured = null;
        apiMock
            .Setup(x => x.CreateCheckoutSessionAsync(It.IsAny<Stripe.Checkout.SessionCreateOptions>()))
            .Callback<Stripe.Checkout.SessionCreateOptions>(options => captured = options)
            .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_test_1", Url = "https://checkout.test/session" });

        var service = new StripeService(config, usageMock.Object, apiMock.Object, loggerMock.Object);

        var url = await service.CreateCheckoutSessionAsync("user_abc", "user@example.com", "premium", "corr-1");

        Assert.Equal("https://checkout.test/session", url);
        Assert.NotNull(captured);
        Assert.Equal("premium", captured!.Metadata["plan"]);
        Assert.Equal("user_abc", captured.Metadata["userId"]);
        Assert.Equal("user@example.com", captured.Metadata["email"]);
        Assert.Equal("https://frontend.test/profile?upgraded=true", captured.SuccessUrl);
        Assert.Equal("https://frontend.test/pricing?cancelled=true", captured.CancelUrl);
    }

    [Fact]
    public async Task HandleStripeEventAsync_CheckoutCompleted_UpgradesPlanAndRecordsAudit()
    {
        var config = BuildConfig();
        var usageMock = new Mock<UsageService>(config, new HttpClient());
        var apiMock = new Mock<IStripeApiClient>();
        var loggerMock = new Mock<ILogger<StripeService>>();

        usageMock
            .Setup(x => x.TryAcquireStripeEventAsync("evt_1"))
            .ReturnsAsync(true);

        // New: GetPlanAsync is called before SetPlanAsync to enforce upgrade-only rule
        usageMock
            .Setup(x => x.GetPlanAsync("user_abc"))
            .ReturnsAsync("free");

        StripeWebhookAuditRecord? audit = null;
        usageMock
            .Setup(x => x.RecordStripeWebhookAuditAsync(It.IsAny<StripeWebhookAuditRecord>()))
            .Callback<StripeWebhookAuditRecord>(a => audit = a)
            .Returns(Task.CompletedTask);

        var service = new StripeService(config, usageMock.Object, apiMock.Object, loggerMock.Object);

        var stripeEvent = new Event
        {
            Id = "evt_1",
            Type = EventTypes.CheckoutSessionCompleted,
            Data = new EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    Id = "cs_test_123",
                    CustomerId = "cus_123",
                    SubscriptionId = "sub_123",
                    Metadata = new Dictionary<string, string>
                    {
                        ["userId"] = "user_abc",
                        ["plan"] = "premium",
                    }
                }
            }
        };

        await service.HandleStripeEventAsync(stripeEvent);

        usageMock.Verify(x => x.SetPlanAsync("user_abc", "premium"), Times.Once);
        usageMock.Verify(x => x.SetStripeCustomerIdAsync("user_abc", "cus_123"), Times.Once);
        usageMock.Verify(x => x.RecordStripeWebhookAuditAsync(It.IsAny<StripeWebhookAuditRecord>()), Times.Once);
        Assert.NotNull(audit);
        Assert.Equal("evt_1", audit!.EventId);
        Assert.Equal("cs_test_123", audit.SessionId);
        Assert.Equal("premium", audit.Plan);
        Assert.Equal("upgraded", audit.Result);
    }

    [Fact]
    public async Task HandleStripeEventAsync_DuplicateEvent_DoesNotProcessTwice()
    {
        var config = BuildConfig();
        var usageMock = new Mock<UsageService>(config, new HttpClient());
        var apiMock = new Mock<IStripeApiClient>();
        var loggerMock = new Mock<ILogger<StripeService>>();

        usageMock
            .Setup(x => x.TryAcquireStripeEventAsync("evt_dupe"))
            .ReturnsAsync(false);

        var service = new StripeService(config, usageMock.Object, apiMock.Object, loggerMock.Object);

        var stripeEvent = new Event
        {
            Id = "evt_dupe",
            Type = EventTypes.CheckoutSessionCompleted,
            Data = new EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    Id = "cs_test_dupe",
                    Metadata = new Dictionary<string, string>
                    {
                        ["userId"] = "user_abc",
                        ["plan"] = "premium",
                    }
                }
            }
        };

        await service.HandleStripeEventAsync(stripeEvent);

        usageMock.Verify(x => x.SetPlanAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        usageMock.Verify(x => x.RecordStripeWebhookAuditAsync(It.IsAny<StripeWebhookAuditRecord>()), Times.Never);
    }

    [Fact]
    public async Task WebhookThenUsageFlow_ReturnsUpgradedPlan()
    {
        var config = BuildConfig();
        var usage = new InMemoryUsageService(config);
        var apiMock = new Mock<IStripeApiClient>();
        var stripeLoggerMock = new Mock<ILogger<StripeService>>();

        var stripeService = new StripeService(config, usage, apiMock.Object, stripeLoggerMock.Object);
        var stripeEvent = new Event
        {
            Id = "evt_flow_1",
            Type = EventTypes.CheckoutSessionCompleted,
            Data = new EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    Id = "cs_flow_1",
                    CustomerId = "cus_flow_1",
                    Metadata = new Dictionary<string, string>
                    {
                        ["userId"] = "user_flow",
                        ["plan"] = "premium",
                    }
                }
            }
        };

        await stripeService.HandleStripeEventAsync(stripeEvent);

        var agentServiceMock = new Mock<IAgentService>();
        var clerkMock = new Mock<ClerkAuthService>();
        var agentLoggerMock = new Mock<ILogger<AgentController>>();
        clerkMock.Setup(x => x.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("user_flow", false));

        var speechMock = new Mock<ISpeechService>();
        var controller = new AgentController(
            agentServiceMock.Object,
            new ConversationService(),
            usage,
            clerkMock.Object,
            speechMock.Object,
            agentLoggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            }
        };

        var result = await controller.GetUsage();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"plan\":\"premium\"", json);
        Assert.Contains("\"dailyLimit\":200", json);
    }

    private sealed class InMemoryUsageService : UsageService
    {
        private readonly Dictionary<string, string> _plans = new();
        private readonly Dictionary<string, int> _usage = new();
        private readonly HashSet<string> _processedEvents = [];
        private readonly Dictionary<string, StripeDebugInfo> _debug = new();

        public InMemoryUsageService(IConfiguration config)
            : base(config, new HttpClient())
        {
        }

        public override Task<string> GetPlanAsync(string userId)
        {
            return Task.FromResult(_plans.TryGetValue(userId, out var plan) ? plan : "free");
        }

        public override Task<string> GetPlanStrictAsync(string userId)
        {
            return GetPlanAsync(userId);
        }

        public override Task SetPlanAsync(string userId, string plan)
        {
            _plans[userId] = plan;
            return Task.CompletedTask;
        }

        public override Task<int> GetUsageTodayAsync(string userId)
        {
            return Task.FromResult(_usage.TryGetValue(userId, out var value) ? value : 0);
        }

        public override Task<int> GetUsageTodayStrictAsync(string userId)
        {
            return GetUsageTodayAsync(userId);
        }

        public override Task SetStripeCustomerIdAsync(string userId, string customerId)
        {
            _ = customerId;
            return Task.CompletedTask;
        }

        public override Task<string?> GetUserIdByStripeCustomerIdAsync(string customerId)
        {
            _ = customerId;
            return Task.FromResult<string?>(null);
        }

        public override Task<bool> TryAcquireStripeEventAsync(string eventId)
        {
            return Task.FromResult(_processedEvents.Add(eventId));
        }

        public override Task RecordStripeWebhookAuditAsync(StripeWebhookAuditRecord audit)
        {
            if (!string.IsNullOrWhiteSpace(audit.UserId))
            {
                _debug[audit.UserId] = new StripeDebugInfo(
                    audit.UserId,
                    _plans.TryGetValue(audit.UserId, out var plan) ? plan : "free",
                    audit.EventId,
                    audit.ProcessedAt,
                    audit.SessionId);
            }

            return Task.CompletedTask;
        }

        public override Task<StripeDebugInfo> GetStripeDebugInfoAsync(string userId)
        {
            if (_debug.TryGetValue(userId, out var info))
                return Task.FromResult(info);

            return Task.FromResult(new StripeDebugInfo(userId, _plans.GetValueOrDefault(userId, "free"), null, null, null));
        }
    }
}
