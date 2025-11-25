# GPS Decoding Strategy

## Goals
- Support heterogeneous tracker models (legacy AAP, NR024, GT60, EPS2, TK102/103, plus future protocols).
- Guarantee deterministic parsing, validation, and normalization before events enter RabbitMQ.
- Provide extensibility via plugin architecture so new models are added without redeploying the entire ingest service.

## Architecture
```
                             ┌─────────────────────────┐
Incoming frames ───────────▶ │ Protocol Router         │
                             └──────────┬──────────────┘
                                        │ selects decoder by signature
                        ┌───────────────┴────────────────┐
                        ▼                                ▼
              ┌───────────────────┐             ┌───────────────────┐
              │ Decoder Trait impl│ ...         │ Decoder Trait impl│
              │ (NR024, AAP, ...) │             │ (Future model)    │
              └──────────┬────────┘             └──────────┬────────┘
                         │ NormalizedTelemetry              │
                         ▼                                  ▼
                  Validation Pipeline (schema, ranges, auth)
                         │
                         ▼
                 Publisher (RabbitMQ exchange per topic)
```

### Decoder trait (Rust)
```rust
pub trait FrameDecoder {
    fn protocol(&self) -> ProtocolId;
    fn matches(&self, raw: &[u8]) -> bool;
    fn decode(&self, raw: &[u8]) -> Result<NormalizedTelemetry, DecodeError>;
}
```
- Registered via plugin registry; each decoder packaged as a crate feature.
- `matches` uses signatures (header bytes, checksum style).
- `decode` returns structured data using shared `NormalizedTelemetry` (Serde + protobuf schema).

### Topics and schemas
| Topic | Payload schema |
|-------|----------------|
| `telemetry.position` | Lat/Lng, speed, heading, odometer, accuracy |
| `telemetry.health` | Device firmware, battery, connectivity |
| `telemetry.alert` | Alarm enums (panic, power cut, tamper) |
| `telemetry.raw` | Optional raw frame storage for audit |

- Schemas stored in `/services/shared-kernel/proto` (protobuf) + `/services/shared-kernel/avro` for compatibility.
- Schema registry (e.g. Apicurio or Redpanda Schema Registry) recommended for validation.

### Error handling & observability
- Frames that fail decoding go to `dead-letter` queue with error context.
- Metrics: per protocol throughput, decode latency, error rate.
- Tracing: OpenTelemetry spans around decode → publish pipeline.

### Security
- Device authentication via SIM/IMEI mapping table (`devices` table) validated before accepting data.
- TLS between trackers and ingest gateway where possible; fallback to VPN-lined APNs for legacy hardware.

### Testing strategy
1. Golden sample library for each protocol with expected NormalizedTelemetry outputs.
2. Property-based fuzz tests to ensure parser resilience.
3. Integration tests using Dockerized RabbitMQ + Postgres to verify end-to-end ingestion.
