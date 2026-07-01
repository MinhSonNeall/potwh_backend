function logLine(message) {
    const log = document.getElementById("log");
    const time = new Date().toLocaleTimeString("vi-VN");
    log.textContent = `[${time}] ${message}\n` + log.textContent;
}

function getApiBase() {
    return document.getElementById("apiBase").value.trim().replace(/\/$/, "");
}

function pick(obj, ...keys) {
    for (const key of keys) {
        if (obj && obj[key] !== undefined && obj[key] !== null) {
            return obj[key];
        }
    }
    return undefined;
}

function formatMoney(value) {
    return Number(value || 0).toLocaleString("vi-VN");
}

function formatDate(value) {
    if (!value) {
        return "-";
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "-";
    }
    return date.toLocaleString("vi-VN", { hour12: false });
}

async function safeFetchJson(url, options = {}) {
    const response = await fetch(url, options);
    const text = await response.text();
    if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}: ${text}`);
    }

    if (!text) {
        return null;
    }

    return JSON.parse(text);
}

async function safeFetchText(url, options = {}) {
    const response = await fetch(url, options);
    const text = await response.text();
    if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}: ${text}`);
    }
    return text;
}

async function checkHealth() {
    const base = getApiBase();
    const status = document.getElementById("healthStatus");
    try {
        const res = await safeFetchText(`${base}/`);
        status.textContent = `OK: ${res}`;
        logLine("Health OK");
    } catch (err) {
        status.textContent = err.message;
        logLine(`Health loi: ${err.message}`);
    }
}

async function loadPackages() {
    const body = document.getElementById("packageBody");
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/packages`);
        if (!Array.isArray(data) || data.length === 0) {
            body.innerHTML = "<tr><td colspan=\"4\">Khong co goi nao.</td></tr>";
            logLine("Packages: khong co du lieu");
            return;
        }
        body.innerHTML = "";
        for (const item of data) {
            const row = document.createElement("tr");
            row.innerHTML = `
                <td>${item.id}</td>
                <td>${item.name}</td>
                <td>${formatMoney(item.coins)}</td>
                <td>${formatMoney(item.priceVnd)}</td>
            `;
            body.appendChild(row);
        }
        logLine(`Packages: load duoc ${data.length} ban ghi`);
    } catch (err) {
        body.innerHTML = "<tr><td colspan=\"4\">Loi tai du lieu</td></tr>";
        logLine(`Packages loi: ${err.message}`);
    }
}

async function loadSummary() {
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/orders/summary`);
        const totalOrders = Number(pick(data, "totalOrders", "TotalOrders") || 0);
        const totalAmount = Number(pick(data, "totalAmountVnd", "TotalAmountVnd") || 0);
        const totalPaidAmount = Number(pick(data, "totalPaidAmountVnd", "TotalPaidAmountVnd") || 0);
        const totalCoins = Number(pick(data, "totalCoins", "TotalCoins") || 0);
        const pending = Number(pick(data, "pendingOrders", "PendingOrders") || 0);
        const paid = Number(pick(data, "paidOrders", "PaidOrders") || 0);
        const cancelled = Number(pick(data, "cancelledOrders", "CancelledOrders") || 0);
        const failed = Number(pick(data, "failedOrders", "FailedOrders") || 0);

        document.getElementById("statTotalOrders").textContent = formatMoney(totalOrders);
        document.getElementById("statTotalAmount").textContent = `${formatMoney(totalAmount)} VND`;
        document.getElementById("statPaidAmount").textContent = `${formatMoney(totalPaidAmount)} VND`;
        document.getElementById("statTotalCoins").textContent = formatMoney(totalCoins);
        document.getElementById("statPending").textContent = formatMoney(pending);
        document.getElementById("statPaid").textContent = formatMoney(paid);
        document.getElementById("statCancelled").textContent = formatMoney(cancelled);
        document.getElementById("statFailed").textContent = formatMoney(failed);
        logLine(`Summary OK: ${totalOrders} don`);
    } catch (err) {
        document.getElementById("statTotalOrders").textContent = "Loi";
        document.getElementById("statTotalAmount").textContent = "Loi";
        document.getElementById("statPaidAmount").textContent = "Loi";
        document.getElementById("statTotalCoins").textContent = "Loi";
        document.getElementById("statPending").textContent = "Loi";
        document.getElementById("statPaid").textContent = "Loi";
        document.getElementById("statCancelled").textContent = "Loi";
        document.getElementById("statFailed").textContent = "Loi";
        logLine(`Summary loi: ${err.message}`);
    }
}

async function loadOrders() {
    const body = document.getElementById("ordersBody");
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/orders`);
        if (!Array.isArray(data) || data.length === 0) {
            body.innerHTML = "<tr><td colspan=\"9\">Chua co don hang nao.</td></tr>";
            logLine("Orders: khong co du lieu");
            return;
        }

        body.innerHTML = "";
        for (const item of data) {
            const row = document.createElement("tr");
            const orderCode = pick(item, "orderCode", "OrderCode");
            const userId = pick(item, "userId", "UserId");
            const packageId = pick(item, "packageId", "PackageId");
            const coins = pick(item, "coins", "Coins");
            const amountVnd = pick(item, "amountVnd", "AmountVnd");
            const status = pick(item, "status", "Status");
            const credited = pick(item, "credited", "Credited");
            const createdAt = pick(item, "createdAt", "CreatedAt");
            const updatedAt = pick(item, "updatedAt", "UpdatedAt");

            row.innerHTML = `
                <td>${orderCode ?? "-"}</td>
                <td>${userId ?? "-"}</td>
                <td>${packageId ?? "-"}</td>
                <td>${formatMoney(coins)}</td>
                <td>${formatMoney(amountVnd)}</td>
                <td>${status ?? "-"}</td>
                <td>${credited ? "Yes" : "No"}</td>
                <td>${formatDate(createdAt)}</td>
                <td>${formatDate(updatedAt)}</td>
            `;
            body.appendChild(row);
        }
        logLine(`Orders: load duoc ${data.length} ban ghi`);
    } catch (err) {
        body.innerHTML = "<tr><td colspan=\"9\">Loi tai danh sach don.</td></tr>";
        logLine(`Orders loi: ${err.message}`);
    }
}

async function loadBalance() {
    const userId = document.getElementById("userIdInput").value.trim();
    const out = document.getElementById("balanceResult");
    if (!userId) {
        out.textContent = "Vui long nhap User ID";
        return;
    }
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/users/${encodeURIComponent(userId)}/balance`);
        out.textContent = `User ${data.userId}: ${data.coins} coin`;
        logLine(`Balance OK cho ${data.userId}`);
    } catch (err) {
        out.textContent = "Loi truy van so du";
        logLine(`Balance loi: ${err.message}`);
    }
}

async function loadOrder() {
    const orderCode = document.getElementById("orderCodeInput").value.trim();
    const out = document.getElementById("orderResult");
    if (!orderCode) {
        out.textContent = "Vui long nhap ma don";
        return;
    }
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/orders/${encodeURIComponent(orderCode)}/status`);
        out.textContent = `Don ${data.orderCode} | ${data.status} | Coins: ${formatMoney(data.coins)} | Amount: ${formatMoney(data.amountVnd)} VND`;
        logLine(`Order OK: ${data.orderCode} ${data.status}`);
    } catch (err) {
        out.textContent = "Khong tim thay don hoac loi truy van";
        logLine(`Order loi: ${err.message}`);
    }
}

async function initializeDashboard() {
    await Promise.all([
        checkHealth(),
        loadPackages(),
        loadSummary(),
        loadOrders()
    ]);
}

document.getElementById("btnHealth").addEventListener("click", checkHealth);
document.getElementById("btnPackages").addEventListener("click", loadPackages);
document.getElementById("btnOrders").addEventListener("click", loadOrders);
document.getElementById("btnBalance").addEventListener("click", loadBalance);
document.getElementById("btnOrder").addEventListener("click", loadOrder);

document.addEventListener("DOMContentLoaded", () => {
    logLine("Dashboard san sang.");
    initializeDashboard().catch((err) => {
        logLine(`Khoi dong loi: ${err.message}`);
    });
});
