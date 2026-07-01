using Npgsql;

namespace PayosServer;

/// <summary>PostgreSQL-backed store for orders and user coin balances.</summary>
public class Store
{
    private readonly NpgsqlDataSource _dataSource;

    public Store(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(NormalizeConnectionString(connectionString));
    }

    public async Task InitializeAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS orders (
                order_code BIGINT PRIMARY KEY,
                user_id TEXT NOT NULL,
                package_id TEXT NOT NULL,
                coins INTEGER NOT NULL CHECK (coins >= 0),
                amount_vnd INTEGER NOT NULL CHECK (amount_vnd >= 0),
                status TEXT NOT NULL,
                credited BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS balances (
                user_id TEXT PRIMARY KEY,
                coins INTEGER NOT NULL DEFAULT 0 CHECK (coins >= 0),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS idx_orders_user_id ON orders (user_id);
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> GetMaxOrderCodeAsync()
    {
        const string sql = "SELECT COALESCE(MAX(order_code), 0) FROM orders;";
        await using var command = _dataSource.CreateCommand(sql);
        object? result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    public async Task SaveOrderAsync(Order order)
    {
        const string sql = """
            INSERT INTO orders (
                order_code,
                user_id,
                package_id,
                coins,
                amount_vnd,
                status,
                credited
            )
            VALUES (
                @order_code,
                @user_id,
                @package_id,
                @coins,
                @amount_vnd,
                @status,
                @credited
            )
            ON CONFLICT (order_code) DO UPDATE SET
                user_id = EXCLUDED.user_id,
                package_id = EXCLUDED.package_id,
                coins = EXCLUDED.coins,
                amount_vnd = EXCLUDED.amount_vnd,
                status = EXCLUDED.status,
                credited = EXCLUDED.credited,
                updated_at = now();
            """;

        await using var command = _dataSource.CreateCommand(sql);
        AddOrderParameters(command, order);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Order?> GetOrderAsync(long orderCode)
    {
        const string sql = """
            SELECT order_code, user_id, package_id, coins, amount_vnd, status, credited
            FROM orders
            WHERE order_code = @order_code;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("order_code", orderCode);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadOrder(reader) : null;
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync()
    {
        const string sql = """
            SELECT order_code, user_id, package_id, coins, amount_vnd, status, credited, created_at, updated_at
            FROM orders
            ORDER BY created_at DESC, order_code DESC;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();

        var orders = new List<OrderView>();
        while (await reader.ReadAsync())
        {
            orders.Add(ReadOrderView(reader));
        }
        return orders;
    }

    public async Task<OrdersSummary> GetOrdersSummaryAsync()
    {
        const string sql = """
            SELECT
                COUNT(*) AS total_orders,
                COALESCE(SUM(amount_vnd), 0) AS total_amount_vnd,
                COALESCE(SUM(amount_vnd) FILTER (WHERE status = 'PAID'), 0) AS total_paid_amount_vnd,
                COALESCE(SUM(coins), 0) AS total_coins,
                COUNT(*) FILTER (WHERE status = 'PENDING') AS pending_orders,
                COUNT(*) FILTER (WHERE status = 'PAID') AS paid_orders,
                COUNT(*) FILTER (WHERE status = 'CANCELLED') AS cancelled_orders,
                COUNT(*) FILTER (WHERE status = 'CREATE_FAILED') AS failed_orders
            FROM orders;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new OrdersSummary(0, 0, 0, 0, 0, 0, 0, 0);

        return new OrdersSummary(
            (int)(reader.IsDBNull(0) ? 0L : reader.GetInt64(0)),
            reader.IsDBNull(1) ? 0L : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0L : reader.GetInt64(2),
            (int)(reader.IsDBNull(3) ? 0L : reader.GetInt64(3)),
            (int)(reader.IsDBNull(4) ? 0L : reader.GetInt64(4)),
            (int)(reader.IsDBNull(5) ? 0L : reader.GetInt64(5)),
            (int)(reader.IsDBNull(6) ? 0L : reader.GetInt64(6)),
            (int)(reader.IsDBNull(7) ? 0L : reader.GetInt64(7)));
    }

    public async Task<int> GetBalanceAsync(string userId)
    {
        const string sql = "SELECT coins FROM balances WHERE user_id = @user_id;";
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);

        object? result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Mark an order PAID and credit its coins exactly once.
    /// Returns false for webhook retries, mismatched payment data, and unknown orders.
    /// </summary>
    public async Task<CreditResult> TryMarkPaidAndCreditAsync(
        long orderCode,
        long paidAmountVnd,
        string? currency)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string selectSql = """
            SELECT order_code, user_id, package_id, coins, amount_vnd, status, credited
            FROM orders
            WHERE order_code = @order_code
            FOR UPDATE;
            """;

        await using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
        selectCommand.Parameters.AddWithValue("order_code", orderCode);

        await using var reader = await selectCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await reader.CloseAsync();
            await transaction.CommitAsync();
            return new CreditResult(false, null, "unknown_order");
        }

        Order order = ReadOrder(reader);
        await reader.CloseAsync();

        if (order.Credited)
        {
            await transaction.CommitAsync();
            return new CreditResult(false, order, "already_credited");
        }

        if (!string.Equals(currency, "VND", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync();
            return new CreditResult(false, order, $"currency_mismatch:{currency}");
        }

        if (paidAmountVnd != order.AmountVnd)
        {
            await transaction.CommitAsync();
            return new CreditResult(false, order, $"amount_mismatch:paid={paidAmountVnd},expected={order.AmountVnd}");
        }

        const string updateOrderSql = """
            UPDATE orders
            SET status = 'PAID',
                credited = TRUE,
                updated_at = now()
            WHERE order_code = @order_code;
            """;

        await using (var updateOrderCommand = new NpgsqlCommand(updateOrderSql, connection, transaction))
        {
            updateOrderCommand.Parameters.AddWithValue("order_code", orderCode);
            await updateOrderCommand.ExecuteNonQueryAsync();
        }

        const string updateBalanceSql = """
            INSERT INTO balances (user_id, coins)
            VALUES (@user_id, @coins)
            ON CONFLICT (user_id) DO UPDATE SET
                coins = balances.coins + EXCLUDED.coins,
                updated_at = now();
            """;

        await using (var updateBalanceCommand = new NpgsqlCommand(updateBalanceSql, connection, transaction))
        {
            updateBalanceCommand.Parameters.AddWithValue("user_id", order.UserId);
            updateBalanceCommand.Parameters.AddWithValue("coins", order.Coins);
            await updateBalanceCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        order.Status = "PAID";
        order.Credited = true;
        return new CreditResult(true, order, "credited");
    }

    public async Task MarkCancelledAsync(long orderCode)
    {
        const string sql = """
            UPDATE orders
            SET status = 'CANCELLED',
                updated_at = now()
            WHERE order_code = @order_code
              AND credited = FALSE;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("order_code", orderCode);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddOrderParameters(NpgsqlCommand command, Order order)
    {
        command.Parameters.AddWithValue("order_code", order.OrderCode);
        command.Parameters.AddWithValue("user_id", order.UserId);
        command.Parameters.AddWithValue("package_id", order.PackageId);
        command.Parameters.AddWithValue("coins", order.Coins);
        command.Parameters.AddWithValue("amount_vnd", order.AmountVnd);
        command.Parameters.AddWithValue("status", order.Status);
        command.Parameters.AddWithValue("credited", order.Credited);
    }

    private static Order ReadOrder(NpgsqlDataReader reader) => new()
    {
        OrderCode = reader.GetInt64(0),
        UserId = reader.GetString(1),
        PackageId = reader.GetString(2),
        Coins = reader.GetInt32(3),
        AmountVnd = reader.GetInt32(4),
        Status = reader.GetString(5),
        Credited = reader.GetBoolean(6),
    };

    private static OrderView ReadOrderView(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetInt32(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
        new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero));

    private static string NormalizeConnectionString(string connectionString)
    {
        string trimmed = connectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        string username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? "");
        string password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "");
        string database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        bool isLocal = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = username,
            Password = password,
            Database = database,
            Pooling = true,
        };

        if (!isLocal)
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }
}

public sealed record CreditResult(bool Credited, Order? Order, string Reason);
