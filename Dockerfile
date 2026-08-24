# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first (not the full source tree) so `dotnet restore` is cached across
# builds unless a .csproj actually changes - a real, if small, speed win during iteration.
COPY Cdsi.sln .
COPY src/Cdsi.Core/Cdsi.Core.csproj src/Cdsi.Core/
COPY src/Cdsi.Api/Cdsi.Api.csproj src/Cdsi.Api/
COPY src/Cdsi.Demo/Cdsi.Demo.csproj src/Cdsi.Demo/
COPY tests/Cdsi.Core.Tests/Cdsi.Core.Tests.csproj tests/Cdsi.Core.Tests/
COPY tests/Cdsi.Api.Tests/Cdsi.Api.Tests.csproj tests/Cdsi.Api.Tests/
RUN dotnet restore Cdsi.sln

COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/Cdsi.Api/Cdsi.Api.csproj -c Release -o /app --no-restore

# Runtime stage - the smaller ASP.NET runtime image, not the full SDK.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl isn't present in the base aspnet image by default - installed specifically so
# docker-compose's own HEALTHCHECK (see docker-compose.yml) has something to call.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
# CDSI_DATA_PATH: deliberately NOT baked into the image (see README's "top priority is easy
# updates" note) - mount the real data/ directory as a volume at /data instead, so a CDC
# schedule/logic update is a volume content change, not an image rebuild.
ENV CDSI_DATA_PATH=/data

EXPOSE 8080
VOLUME /data

ENTRYPOINT ["dotnet", "Cdsi.Api.dll"]
