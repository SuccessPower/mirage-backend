FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Mirage.sln Directory.Build.props ./
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

# Debian-based (not alpine): OpenCvSharp's Linux native runtime is built against glibc and
# does not load under musl, so face detection (Mirage.Infrastructure/Vision) requires this base.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 libglib2.0-0 libsm6 libxext6 libxrender1 libgl1 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -r mirage && useradd -r -g mirage mirage
COPY --from=build --chown=mirage:mirage /app/publish .

USER mirage
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mirage.Api.dll"]
