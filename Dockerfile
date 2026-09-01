# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Only the .csproj files OpenCdsi.VaxEngine.Api actually depends on (itself, OpenCdsi.VaxEngine.Contracts, OpenCdsi.VaxEngine.Core) are
# copied for the restore-caching step - restoring OpenCdsi.VaxEngine.Api.csproj directly (not the whole
# OpenCdsi.VaxEngine.sln) means this image build never needs to know about OpenCdsi.VaxEngine.Demo, OpenCdsi.VaxEngine.Functions, or
# either test project, none of which are part of what gets published here. Also means adding a
# new project to the solution later doesn't require touching this Dockerfile unless OpenCdsi.VaxEngine.Api
# itself gains a new dependency.
COPY src/OpenCdsi.VaxEngine.Core/OpenCdsi.VaxEngine.Core.csproj src/OpenCdsi.VaxEngine.Core/
COPY src/OpenCdsi.VaxEngine.Contracts/OpenCdsi.VaxEngine.Contracts.csproj src/OpenCdsi.VaxEngine.Contracts/
COPY src/OpenCdsi.VaxEngine.Api/OpenCdsi.VaxEngine.Api.csproj src/OpenCdsi.VaxEngine.Api/
RUN dotnet restore src/OpenCdsi.VaxEngine.Api/OpenCdsi.VaxEngine.Api.csproj

COPY src/OpenCdsi.VaxEngine.Core/ src/OpenCdsi.VaxEngine.Core/
COPY src/OpenCdsi.VaxEngine.Contracts/ src/OpenCdsi.VaxEngine.Contracts/
COPY src/OpenCdsi.VaxEngine.Api/ src/OpenCdsi.VaxEngine.Api/
RUN dotnet publish src/OpenCdsi.VaxEngine.Api/OpenCdsi.VaxEngine.Api.csproj -c Release -o /app --no-restore

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

ENTRYPOINT ["dotnet", "OpenCdsi.VaxEngine.Api.dll"]
