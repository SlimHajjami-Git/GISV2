using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class MaintenanceTemplateEnhancedConfiguration : IEntityTypeConfiguration<MaintenanceTemplate>
{
    public void Configure(EntityTypeBuilder<MaintenanceTemplate> builder)
    {
        builder.ToTable("maintenance_templates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Priority).HasColumnName("priority").HasMaxLength(20).HasDefaultValue("medium");
        builder.Property(e => e.IntervalKm).HasColumnName("interval_km");
        builder.Property(e => e.IntervalMonths).HasColumnName("interval_months");
        builder.Property(e => e.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(10, 2);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        
        // New columns
        builder.Property(e => e.WarningKm).HasColumnName("warning_km").HasDefaultValue(1000);
        builder.Property(e => e.WarningDays).HasColumnName("warning_days").HasDefaultValue(30);
        builder.Property(e => e.CriticalKm).HasColumnName("critical_km").HasDefaultValue(0);
        builder.Property(e => e.CriticalDays).HasColumnName("critical_days").HasDefaultValue(0);
        builder.Property(e => e.EstimatedDurationMinutes).HasColumnName("estimated_duration_minutes");
        builder.Property(e => e.Instructions).HasColumnName("instructions");
        builder.Property(e => e.AppliesToVehicleTypes).HasColumnName("applies_to_vehicle_types").HasColumnType("text[]");
        builder.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(50).HasDefaultValue("wrench");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasMany(e => e.Schedules)
            .WithOne(s => s.Template)
            .HasForeignKey(s => s.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Parts)
            .WithOne(p => p.Template)
            .HasForeignKey(p => p.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.CompanyId).HasDatabaseName("IX_maintenance_templates_CompanyId");
    }
}

public class VehicleMaintenanceScheduleEnhancedConfiguration : IEntityTypeConfiguration<VehicleMaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<VehicleMaintenanceSchedule> builder)
    {
        builder.ToTable("vehicle_maintenance_schedules");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.LastDoneDate).HasColumnName("last_done_date").HasColumnType("date");
        builder.Property(e => e.LastDoneKm).HasColumnName("last_done_km");
        builder.Property(e => e.NextDueDate).HasColumnName("next_due_date").HasColumnType("date");
        builder.Property(e => e.NextDueKm).HasColumnName("next_due_km");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("upcoming");
        
        // New columns
        builder.Property(e => e.IsPaused).HasColumnName("is_paused").HasDefaultValue(false);
        builder.Property(e => e.PausedAt).HasColumnName("paused_at");
        builder.Property(e => e.PausedReason).HasColumnName("paused_reason").HasMaxLength(255);
        builder.Property(e => e.CustomIntervalKm).HasColumnName("custom_interval_km");
        builder.Property(e => e.CustomIntervalMonths).HasColumnName("custom_interval_months");
        builder.Property(e => e.LastNotificationAt).HasColumnName("last_notification_at");
        builder.Property(e => e.NotificationCount).HasColumnName("notification_count").HasDefaultValue(0);
        builder.Property(e => e.Notes).HasColumnName("notes");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Template)
            .WithMany(t => t.Schedules)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Notifications)
            .WithOne(n => n.Schedule)
            .HasForeignKey(n => n.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.VehicleId).HasDatabaseName("IX_vehicle_maintenance_schedules_VehicleId");
        builder.HasIndex(e => e.TemplateId).HasDatabaseName("IX_vehicle_maintenance_schedules_TemplateId");
        builder.HasIndex(e => e.Status).HasDatabaseName("IX_vehicle_maintenance_schedules_Status");
        builder.HasIndex(e => new { e.VehicleId, e.TemplateId }).IsUnique().HasDatabaseName("IX_vehicle_maintenance_schedules_VehicleId_TemplateId");
    }
}

public class MaintenanceLogEnhancedConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> builder)
    {
        builder.ToTable("maintenance_logs");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.ScheduleId).HasColumnName("schedule_id");
        builder.Property(e => e.CostId).HasColumnName("cost_id");
        builder.Property(e => e.DoneDate).HasColumnName("done_date").HasColumnType("date");
        builder.Property(e => e.DoneKm).HasColumnName("done_km");
        builder.Property(e => e.ActualCost).HasColumnName("actual_cost").HasPrecision(10, 2);
        builder.Property(e => e.SupplierId).HasColumnName("supplier_id");
        builder.Property(e => e.Notes).HasColumnName("notes");
        
        // New columns
        builder.Property(e => e.TechnicianName).HasColumnName("technician_name").HasMaxLength(100);
        builder.Property(e => e.WorkOrderNumber).HasColumnName("work_order_number").HasMaxLength(50);
        builder.Property(e => e.PartsReplaced).HasColumnName("parts_replaced").HasColumnType("jsonb");
        builder.Property(e => e.LaborHours).HasColumnName("labor_hours").HasPrecision(5, 2);
        builder.Property(e => e.LaborCost).HasColumnName("labor_cost").HasPrecision(10, 2);
        builder.Property(e => e.PartsCost).HasColumnName("parts_cost").HasPrecision(10, 2);
        builder.Property(e => e.QualityRating).HasColumnName("quality_rating");
        builder.Property(e => e.Photos).HasColumnName("photos").HasColumnType("text[]");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Schedule)
            .WithMany()
            .HasForeignKey(e => e.ScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Cost)
            .WithMany()
            .HasForeignKey(e => e.CostId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.VehicleId).HasDatabaseName("IX_maintenance_logs_VehicleId");
        builder.HasIndex(e => e.TemplateId).HasDatabaseName("IX_maintenance_logs_TemplateId");
    }
}

public class MaintenanceTemplatePartConfiguration : IEntityTypeConfiguration<MaintenanceTemplatePart>
{
    public void Configure(EntityTypeBuilder<MaintenanceTemplatePart> builder)
    {
        builder.ToTable("maintenance_template_parts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.PartName).HasColumnName("part_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.PartNumber).HasColumnName("part_number").HasMaxLength(100);
        builder.Property(e => e.Quantity).HasColumnName("quantity").HasDefaultValue(1);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20).HasDefaultValue("unit");
        builder.Property(e => e.EstimatedUnitCost).HasColumnName("estimated_unit_cost").HasPrecision(10, 2);
        builder.Property(e => e.IsRequired).HasColumnName("is_required").HasDefaultValue(true);
        builder.Property(e => e.PreferredSupplierId).HasColumnName("preferred_supplier_id");
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Template)
            .WithMany(t => t.Parts)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PreferredSupplier)
            .WithMany()
            .HasForeignKey(e => e.PreferredSupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.TemplateId).HasDatabaseName("idx_template_parts_template_id");
    }
}

public class MaintenanceNotificationConfiguration : IEntityTypeConfiguration<MaintenanceNotification>
{
    public void Configure(EntityTypeBuilder<MaintenanceNotification> builder)
    {
        builder.ToTable("maintenance_notifications");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ScheduleId).HasColumnName("schedule_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.NotificationType).HasColumnName("notification_type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.TriggerReason).HasColumnName("trigger_reason").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CurrentKm).HasColumnName("current_km");
        builder.Property(e => e.KmRemaining).HasColumnName("km_remaining");
        builder.Property(e => e.DaysRemaining).HasColumnName("days_remaining");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.SentAt).HasColumnName("sent_at");
        builder.Property(e => e.SentChannels).HasColumnName("sent_channels").HasColumnType("text[]");
        builder.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by");
        builder.Property(e => e.DismissedAt).HasColumnName("dismissed_at");

        builder.HasOne(e => e.Schedule)
            .WithMany(s => s.Notifications)
            .HasForeignKey(e => e.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.VehicleId).HasDatabaseName("idx_maint_notif_vehicle");
        builder.HasIndex(e => e.CompanyId).HasDatabaseName("idx_maint_notif_company");
        builder.HasIndex(e => e.NotificationType).HasDatabaseName("idx_maint_notif_type");
        builder.HasIndex(e => new { e.ScheduleId, e.NotificationType, e.CreatedAt })
            .HasDatabaseName("ix_maint_notif_unique_daily");
    }
}

public class MaintenanceAlertSettingsConfiguration : IEntityTypeConfiguration<MaintenanceAlertSettings>
{
    public void Configure(EntityTypeBuilder<MaintenanceAlertSettings> builder)
    {
        builder.ToTable("maintenance_alert_settings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.EnablePush).HasColumnName("enable_push").HasDefaultValue(true);
        builder.Property(e => e.EnableEmail).HasColumnName("enable_email").HasDefaultValue(true);
        builder.Property(e => e.EnableSms).HasColumnName("enable_sms").HasDefaultValue(false);
        builder.Property(e => e.NotifyDriver).HasColumnName("notify_driver").HasDefaultValue(true);
        builder.Property(e => e.NotifySupervisor).HasColumnName("notify_supervisor").HasDefaultValue(true);
        builder.Property(e => e.NotifyFleetManager).HasColumnName("notify_fleet_manager").HasDefaultValue(true);
        builder.Property(e => e.AdditionalEmails).HasColumnName("additional_emails").HasColumnType("text[]");
        builder.Property(e => e.AdditionalPhones).HasColumnName("additional_phones").HasColumnType("text[]");
        builder.Property(e => e.ReminderFrequencyDays).HasColumnName("reminder_frequency_days").HasDefaultValue(7);
        builder.Property(e => e.MaxReminders).HasColumnName("max_reminders").HasDefaultValue(3);
        builder.Property(e => e.QuietHoursStart).HasColumnName("quiet_hours_start");
        builder.Property(e => e.QuietHoursEnd).HasColumnName("quiet_hours_end");

        builder.HasIndex(e => e.CompanyId).IsUnique().HasDatabaseName("ix_maint_alert_settings_company");
    }
}
