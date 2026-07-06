using Microsoft.AspNetCore.Mvc;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;
using PayosServer;

var builder = WebApplication.CreateBuilder(args);

var payosCfg = builder.Configuration.GetSection("PayOS");
string clientId = payosCfg["ClientId"] ?? "";
string apiKey = payosCfg["ApiKey"] ?? "";
string checksumKey = payosCfg["ChecksumKey"] ?? "";

// Public base URL of this server, for example your ngrok HTTPS URL.
string appBaseUrl = (builder.Configuration["AppBaseUrl"] ?? "http://localhost:5000").TrimEnd('/');
string databaseUrl = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Missing database connection string. Set DATABASE_URL or ConnectionStrings:DefaultConnection.");

var packages = builder.Configuration.GetSection("CoinPackages").Get<List<CoinPackage>>()
    ?? new List<CoinPackage>();

var payOS = new PayOSClient(new PayOSOptions
{
    ClientId = clientId,
    ApiKey = apiKey,
    ChecksumKey = checksumKey,
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var store = new Store(databaseUrl);
await store.InitializeAsync();

builder.Services.AddSingleton(payOS);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(packages);

var app = builder.Build();
app.UseCors();

// PayOS orderCode must be unique. Keep it above persisted orders across restarts.
long orderCounter = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), await store.GetMaxOrderCodeAsync());
const int CoinStep = 100;
const int MinTopUpCoins = 100;
const int MaxTopUpCoins = 100000;

app.MapGet("/", () => "PayosServer is running.");

app.MapGet("/api/packages", (List<CoinPackage> pkgs) => Results.Ok(pkgs));

app.MapGet("/api/users/{userId}/balance", async (string userId, Store s) =>
    Results.Ok(new { userId, coins = await s.GetBalanceAsync(userId) }));

app.MapPost("/api/orders/create",
    async (CreateOrderRequest req, PayOSClient pay, Store s, List<CoinPackage> pkgs) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "Missing userId" });

    var pkg = ResolveCoinPackage(req, pkgs);
    if (pkg is null)
    {
        return Results.BadRequest(new
        {
            error = $"coins must be a multiple of {CoinStep}, from {MinTopUpCoins} to {MaxTopUpCoins}"
        });
    }

    long orderCode = Interlocked.Increment(ref orderCounter);

    var order = new Order
    {
        OrderCode = orderCode,
        UserId = req.UserId,
        PackageId = pkg.Id,
        Coins = pkg.Coins,
        AmountVnd = pkg.PriceVnd,
        Status = "PENDING",
    };
    await s.SaveOrderAsync(order);

    var paymentRequest = new CreatePaymentLinkRequest
    {
        OrderCode = orderCode,
        Amount = pkg.PriceVnd,
        Description = "Nap coin",
        CancelUrl = $"{appBaseUrl}/pay/cancel?orderCode={orderCode}",
        ReturnUrl = $"{appBaseUrl}/pay/success?orderCode={orderCode}",
    };

    try
    {
        var result = await pay.PaymentRequests.CreateAsync(paymentRequest);
        return Results.Ok(new
        {
            orderCode,
            checkoutUrl = result.CheckoutUrl,
            qrCode = result.QrCode,
            paymentLinkId = result.PaymentLinkId,
            amountVnd = pkg.PriceVnd,
            coins = pkg.Coins,
        });
    }
    catch (Exception ex)
    {
        order.Status = "CREATE_FAILED";
        await s.SaveOrderAsync(order);
        return Results.Problem($"PayOS createPaymentLink failed: {ex.Message}");
    }
});

app.MapGet("/api/orders/{orderCode:long}/status", async (long orderCode, Store s) =>
{
    var order = await s.GetOrderAsync(orderCode);
    return order is null
        ? Results.NotFound(new { error = "Unknown orderCode" })
        : Results.Ok(new
        {
            order.OrderCode,
            order.Status,
            order.Coins,
            order.UserId,
            order.AmountVnd,
            order.Credited,
        });
});

app.MapGet("/api/orders", async (Store s) =>
{
    var orders = await s.GetOrdersAsync();
    return Results.Ok(orders);
});

app.MapGet("/api/orders/summary", async (Store s) =>
{
    var summary = await s.GetOrdersSummaryAsync();
    return Results.Ok(summary);
});

// PayOS calls this endpoint server-to-server. Verify signature before crediting.
app.MapPost("/payos-webhook", async (Webhook body, PayOSClient pay, Store s) =>
{
    WebhookData data;
    try
    {
        data = await pay.Webhooks.VerifyAsync(body);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[webhook] ignored invalid payload: {ex.Message}");
        return Results.Ok(new { received = true });
    }

    if (data.Code != "00")
    {
        Console.WriteLine($"[webhook] order {data.OrderCode} ignored, code={data.Code}");
        return Results.Ok(new { received = true });
    }

    CreditResult creditResult = await s.TryMarkPaidAndCreditAsync(
        data.OrderCode,
        data.Amount,
        data.Currency);

    Console.WriteLine(
        $"[webhook] order={data.OrderCode}, user={creditResult.Order?.UserId ?? "unknown"}, " +
        $"amount={data.Amount} {data.Currency}, credited={creditResult.Credited}, reason={creditResult.Reason}");

    return Results.Ok(new { received = true });
});

app.MapGet("/pay/success", (long orderCode) =>
    Results.Content(
        $"<h2>Thanh toan thanh cong!</h2><p>Ma don: {orderCode}. Ban co the quay lai game.</p>",
        "text/html"));

app.MapGet("/pay/cancel", async (long orderCode, Store s) =>
{
    await s.MarkCancelledAsync(orderCode);
    return Results.Content(
        $"<h2>Da huy thanh toan.</h2><p>Ma don: {orderCode}.</p>",
        "text/html");
});

// Register a public webhook URL with PayOS once your HTTPS tunnel/domain is up.
app.MapPost("/api/confirm-webhook", async ([FromQuery] string url, PayOSClient pay) =>
{
    try
    {
        return Results.Ok(new { result = await pay.Webhooks.ConfirmAsync(url) });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

static CoinPackage? ResolveCoinPackage(CreateOrderRequest req, List<CoinPackage> packages)
{
    int unitPriceVnd = GetCoinStepPriceVnd(packages);

    if (req.Coins.HasValue)
    {
        int coins = req.Coins.Value;
        if (!IsValidTopUpCoins(coins))
            return null;

        return new CoinPackage
        {
            Id = $"coin_{coins}",
            Name = $"{coins} coin",
            Coins = coins,
            PriceVnd = coins / CoinStep * unitPriceVnd,
        };
    }

    if (string.IsNullOrWhiteSpace(req.PackageId))
        return null;

    var configured = packages.FirstOrDefault(p => string.Equals(p.Id, req.PackageId, StringComparison.OrdinalIgnoreCase));
    if (configured is null || !IsValidTopUpCoins(configured.Coins))
        return null;

    return configured;
}

static bool IsValidTopUpCoins(int coins)
{
    return coins >= MinTopUpCoins && coins <= MaxTopUpCoins && coins % CoinStep == 0;
}

static int GetCoinStepPriceVnd(List<CoinPackage> packages)
{
    var basePackage = packages.FirstOrDefault(p => p.Coins == CoinStep && p.PriceVnd > 0);
    if (basePackage is not null)
        return basePackage.PriceVnd;

    var package = packages
        .Where(p => p.Coins > 0 && p.PriceVnd > 0 && p.Coins % CoinStep == 0)
        .OrderBy(p => p.Coins)
        .FirstOrDefault();

    return package is null ? 10000 : package.PriceVnd / (package.Coins / CoinStep);
}
