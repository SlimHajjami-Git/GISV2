# syntax=docker/dockerfile:1.6

############################
# 1) Build stage
############################
FROM rustlang/rust:nightly-slim AS builder

WORKDIR /app

# Install build dependencies (openssl for sqlx + RabbitMQ TLS, etc.)
RUN apt-get update && \
    apt-get install -y --no-install-recommends pkg-config libssl-dev ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# Cache dependencies
COPY Cargo.toml Cargo.lock ./
COPY src ./src

RUN cargo build --release

############################
# 2) Runtime stage
############################
FROM debian:bookworm-slim AS runtime

RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=builder /app/target/release/gps-ingest-rust /usr/local/bin/gps-ingest-rust

ENV RUST_LOG=info \
    DATABASE_URL=postgres://postgres:postgres@postgres:5432/gis_v2

# Default ports (override via config/listeners.yaml)
EXPOSE 6100 6200 6210

COPY config ./config

CMD ["/usr/local/bin/gps-ingest-rust"]
