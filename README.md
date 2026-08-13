# shopvot — Containerized shop + GitOps deploy (self-hosted CI/CD)

Ứng dụng web bán hàng (ASP.NET Core 8, minimal API) đóng gói Docker, **tự động deploy 24/7**
bằng GitHub Actions chạy trên **self-hosted runner**. `git push` → build image → rolling update.
Không ai `docker` bằng tay.

🔗 **Live:** https://shopvot.kientlt59.workers.dev/

---

## Luồng CI/CD

```
  git push (main)
        │
        ▼
  GitHub Actions  ──►  self-hosted runner (máy chủ, trong WSL/Docker)
        │                     │
        │                     ├─ rsync build-context
        │                     ├─ docker compose build badminton
        │                     ├─ docker compose up -d badminton   (rolling update)
        │                     ├─ health check  127.0.0.1:5080
        │                     └─ verify public URL
        ▼
  App live 24/7  ──►  Cloudflare Worker (reverse-proxy)  ──►  tunnel  ──►  container
```

Workflow: [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml).

## Chạy 24/7 (không phụ thuộc phiên đăng nhập)

- Container khai báo `restart: unless-stopped` → Docker tự bật lại khi lỗi/khởi động máy.
- Máy chủ dùng WSL: một **scheduled task (boot trigger, S4U)** giữ WSL sống liên tục nên
  Docker daemon + toàn bộ container không bị teardown khi không có ai đăng nhập.

## Kiến trúc container

| Thành phần | Vai trò |
|---|---|
| `badminton` | ASP.NET Core 8, phục vụ storefront + API (chat rule-based, giỏ hàng, đơn hàng, rating) |
| Docker volumes | `chats/`, `orders/`, `config/` = **state runtime**, tách khỏi image (không nằm trong git) |
| Cloudflare Worker | Reverse-proxy cho URL công khai ổn định, không interstitial |

`docker-compose.yml` ở đây là **bản tối giản để chạy cục bộ**:

```bash
docker compose up -d --build
# mở http://localhost:5080
```

## Build từ source

Ảnh Docker **build từ source C#** (`badminton/src/`) qua Dockerfile multi-stage:
`sdk:8.0` → `dotnet publish` → runtime `aspnet:8.0` (ảnh runtime gọn, không kèm SDK).

```
badminton/
├── Dockerfile            # multi-stage build
└── src/
    ├── BadmintonShop.csproj   # net8.0, minimal API
    ├── Program.cs            # endpoints: products / chat / order / rate / reply
    ├── *.cs                  # models (Order, MailCfg, Msg, DTO...)
    ├── wwwroot/              # storefront (html, img, video)
    └── config/              # default config (runtime override bằng volume)
```

## Ghi chú

- State khách hàng (chat/đơn hàng) **không** được commit — do Docker volume quản lý trên server.
- `config/` trong image là **default**; lúc chạy được volume `badminton-config` ghi đè (sửa nóng không cần build lại).

---

**Tech:** ASP.NET Core 8 · Docker · GitHub Actions (self-hosted runner) · Cloudflare Workers · WSL2
