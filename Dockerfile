FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Mirage.sln ./
COPY src/Mirage.Domain/Mirage.Domain.csproj src/Mirage.Domain/
COPY src/Mirage.Application/Mirage.Application.csproj src/Mirage.Application/
COPY src/Mirage.Infrastructure/Mirage.Infrastructure.csproj src/Mirage.Infrastructure/
COPY src/Mirage.Api/Mirage.Api.csproj src/Mirage.Api/
RUN dotnet restore src/Mirage.Api/Mirage.Api.csproj

COPY src ./src
RUN dotnet publish src/Mirage.Api/Mirage.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
RUN apk add --no-cache icu-libs \
    && addgroup -S mirage \
    && adduser -S mirage -G mirage
COPY --from=build --chown=mirage:mirage /app/publish .

USER mirage
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mirage.Api.dll"]
