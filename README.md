# POTWH Backend

ASP.NET Core backend for PayOS payment links, payment webhooks, order tracking, and coin balance updates.

## Requirements

- .NET SDK 9.0
- PostgreSQL
- PayOS merchant credentials
- A public HTTPS URL for webhook callbacks in development, such as an ngrok tunnel

## Configuration

Set these values in `appsettings.Development.json`, user secrets, or your deployment environment. Do not commit real PayOS credentials.

```json
{
  "AppBaseUrl": "https://your-public-url",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=potwh_backend;Username=postgres;Password=postgres"
  },
  "PayOS": {
    "ClientId": "your-client-id",
    "ApiKey": "your-api-key",
    "ChecksumKey": "your-checksum-key"
  },
  "CoinPackages": [
    {
      "Id": "starter",
      "Name": "Starter Pack",
      "Coins": 100,
      "PriceVnd": 10000
    }
  ]
}
```

On Render, set `DATABASE_URL` to your Render Postgres internal database URL. The app creates the required `orders` and `balances` tables automatically on startup.

## Run

```bash
dotnet restore
dotnet run
```

## API

- `GET /` - health check
- `GET /api/packages` - list configured coin packages
- `GET /api/users/{userId}/balance` - get a user's coin balance
- `POST /api/orders/create` - create a PayOS payment link
- `GET /api/orders/{orderCode}/status` - get order status
- `GET /api/orders` - list all orders (newest first)
- `GET /api/orders/summary` - totals and order counts for dashboard
- `POST /payos-webhook` - PayOS webhook endpoint
- `POST /api/confirm-webhook?url={publicWebhookUrl}` - register a PayOS webhook URL

## Notes

Orders and balances are stored in PostgreSQL. For Render, deploy this repo as a Docker web service and set `ASPNETCORE_URLS` to `http://0.0.0.0:10000`.

## Local dashboard (POTWH_data)

Static dashboard for quick inspection is in the `POTWH_data` folder:

- Open `POTWH_data/index.html` directly in browser.
- It reads data from:
  - `/api/packages`
  - `/api/users/{userId}/balance`
  - `/api/orders/{orderCode}/status`
- Make sure backend allows CORS (already configured in `Program.cs`).
