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

# Install cultures for localization
RUN apt-get update && apt-get install -y locales && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Expose port
EXPOSE 5000

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Docker

ENTRYPOINT ["dotnet", "GisAPI.dll"]
