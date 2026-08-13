using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
var app = builder.Build();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        string name = ctx.File.Name.ToLowerInvariant();
        bool isAsset = name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".png")
                       || name.EndsWith(".webp") || name.EndsWith(".gif") || name.EndsWith(".svg")
                       || name.EndsWith(".ico");
        // Ảnh/asset: cache 30 ngày (khách quay lại tải tức thì). HTML: luôn revalidate để update hiện ngay.
        ctx.Context.Response.Headers["Cache-Control"] = isAsset ? "public,max-age=2592000" : "no-cache";
    }
});

app.MapGet("/healthz", () => Results.Ok("ok"));

string baseDir = AppContext.BaseDirectory;
string chatDir = Path.Combine(baseDir, "chats");
string orderDir = Path.Combine(baseDir, "orders");
Directory.CreateDirectory(chatDir);
Directory.CreateDirectory(orderDir);

var locks = new ConcurrentDictionary<string, object>();
var rateLock = new object();
var orderLock = new object();
var opt = new JsonSerializerOptions { WriteIndented = true };

// Danh mục sản phẩm (mũ bảo hiểm)
var products = new[]
{
    new { id = 1, name = "Mũ Fullface Royal M179 (lật cằm)", brand = "Royal", price = 950000,  img = "/img/h1.jpg", video = "dFWw2p1jQGI", desc = "Mũ fullface lật cằm cao cấp, 2 kính, xốp EPS dày, đệm lót tháo giặt. Đi phượt + chạy phố." },
    new { id = 2, name = "Mũ Fullface AGU Tem Sói",         brand = "AGU",   price = 550000,  img = "/img/h2.jpg", video = "UsfsHwqJE4U", desc = "Mũ fullface giá tốt, tem sói cá tính, kính chống UV, đệm êm. Hợp bạn trẻ." },
    new { id = 3, name = "Mũ Fullface Yohe 950 (lật hàm)",   brand = "Yohe",  price = 1250000, img = "/img/h3.jpg", video = "7dbPfpHZE0w", desc = "Fullface lật hàm 2 kính, form rộng đội kính cận được, hợp đi tour." },
    new { id = 4, name = "Mũ Fullface Roc Helmet R03",       brand = "Roc",   price = 980000,  img = "/img/h4.jpg", video = "Bd5aG09Hy58", desc = "Fullface thể thao, nhiều khe gió mát, lót kháng khuẩn." },
    new { id = 5, name = "Mũ Fullface POC Revo (ECE 22.05)", brand = "POC",   price = 1450000, img = "/img/h6.jpg", video = "HIVgA7ygiEA", desc = "Đạt chuẩn ECE 22.05, form nhỏ gọn đội êm, nhẹ, an toàn cao." },
    new { id = 6, name = "Mũ Fullface LS2 FF320 (Racing)",   brand = "LS2",   price = 2350000, img = "/img/h7.jpg", video = "XWmZvy9NWyI", desc = "Fullface cao cấp, vỏ siêu bền nhẹ, kính chống UV, đạt ECE. Cho biker tốc độ cao." },
    new { id = 7, name = "Mũ Fullface EGO-K 727",            brand = "EGO",   price = 680000,  img = "/img/h8.jpg", video = "D17BZ_oPoyk", desc = "Fullface tầm trung, gọn gàng, kính chống chói, đệm êm." },
    new { id = 8, name = "Mũ 3/4 Andes 3S6 (nửa đầu)",       brand = "Andes", price = 380000,  img = "/img/h9.jpg", video = "hMYz60WE9qw", desc = "Mũ 3/4 nửa đầu nhẹ, thoáng mát, kính phi công. Tiện đi phố." },
};

app.MapGet("/api/products", () =>
{
    var aff = LoadAff();
    return Results.Json(products.Select(p => new
    {
        p.id, p.name, p.brand, p.price, p.img, p.video, p.desc,
        affiliate = aff.TryGetValue(p.id.ToString(), out var a) ? a : ""
    }));
});

app.MapGet("/api/ratings", () =>
{
    string path = Path.Combine(baseDir, "config", "ratings.json");
    var result = new Dictionary<string, object>();
    try
    {
        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                double sum = prop.Value.GetProperty("sum").GetDouble();
                int count = prop.Value.GetProperty("count").GetInt32();
                result[prop.Name] = new { avg = count > 0 ? Math.Round(sum / count, 1) : 0.0, count };
            }
        }
    }
    catch { }
    return Results.Json(result);
});

app.MapPost("/api/rate", (RateReq req) =>
{
    if (req.stars < 1 || req.stars > 5) return Results.BadRequest();
    string path = Path.Combine(baseDir, "config", "ratings.json");
    lock (rateLock)
    {
        var data = new Dictionary<string, Dictionary<string, double>>();
        try
        {
            if (File.Exists(path))
                data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        string key = req.productId.ToString();
        if (!data.ContainsKey(key))
            data[key] = new Dictionary<string, double> { { "sum", 0.0 }, { "count", 0.0 } };
        data[key]["sum"] += req.stars;
        data[key]["count"] += 1.0;
        File.WriteAllText(path, JsonSerializer.Serialize(data, opt));
        return Results.Json(new { ok = true, avg = Math.Round(data[key]["sum"] / data[key]["count"], 1), count = (int)data[key]["count"] });
    }
});

app.MapPost("/api/order", (OrderReq req) =>
{
    if (string.IsNullOrWhiteSpace(req.email) || req.items == null || req.items.Count == 0)
        return Results.BadRequest();
    var order = new Order
    {
        Id = Guid.NewGuid().ToString("N").Substring(0, 8),
        Name = req.name ?? "",
        Email = req.email.Trim(),
        Phone = req.phone ?? "",
        Note = req.note ?? "",
        Source = string.IsNullOrWhiteSpace(req.source) ? "web" : req.source,
        Status = "pending",
        Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    };
    long total = 0;
    foreach (var item in req.items)
    {
        long price = PriceOf(item.id);
        var line = new OrderLine { Id = item.id, Name = NameOf(item.id), Price = price, Qty = Math.Max(1, item.qty) };
        order.Items.Add(line);
        total += price * line.Qty;
    }
    order.Total = total;
    lock (orderLock)
        File.WriteAllText(Path.Combine(orderDir, order.Id + ".json"), JsonSerializer.Serialize(order, opt));

    var mail = LoadMail();
    if (mail.enabled && !string.IsNullOrWhiteSpace(mail.ownerEmail))
    {
        string lines = string.Join("", order.Items.Select(i => $"<li>{i.Name} × {i.Qty} = {i.Price * i.Qty:N0}đ</li>"));
        SendMail(mail.ownerEmail,
            $"[Shop Mũ] Đơn mới #{order.Id} ({order.Total:N0}đ)",
            $"<h3>Đơn hàng mới cần duyệt</h3><p><b>Mã:</b> {order.Id} | <b>Nguồn:</b> {order.Source} | {order.Ts}</p><p><b>Khách:</b> {order.Name} — {order.Phone} — {order.Email}</p><ul>{lines}</ul><p><b>Tổng: {order.Total:N0}đ</b></p><p>Ghi chú: {order.Note}</p><p>Vào trang admin để DUYỆT & gửi xác nhận cho khách.</p>");
    }
    return Results.Json(new { ok = true, orderId = order.Id });
});

app.MapGet("/api/orders/{key}", (string key) =>
{
    if (key != "shopvot2026") return Results.Unauthorized();
    var list = new List<Order>();
    foreach (string path in Directory.GetFiles(orderDir, "*.json"))
    {
        try
        {
            var o = JsonSerializer.Deserialize<Order>(File.ReadAllText(path));
            if (o != null) list.Add(o);
        }
        catch { }
    }
    return Results.Json(list.OrderByDescending(o => o.Ts));
});

app.MapPost("/api/order/approve", (ApproveReq req) =>
{
    if (req.key != "shopvot2026") return Results.Unauthorized();
    string path = Path.Combine(orderDir, Safe(req.orderId) + ".json");
    if (!File.Exists(path)) return Results.NotFound();
    Order? order;
    lock (orderLock)
        order = JsonSerializer.Deserialize<Order>(File.ReadAllText(path));
    if (order == null) return Results.NotFound();

    string lines = string.Join("", order.Items.Select(i => $"<li>{i.Name} × {i.Qty} = {i.Price * i.Qty:N0}đ</li>"));
    string html = $"<h2>Cảm ơn bạn đã đặt hàng tại Shop Mũ Bảo Hiểm! 🪖</h2><p>Đơn hàng <b>#{order.Id}</b> của bạn đã được xác nhận:</p><ul>{lines}</ul><p><b>Tổng cộng: {order.Total:N0}đ</b> (thanh toán khi nhận hàng - COD)</p><p>Shop sẽ liên hệ <b>{order.Phone}</b> để giao hàng sớm nhất. Xem thêm mẫu & video tại TikTok: <a href='https://www.tiktok.com/@non.dep.102'>@non.dep.102</a></p><p>Trân trọng,<br>Shop Mũ Bảo Hiểm</p>";
    var (ok, error) = SendMail(order.Email, "Xác nhận đơn hàng #" + order.Id + " - Shop Mũ Bảo Hiểm", html);
    if (!ok) return Results.Json(new { ok = false, error });

    order.Status = "confirmed";
    order.ConfirmedTs = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    lock (orderLock)
        File.WriteAllText(path, JsonSerializer.Serialize(order, opt));
    return Results.Json(new { ok = true });
});

app.MapGet("/api/mailstatus/{key}", (string key) =>
{
    if (key != "shopvot2026") return Results.Unauthorized();
    var mail = LoadMail();
    return Results.Json(new { enabled = mail.enabled, user = mail.user, ownerEmail = mail.ownerEmail, configured = !string.IsNullOrWhiteSpace(mail.appPassword) });
});

app.MapPost("/api/chat", (ChatPost req) =>
{
    string sid = Safe(req.sessionId);
    if (string.IsNullOrEmpty(sid) || string.IsNullOrWhiteSpace(req.message))
        return Results.BadRequest();
    string file = Path.Combine(chatDir, sid + ".json");
    lock (L(sid))
    {
        var msgs = Load(file);
        if (msgs.Count >= 40) return Results.Json(new { ok = false, full = true });
        msgs.Add(new Msg("user", req.message, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        string reply = RuleReplyTag(req.message).reply;
        if (!string.IsNullOrWhiteSpace(reply))
            msgs.Add(new Msg("assistant", reply, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        File.WriteAllText(file, JsonSerializer.Serialize(msgs, opt));
    }
    return Results.Json(new { ok = true, full = false });
});

app.MapGet("/api/ruletest", (string key, string q) =>
{
    if (key != "shopvot2026") return Results.Unauthorized();
    var (reply, tag) = RuleReplyTag(q);
    return Results.Json(new { q, tag, isDefault = tag == "DEFAULT", reply });
});

app.MapGet("/api/messages/{sessionId}", (string sessionId) =>
{
    string file = Path.Combine(chatDir, Safe(sessionId) + ".json");
    lock (L(Safe(sessionId)))
        return Results.Json(Load(file));
});

app.MapGet("/api/pending/{key}", (string key) =>
{
    if (key != "shopvot2026") return Results.Unauthorized();
    var pending = new List<object>();
    foreach (string file in Directory.GetFiles(chatDir, "*.json"))
    {
        var msgs = Load(file);
        if (msgs.Count > 0 && msgs[^1].role == "user")
            pending.Add(new { sessionId = Path.GetFileNameWithoutExtension(file), messages = msgs });
    }
    return Results.Json(pending);
});

app.MapPost("/api/reply", (ReplyPost req) =>
{
    if (req.key != "shopvot2026") return Results.Unauthorized();
    string sid = Safe(req.sessionId);
    string file = Path.Combine(chatDir, sid + ".json");
    if (!File.Exists(file)) return Results.NotFound();
    lock (L(sid))
    {
        var msgs = Load(file);
        msgs.Add(new Msg("assistant", req.content, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        File.WriteAllText(file, JsonSerializer.Serialize(msgs, opt));
    }
    return Results.Json(new { ok = true });
});

app.Run("http://0.0.0.0:8080");

// ----------------- local helpers -----------------
object L(string id) => locks.GetOrAdd(id, _ => new object());

static List<Msg> Load(string file) =>
    !File.Exists(file) ? new() : JsonSerializer.Deserialize<List<Msg>>(File.ReadAllText(file)) ?? new();

Dictionary<string, string> LoadAff()
{
    string path = Path.Combine(baseDir, "config", "affiliate.json");
    if (!File.Exists(path)) return new();
    try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); }
    catch { return new(); }
}

MailCfg LoadMail()
{
    string path = Path.Combine(baseDir, "config", "mail.json");
    if (!File.Exists(path)) return new MailCfg();
    try { return JsonSerializer.Deserialize<MailCfg>(File.ReadAllText(path)) ?? new MailCfg(); }
    catch { return new MailCfg(); }
}

string NameOf(int id) => products.FirstOrDefault(p => p.id == id)?.name ?? ("SP#" + id);
long PriceOf(int id) => products.FirstOrDefault(p => p.id == id)?.price ?? 0;

(string reply, string tag) RuleReplyTag(string? userMsg)
{
    string path = Path.Combine(baseDir, "config", "rules.json");
    if (!File.Exists(path)) return ("", "NONE");
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (!root.TryGetProperty("enabled", out var en) || !en.GetBoolean())
            return ("", "DISABLED");
        string text = (userMsg ?? "").ToLowerInvariant();
        if (root.TryGetProperty("rules", out var rules))
        {
            foreach (var rule in rules.EnumerateArray())
            {
                if (rule.TryGetProperty("keywords", out var kws))
                {
                    foreach (var kw in kws.EnumerateArray())
                    {
                        string? k = kw.GetString();
                        if (!string.IsNullOrEmpty(k) && text.Contains(k.ToLowerInvariant()))
                        {
                            string tag = kws.EnumerateArray().FirstOrDefault().GetString() ?? "?";
                            return (rule.TryGetProperty("reply", out var rep) ? (rep.GetString() ?? "") : "", tag);
                        }
                    }
                }
            }
        }
        return (root.TryGetProperty("default", out var def) ? (def.GetString() ?? "") : "", "DEFAULT");
    }
    catch { return ("", "ERROR"); }
}

static string Safe(string? id) =>
    new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

(bool ok, string err) SendMail(string to, string subject, string html)
{
    var mail = LoadMail();
    if (!mail.enabled) return (false, "mail disabled");
    if (string.IsNullOrWhiteSpace(mail.user) || string.IsNullOrWhiteSpace(mail.appPassword) || string.IsNullOrWhiteSpace(to))
        return (false, "mail not configured");
    try
    {
        using var msg = new MailMessage
        {
            From = new MailAddress(string.IsNullOrWhiteSpace(mail.fromEmail) ? mail.user : mail.fromEmail,
                                   string.IsNullOrWhiteSpace(mail.fromName) ? "Shop" : mail.fromName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = html,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
        };
        msg.To.Add(to);
        using var smtp = new SmtpClient(mail.smtpHost, mail.smtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(mail.user, mail.appPassword),
        };
        smtp.Send(msg);
        return (true, "");
    }
    catch (Exception ex) { return (false, ex.Message); }
}
