using System.Collections.Concurrent;
using System.Text.Json;

namespace PayosServer;

/// <summary>
/// Small file-backed store for orders and user coin balances.
/// This keeps local/dev payments across server restarts. For production,
/// replace this with a transactional database (EF Core + SQL Server/Postgres).
/// </summary>
public class Store
{
    private readonly ConcurrentDictionary<long, Order> _orders = new();
    private readonly ConcurrentDictionary<string, int> _balances = new();
    private readonly object _creditLock = new();
    private readonly string _dataFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public Store(string dataFilePath)
    {
        _dataFilePath = dataFilePath;
        Load();
    }

    public long MaxOrderCode => _orders.Keys.DefaultIfEmpty(0).Max();

    public void SaveOrder(Order order)
    {
        _orders[order.OrderCode] = order;
        Persist();
    }

    public Order? GetOrder(long orderCode) =>
        _orders.TryGetValue(orderCode, out var order) ? order : null;

    public int GetBalance(string userId) =>
        _balances.TryGetValue(userId, out var coins) ? coins : 0;

    /// <summary>
    /// Mark an order PAID and credit its coins exactly once.
    /// Returns true if coins were credited by this call. False is expected for
    /// webhook retries, mismatched payment data, and unknown orders.
    /// </summary>
    public bool TryMarkPaidAndCredit(
        long orderCode,
        long paidAmountVnd,
        string? currency,
        out Order? order,
        out string reason)
    {
        lock (_creditLock)
        {
            if (!_orders.TryGetValue(orderCode, out order))
            {
                reason = "unknown_order";
                return false;
            }

            if (order.Credited)
            {
                reason = "already_credited";
                return false;
            }

            if (!string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"currency_mismatch:{currency}";
                return false;
            }

            if (paidAmountVnd != order.AmountVnd)
            {
                reason = $"amount_mismatch:paid={paidAmountVnd},expected={order.AmountVnd}";
                return false;
            }

            int coinsToCredit = order.Coins;
            order.Status = "PAID";
            order.Credited = true;
            _balances.AddOrUpdate(order.UserId, coinsToCredit, (_, current) => current + coinsToCredit);
            Persist();
            reason = "credited";
            return true;
        }
    }

    public void MarkCancelled(long orderCode)
    {
        if (_orders.TryGetValue(orderCode, out var order) && !order.Credited)
        {
            order.Status = "CANCELLED";
            Persist();
        }
    }

    private void Load()
    {
        if (!File.Exists(_dataFilePath)) return;

        string json = File.ReadAllText(_dataFilePath);
        if (string.IsNullOrWhiteSpace(json)) return;

        var snapshot = JsonSerializer.Deserialize<StoreSnapshot>(json, JsonOptions);
        if (snapshot is null) return;

        foreach (var order in snapshot.Orders)
            _orders[order.OrderCode] = order;

        foreach (var balance in snapshot.Balances)
            _balances[balance.Key] = balance.Value;
    }

    private void Persist()
    {
        string? dir = Path.GetDirectoryName(_dataFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var snapshot = new StoreSnapshot
        {
            Orders = _orders.Values.OrderBy(order => order.OrderCode).ToList(),
            Balances = _balances.OrderBy(balance => balance.Key)
                .ToDictionary(balance => balance.Key, balance => balance.Value),
        };

        string tmpPath = _dataFilePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(snapshot, JsonOptions));

        if (File.Exists(_dataFilePath))
            File.Replace(tmpPath, _dataFilePath, null);
        else
            File.Move(tmpPath, _dataFilePath);
    }

    private sealed class StoreSnapshot
    {
        public List<Order> Orders { get; set; } = new();
        public Dictionary<string, int> Balances { get; set; } = new();
    }
}
