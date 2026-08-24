# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Only the .csproj files Cdsi.Api actually depends on (itself, Cdsi.Contracts, Cdsi.Core) are
# copied for the restore-caching step - restoring Cdsi.Api.csproj directly (not the whole
# Cdsi.sln) means this image build never needs to know about Cdsi.Demo, Cdsi.Functions, or
# either test project, none of which are part of what gets published here. Also means adding a
# new project to the solution later doesn't require touching this Dockerfile unless Cdsi.Api
# itself gains a new dependency.
COPY src/Cdsi.Core/Cdsi.Core.csproj src/Cdsi.Core/
COPY src/Cdsi.Contracts/Cdsi.Contracts.csproj src/Cdsi.Contracts/
COPY src/Cdsi.Api/Cdsi.Api.csproj src/Cdsi.Api/
RUN dotnet restore src/Cdsi.Api/Cdsi.Api.csproj

COPY src/Cdsi.Core/ src/Cdsi.Core/
COPY src/Cdsi.Contracts/ src/Cdsi.Contracts/
COPY src/Cdsi.Api/ src/Cdsi.Api/
RUN dotnet publish src/Cdsi.Api/Cdsi.Api.csproj -c Release -o /app --no-restore

# Runtime stage - the smaller ASP.NET runtime image, not the full SDK.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
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
