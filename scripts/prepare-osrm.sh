#!/bin/bash
# Script to prepare OSRM data for Tunisia

set -e

DATA_DIR="./osrm-data"
mkdir -p $DATA_DIR

echo "=== Downloading Tunisia OSM data ==="
wget -O $DATA_DIR/tunisia-latest.osm.pbf https://download.geofabrik.de/africa/tunisia-latest.osm.pbf

echo "=== Extracting road network ==="
docker run -t -v "${PWD}/$DATA_DIR:/data" osrm/osrm-backend osrm-extract -p /opt/car.lua /data/tunisia-latest.osm.pbf

echo "=== Partitioning ==="
docker run -t -v "${PWD}/$DATA_DIR:/data" osrm/osrm-backend osrm-partition /data/tunisia-latest.osrm

echo "=== Customizing ==="
docker run -t -v "${PWD}/$DATA_DIR:/data" osrm/osrm-backend osrm-customize /data/tunisia-latest.osrm

echo "=== OSRM data ready! ==="
echo "Files created in $DATA_DIR"
