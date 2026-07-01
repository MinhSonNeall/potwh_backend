# POTWH Backend

ASP.NET Core backend for PayOS payment links, payment webhooks, order tracking, and coin balance updates.

## Requirements

- .NET SDK 9.0
- PayOS merchant credentials
- A public HTTPS URL for webhook callbacks in development, such as an ngrok tunnel

## Configuration

Set these values in `appsettings.Development.json`, user secrets, or your deployment environment. Do not commit real PayOS credentials.

```json
{
  "AppBaseUrl": "https://your-public-url",
  "DataFilePath": "App_Data/payos-store.json",
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
- `POST /payos-webhook` - PayOS webhook endpoint
- `POST /api/confirm-webhook?url={publicWebhookUrl}` - register a PayOS webhook URL

## Notes

Orders and balances are stored in a local JSON file under `App_Data` by default. Replace this with a transactional database before production use.
