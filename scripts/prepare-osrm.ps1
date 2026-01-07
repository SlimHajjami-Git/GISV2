# Script to prepare OSRM data for Tunisia (PowerShell)

$ErrorActionPreference = "Stop"

$DATA_DIR = ".\osrm-data"
New-Item -ItemType Directory -Force -Path $DATA_DIR | Out-Null

Write-Host "=== Downloading Tunisia OSM data ===" -ForegroundColor Green
Invoke-WebRequest -Uri "https://download.geofabrik.de/africa/tunisia-latest.osm.pbf" -OutFile "$DATA_DIR\tunisia-latest.osm.pbf"

Write-Host "=== Extracting road network ===" -ForegroundColor Green
docker run -t -v "${PWD}\${DATA_DIR}:/data" osrm/osrm-backend osrm-extract -p /opt/car.lua /data/tunisia-latest.osm.pbf

Write-Host "=== Partitioning ===" -ForegroundColor Green
docker run -t -v "${PWD}\${DATA_DIR}:/data" osrm/osrm-backend osrm-partition /data/tunisia-latest.osrm

Write-Host "=== Customizing ===" -ForegroundColor Green
docker run -t -v "${PWD}\${DATA_DIR}:/data" osrm/osrm-backend osrm-customize /data/tunisia-latest.osrm

Write-Host "=== OSRM data ready! ===" -ForegroundColor Green
Write-Host "Files created in $DATA_DIR"
Write-Host "Now run: docker compose up -d osrm"
