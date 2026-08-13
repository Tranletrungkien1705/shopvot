# ---- build stage: bien dich tu source C# ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BadmintonShop.csproj ./
RUN dotnet restore
COPY src/ ./
RUN dotnet publish BadmintonShop.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage: chi chua .NET runtime + app da publish ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
# App tu bind http://0.0.0.0:8080 (xem app.Run trong Program.cs)
ENTRYPOINT ["dotnet", "BadmintonShop.dll"]
