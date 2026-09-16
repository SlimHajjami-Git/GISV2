using Xunit;

namespace GisAPI.Tests.Common;

/// <summary>
/// Test qui ne s'exécute que sur une machine hors UTC. Npgsql en mode legacy
/// (Program.cs) relit les timestamptz en heure LOCALE : c'est hors UTC que le
/// jour local d'une échéance diffère de son jour UTC. Sur une machine réglée
/// sur UTC, le décalage ne peut pas se produire et le test ne prouverait rien ;
/// le motif du saut apparaît dans le rapport de tests.
/// </summary>
public sealed class FactMachineHorsUtcAttribute : FactAttribute
{
    public FactMachineHorsUtcAttribute()
    {
        if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc)) == TimeSpan.Zero)
            Skip = "Machine réglée sur UTC : le décalage de jour de la relecture Npgsql legacy ne peut pas s'y produire.";
    }
}
