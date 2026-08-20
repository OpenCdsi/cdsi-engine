# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Cdsi.sln .
COPY src/Cdsi.Core/Cdsi.Core.csproj src/Cdsi.Core/
COPY tests/Cdsi.Core.Tests/Cdsi.Core.Tests.csproj tests/Cdsi.Core.Tests/
RUN dotnet restore Cdsi.sln

COPY src/ src/
COPY tests/ tests/
RUN dotnet build Cdsi.sln -c Release --no-restore

# NOTE: this Dockerfile builds Cdsi.Core only. There is no ASP.NET API project yet
# (that's next in the build order) — this container is a placeholder until Cdsi.Api exists.
# When it's added: RUN dotnet publish src/Cdsi.Api/Cdsi.Api.csproj -c Release -o /app

# Reference data is deliberately NOT baked into the image (see README) — mount it as a
# volume at runtime so a supporting-data update doesn't require a rebuild.
VOLUME /data

# Placeholder runtime stage — replace with the ASP.NET runtime image once Cdsi.Api exists.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime
WORKDIR /app
COPY --from=build /src .
VOLUME /data
ENTRYPOINT ["dotnet", "test", "Cdsi.sln"]
