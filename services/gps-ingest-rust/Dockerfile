# syntax=docker/dockerfile:1.6

############################
# 1) Build stage
############################
# Compilateur FIGE, et stable. L'image tirait « rustlang/rust:nightly-slim » :
# chaque build prenait la version experimentale du jour. Le 24/09/2026 l'ingest
# est devenu INCONSTRUCTIBLE sans que personne n'ait touche au projet — nightly a
# retire le repli du type « jamais » vers (), ce que redis 0.25.4 (fige par
# Cargo.lock) enfreint : « the trait bound !: FromRedisValue is not satisfied »,
# 4 erreurs dans script.rs. Consequence : le correctif batterie du 17/09 est
# reste SEPT JOURS sur master sans pouvoir partir.
# Le code n'utilise AUCUNE fonctionnalite nightly (zero #![feature]) : la version
# stable suffit, et la figer rend le build reproductible. Ne pas remettre
# « nightly » ni une etiquette flottante.
#
# POURQUOI 1.90 ET PAS PLUS ANCIEN : Cargo.lock (versionne, 318 paquets) fige
# base64ct 1.8.1, dont le manifeste exige l'edition 2024 — stabilisee seulement
# a partir de Cargo 1.85. Un essai en 1.83 echoue des la resolution :
# « feature edition2024 is required ». La fenetre qui marche est donc
# stable >= 1.85 : assez recent pour l'edition 2024, et le repli du type
# « jamais » vers () y reste en vigueur pour les caisses en edition 2021 comme
# redis 0.25.4 — c'est nightly qui l'avait retire pour toutes les editions.
FROM rust:1.90-slim AS builder

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
EXPOSE 6100 6200 6210 6300

COPY config ./config

CMD ["/usr/local/bin/gps-ingest-rust"]
