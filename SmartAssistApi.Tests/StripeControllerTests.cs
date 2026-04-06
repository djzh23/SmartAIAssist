using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class StripeControllerTests
{
    private readonly Mock<ClerkAuthService> _clerkAuthMock = new();
    private readonly Mock<UsageService> _usageServiceMock;
    private readonly Mock<ILogger<StripeController>> _controllerLoggerMock = new();
    private readonly StripeService _stripeService;
    private readonly IConfiguration _config;

    public StripeControllerTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"] = "https://fake.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
                ["Stripe:SecretKey"] = "sk_test_dummy",
                ["Stripe:PremiumPriceId"] = "price_test_premium",
                ["Stripe:ProPriceId"] = "price_test_pro",
                ["Frontend:BaseUrl"] = "http://localhost:5173",
                ["Stripe:WebhookSecret"] = "whsec_dummy",
                ["Stripe:EnableDebugEndpoint"] = "false",
            })
            .Build();

        _usageServiceMock = new Mock<UsageService>(_config, new HttpClient());

        var stripeApiMock = new Mock<IStripeApiClient>();
        var stripeLoggerMock = new Mock<ILogger<StripeService>>();
        _stripeService = new StripeService(_config, _usageServiceMock.Object, stripeApiMock.Object, stripeLoggerMock.Object);
    }

    private StripeController CreateController()
    {
        var controller = new StripeController(
            _stripeService,
            _usageServiceMock.Object,
            _clerkAuthMock.Object,
            _config,
            _controllerLoggerMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return controller;
    }

    [Fact]
    public async Task CreateCheckout_WithoutAuth_Returns401()
    {
        _clerkAuthMock
            .Setup(x => x.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("ip:127.0.0.1", true));
        var controller = CreateController();

        var result = await controller.CreateCheckout(new CheckoutRequest("premium", "test@example.com", null));

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task CreateCheckout_InvalidPlan_Returns400()
    {
        _clerkAuthMock
            .Setup(x => x.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_123", false));
        var controller = CreateController();

        var result = await controller.CreateCheckout(new CheckoutRequest("enterprise", "test@example.com", null));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task CreateCheckout_UserIdMismatch_Returns403()
    {
        _clerkAuthMock
            .Setup(x => x.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_123", false));
        var controller = CreateController();

        var result = await controller.CreateCheckout(new CheckoutRequest("premium", "test@example.com", "user_other"));

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task Webhook_WithoutSignature_Returns400()
    {
        var controller = CreateController();
        var payload = "{}";
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        controller.ControllerContext.HttpContext.Request.ContentLength = payload.Length;

        var result = await controller.Webhook();

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }
}
