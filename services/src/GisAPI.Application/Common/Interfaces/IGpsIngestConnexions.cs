namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Connexions GPS en attente d'identification, tenues par l'ingestion Rust
/// (<c>GET /connexions/en-attente</c>, <c>POST /connexions/attribuer</c>).
///
/// Un NEMS_LITE R00C03 qui se reconnecte sans renvoyer son AA00 n'est plus
/// jeté : ses trames sont retenues jusqu'à ce qu'un administrateur confirme le
/// véhicule (voir gps-ingest-rust/src/attente.rs).
/// </summary>
public interface IGpsIngestConnexions
{
    /// <summary>Connexions en attente ; <c>null</c> si l'ingestion ne répond pas.</summary>
    Task<IReadOnlyList<HeldConnection>?> ListAsync(CancellationToken ct = default);

    /// <summary>Rattache la connexion <paramref name="peer"/> au boîtier <paramref name="imei"/>
    /// et fait ingérer ses trames retenues. <paramref name="by"/> est tracé côté ingestion.</summary>
    Task<HeldConnectionAttribution> AttributeAsync(string peer, string imei, string by, CancellationToken ct = default);
}

/// <summary>Position lue dans une trame retenue ; <see cref="At"/> est l'heure GPS (UTC).</summary>
public record HeldPosition(double Lat, double Lon, DateTime At, double SpeedKph);

public record HeldConnection(
    string Peer,
    string Protocol,
    bool Open,
    DateTime FirstReceivedAt,
    DateTime LastReceivedAt,
    int Frames,
    long DroppedFrames,
    HeldPosition? FirstPosition,
    HeldPosition? LastPosition);

public enum HeldAttributionOutcome
{
    /// <summary>Rattachée ; trames retenues ingérées.</summary>
    Done,
    /// <summary>Plus en attente (déjà rattachée, ou oubliée après 48 h).</summary>
    UnknownConnection,
    /// <summary>Le boîtier s'est annoncé entre-temps sous un autre IMEI : refusé.</summary>
    AlreadyIdentified,
    /// <summary>Ingestion injoignable ou réponse inattendue.</summary>
    Error
}

public record HeldConnectionAttribution(
    HeldAttributionOutcome Outcome,
    int ReplayedFrames,
    bool ConnectionOpen,
    string? Detail);
