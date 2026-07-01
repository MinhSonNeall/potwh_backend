function logLine(message) {
    const log = document.getElementById("log");
    const time = new Date().toLocaleTimeString("vi-VN");
    log.textContent = `[${time}] ${message}\n` + log.textContent;
}

function getApiBase() {
    return document.getElementById("apiBase").value.trim().replace(/\/$/, "");
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
        logLine(`Health lỗi: ${err.message}`);
    }
}

async function loadPackages() {
    const body = document.getElementById("packageBody");
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/packages`);
        if (!Array.isArray(data) || data.length === 0) {
            body.innerHTML = "<tr><td colspan=\"4\">Không có gói nào.</td></tr>";
            logLine("Packages: không có dữ liệu");
            return;
        }
        body.innerHTML = "";
        for (const item of data) {
            const row = document.createElement("tr");
            row.innerHTML = `
                <td>${item.id}</td>
                <td>${item.name}</td>
                <td>${item.coins}</td>
                <td>${item.priceVnd}</td>
            `;
            body.appendChild(row);
        }
        logLine(`Packages: load được ${data.length} bản ghi`);
    } catch (err) {
        body.innerHTML = "<tr><td colspan=\"4\">Lỗi tải dữ liệu</td></tr>";
        logLine(`Packages lỗi: ${err.message}`);
    }
}

async function loadBalance() {
    const userId = document.getElementById("userIdInput").value.trim();
    const out = document.getElementById("balanceResult");
    if (!userId) {
        out.textContent = "Vui lòng nhập User ID";
        return;
    }
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/users/${encodeURIComponent(userId)}/balance`);
        out.textContent = `User ${data.userId}: ${data.coins} coin`;
        logLine(`Balance OK cho ${data.userId}`);
    } catch (err) {
        out.textContent = "Lỗi truy vấn số dư";
        logLine(`Balance lỗi: ${err.message}`);
    }
}

async function loadOrder() {
    const orderCode = document.getElementById("orderCodeInput").value.trim();
    const out = document.getElementById("orderResult");
    if (!orderCode) {
        out.textContent = "Vui lòng nhập mã đơn";
        return;
    }
    try {
        const data = await safeFetchJson(`${getApiBase()}/api/orders/${encodeURIComponent(orderCode)}/status`);
        out.textContent = `Đơn ${data.orderCode} | ${data.status} | Coins: ${data.coins} | Amount: ${data.amountVnd}`;
        logLine(`Order OK: ${data.orderCode} ${data.status}`);
    } catch (err) {
        out.textContent = "Không tìm thấy đơn hoặc lỗi truy vấn";
        logLine(`Order lỗi: ${err.message}`);
    }
}

document.getElementById("btnHealth").addEventListener("click", checkHealth);
document.getElementById("btnPackages").addEventListener("click", loadPackages);
document.getElementById("btnBalance").addEventListener("click", loadBalance);
document.getElementById("btnOrder").addEventListener("click", loadOrder);

logLine("Dashboard sẵn sàng.");
