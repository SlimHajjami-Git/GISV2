# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy all project files for restore
COPY GisAPI/*.csproj ./GisAPI/
COPY src/GisAPI.Domain/*.csproj ./src/GisAPI.Domain/
COPY src/GisAPI.Application/*.csproj ./src/GisAPI.Application/
COPY src/GisAPI.Infrastructure/*.csproj ./src/GisAPI.Infrastructure/
COPY src/GisAPI.Shared/*.csproj ./src/GisAPI.Shared/

# Restore dependencies
WORKDIR /src/GisAPI
RUN dotnet restore

# Copy everything else
WORKDIR /src
COPY GisAPI/ ./GisAPI/
COPY src/ ./src/

# Build
WORKDIR /src/GisAPI
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install cultures for localization + libfontconfig1 (required by SkiaSharp/QuestPDF
# for server-side PDF generation of the daily fleet report).
# locales + libfontconfig1 (QuestPDF) + postgresql-client-16 (pg_dump/psql pour
# les sauvegardes admin). Le client DOIT être en v16 pour correspondre au
# serveur PostGIS 16 (pg_dump refuse une version majeure différente) → dépôt PGDG.
RUN apt-get update \
    && apt-get install -y locales curl libfontconfig1 gnupg ca-certificates \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] http://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y postgresql-client-16 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Expose port
EXPOSE 5000

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Docker

ENTRYPOINT ["dotnet", "GisAPI.dll"]
