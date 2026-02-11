using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

// NOTE: AlertSeverity, AlertType, CostType, VehicleStatus, MaintenanceType, MaintenanceStatus
// are defined in their own files in this namespace.

// ==================== VEHICLE ====================

public enum VehicleType
{
    [EnumMember(Value = "camion")] Camion,
    [EnumMember(Value = "voiture")] Voiture,
    [EnumMember(Value = "moto")] Moto,
    [EnumMember(Value = "bus")] Bus,
    [EnumMember(Value = "utilitaire")] Utilitaire,
    [EnumMember(Value = "remorque")] Remorque,
    [EnumMember(Value = "engin")] Engin,
    [EnumMember(Value = "autre")] Autre
}

public enum FuelTypeEnum
{
    [EnumMember(Value = "diesel")] Diesel,
    [EnumMember(Value = "gasoline")] Gasoline,
    [EnumMember(Value = "electric")] Electric,
    [EnumMember(Value = "hybrid")] Hybrid,
    [EnumMember(Value = "lpg")] Lpg
}

// ==================== DRIVING EVENTS ====================

public enum DrivingEventType
{
    [EnumMember(Value = "harsh_braking")] HarshBraking,
    [EnumMember(Value = "harsh_acceleration")] HarshAcceleration,
    [EnumMember(Value = "sharp_turn")] SharpTurn,
    [EnumMember(Value = "overspeed")] Overspeed,
    [EnumMember(Value = "speed_bump")] SpeedBump,
    [EnumMember(Value = "pothole")] Pothole,
    [EnumMember(Value = "idle")] Idle,
    [EnumMember(Value = "other")] Other
}

// ==================== ACCIDENT ====================

public enum AccidentSeverity
{
    [EnumMember(Value = "minor")] Minor,
    [EnumMember(Value = "moderate")] Moderate,
    [EnumMember(Value = "major")] Major,
    [EnumMember(Value = "total_loss")] TotalLoss
}

public enum ClaimStatus
{
    [EnumMember(Value = "draft")] Draft,
    [EnumMember(Value = "submitted")] Submitted,
    [EnumMember(Value = "under_review")] UnderReview,
    [EnumMember(Value = "approved")] Approved,
    [EnumMember(Value = "rejected")] Rejected,
    [EnumMember(Value = "closed")] Closed
}

// ==================== MAINTENANCE ====================

public enum ScheduleStatus
{
    [EnumMember(Value = "ok")] Ok,
    [EnumMember(Value = "upcoming")] Upcoming,
    [EnumMember(Value = "due")] Due,
    [EnumMember(Value = "overdue")] Overdue,
    [EnumMember(Value = "critical")] Critical
}

public enum Priority
{
    [EnumMember(Value = "low")] Low,
    [EnumMember(Value = "medium")] Medium,
    [EnumMember(Value = "high")] High,
    [EnumMember(Value = "critical")] Critical
}

// ==================== GEOFENCE ====================

public enum GeofenceType
{
    [EnumMember(Value = "polygon")] Polygon,
    [EnumMember(Value = "circle")] Circle
}

public enum GeofenceEventType
{
    [EnumMember(Value = "entry")] Entry,
    [EnumMember(Value = "exit")] Exit
}

// ==================== CONTRACT ====================

public enum ContractType
{
    [EnumMember(Value = "insurance")] Insurance,
    [EnumMember(Value = "leasing")] Leasing,
    [EnumMember(Value = "rental")] Rental,
    [EnumMember(Value = "warranty")] Warranty,
    [EnumMember(Value = "service")] Service,
    [EnumMember(Value = "other")] Other
}

public enum ContractStatus
{
    [EnumMember(Value = "active")] Active,
    [EnumMember(Value = "expired")] Expired,
    [EnumMember(Value = "cancelled")] Cancelled,
    [EnumMember(Value = "pending")] Pending
}

public enum PaymentFrequency
{
    [EnumMember(Value = "monthly")] Monthly,
    [EnumMember(Value = "quarterly")] Quarterly,
    [EnumMember(Value = "yearly")] Yearly,
    [EnumMember(Value = "one_time")] OneTime
}

// ==================== RESERVATION ====================

public enum ReservationStatus
{
    [EnumMember(Value = "pending")] Pending,
    [EnumMember(Value = "approved")] Approved,
    [EnumMember(Value = "rejected")] Rejected,
    [EnumMember(Value = "in_progress")] InProgress,
    [EnumMember(Value = "completed")] Completed,
    [EnumMember(Value = "cancelled")] Cancelled
}

// ==================== REPAIR ====================

public enum RepairStatus
{
    [EnumMember(Value = "pending")] Pending,
    [EnumMember(Value = "in_progress")] InProgress,
    [EnumMember(Value = "completed")] Completed,
    [EnumMember(Value = "cancelled")] Cancelled
}

// ==================== SUPPLIER ====================

public enum SupplierType
{
    [EnumMember(Value = "general")] General,
    [EnumMember(Value = "parts")] Parts,
    [EnumMember(Value = "fuel")] Fuel,
    [EnumMember(Value = "tires")] Tires,
    [EnumMember(Value = "service")] Service,
    [EnumMember(Value = "garage")] Garage
}

// ==================== VEHICLE STOP ====================

public enum StopType
{
    [EnumMember(Value = "parking")] Parking,
    [EnumMember(Value = "traffic")] Traffic,
    [EnumMember(Value = "delivery")] Delivery,
    [EnumMember(Value = "refuel")] Refuel,
    [EnumMember(Value = "rest")] Rest,
    [EnumMember(Value = "unknown")] Unknown
}

// ==================== FUEL ====================

public enum FuelEventType
{
    [EnumMember(Value = "reading")] Reading,
    [EnumMember(Value = "refuel")] Refuel,
    [EnumMember(Value = "consumption_spike")] ConsumptionSpike,
    [EnumMember(Value = "theft_alert")] TheftAlert,
    [EnumMember(Value = "sensor_error")] SensorError,
    [EnumMember(Value = "low_fuel")] LowFuel
}

// ==================== COMPANY ====================

public enum CompanyType
{
    [EnumMember(Value = "transport")] Transport,
    [EnumMember(Value = "location")] Location,
    [EnumMember(Value = "autre")] Autre
}

// ==================== NOTIFICATION ====================

public enum MaintenanceNotificationType
{
    [EnumMember(Value = "upcoming")] Upcoming,
    [EnumMember(Value = "due")] Due,
    [EnumMember(Value = "overdue")] Overdue,
    [EnumMember(Value = "critical")] Critical
}

public enum MaintenanceTriggerReason
{
    [EnumMember(Value = "km")] Km,
    [EnumMember(Value = "date")] Date,
    [EnumMember(Value = "both")] Both
}

// ==================== TRANSACTION ====================

public enum PartTransactionType
{
    [EnumMember(Value = "purchase")] Purchase,
    [EnumMember(Value = "consumption")] Consumption,
    [EnumMember(Value = "return")] Return,
    [EnumMember(Value = "adjustment")] Adjustment,
    [EnumMember(Value = "transfer")] Transfer
}

// ==================== DOCUMENT ====================

public enum DocumentType
{
    [EnumMember(Value = "photo")] Photo,
    [EnumMember(Value = "police_report")] PoliceReport,
    [EnumMember(Value = "insurance_form")] InsuranceForm,
    [EnumMember(Value = "repair_estimate")] RepairEstimate,
    [EnumMember(Value = "other")] Other
}
