namespace PayosServer;

/// <summary>A coin package the player can buy (configured in appsettings.json).</summary>
public class CoinPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Coins { get; set; }
    public int PriceVnd { get; set; }
}

/// <summary>Request body for POST /api/orders/create.</summary>
public record CreateOrderRequest(string UserId, string PackageId);

/// <summary>A pending/paid order tracked server-side.</summary>
public class Order
{
    public long OrderCode { get; set; }
    public string UserId { get; set; } = "";
    public string PackageId { get; set; } = "";
    public int Coins { get; set; }
    public int AmountVnd { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING | PAID | CANCELLED
    public bool Credited { get; set; }              // guards against double-crediting
}
