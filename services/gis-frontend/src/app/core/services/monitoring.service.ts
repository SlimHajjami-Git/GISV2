import { Injectable } from '@angular/core';
import { SupabaseService } from './supabase.service';
import { Trip, Stop, VehicleGroup, Geofence, GeofenceEvent, ReportTemplate, GeneratedReport } from '../models/monitoring.types';

@Injectable({
  providedIn: 'root'
})
export class MonitoringService {
  constructor(private supabase: SupabaseService) {}

  async getTrips(companyId: string, startDate?: string, endDate?: string) {
    let query = this.supabase
      .from('trips')
      .select(`
        *,
        vehicle:vehicles(id, registration_number, brand, model),
        stops:stops(*)
      `)
      .eq('vehicle.company_id', companyId)
      .order('start_time', { ascending: false });

    if (startDate) query = query.gte('start_time', startDate);
    if (endDate) query = query.lte('start_time', endDate);

    const { data, error } = await query;
    if (error) throw error;
    return data as Trip[];
  }

  async getTripById(tripId: string) {
    const { data, error } = await this.supabase
      .from('trips')
      .select(`
        *,
        vehicle:vehicles(*),
        stops:stops(*)
      `)
      .eq('id', tripId)
      .single();

    if (error) throw error;
    return data as Trip;
  }

  async getStops(tripId: string) {
    const { data, error } = await this.supabase
      .from('stops')
      .select('*')
      .eq('trip_id', tripId)
      .order('start_time', { ascending: true });

    if (error) throw error;
    return data as Stop[];
  }

  async getVehicleGroups(companyId: string) {
    const { data, error } = await this.supabase
      .from('vehicle_groups')
      .select(`
        *,
        members:vehicle_group_members(
          *,
          vehicle:vehicles(*)
        )
      `)
      .eq('company_id', companyId)
      .order('name');

    if (error) throw error;
    return data as VehicleGroup[];
  }

  async createVehicleGroup(group: Partial<VehicleGroup>) {
    const { data, error } = await this.supabase
      .from('vehicle_groups')
      .insert(group)
      .select()
      .single();

    if (error) throw error;
    return data;
  }

  async addVehicleToGroup(groupId: string, vehicleId: string) {
    const { data, error } = await this.supabase
      .from('vehicle_group_members')
      .insert({ group_id: groupId, vehicle_id: vehicleId })
      .select()
      .single();

    if (error) throw error;
    return data;
  }

  async removeVehicleFromGroup(groupId: string, vehicleId: string) {
    const { error } = await this.supabase
      .from('vehicle_group_members')
      .delete()
      .eq('group_id', groupId)
      .eq('vehicle_id', vehicleId);

    if (error) throw error;
  }

  async getGeofences(companyId: string) {
    const { data, error } = await this.supabase
      .from('geofences')
      .select('*')
      .eq('company_id', companyId)
      .order('name');

    if (error) throw error;
    return data as Geofence[];
  }

  async createGeofence(geofence: Partial<Geofence>) {
    const { data, error } = await this.supabase
      .from('geofences')
      .insert(geofence)
      .select()
      .single();

    if (error) throw error;
    return data;
  }

  async updateGeofence(id: string, updates: Partial<Geofence>) {
    const { data, error } = await this.supabase
      .from('geofences')
      .update(updates)
      .eq('id', id)
      .select()
      .single();

    if (error) throw error;
    return data;
  }

  async deleteGeofence(id: string) {
    const { error } = await this.supabase
      .from('geofences')
      .delete()
      .eq('id', id);

    if (error) throw error;
  }

  async getGeofenceEvents(companyId: string, startDate?: string, endDate?: string) {
    let query = this.supabase
      .from('geofence_events')
      .select(`
        *,
        vehicle:vehicles(*),
        geofence:geofences(*)
      `)
      .eq('vehicle.company_id', companyId)
      .order('timestamp', { ascending: false });

    if (startDate) query = query.gte('timestamp', startDate);
    if (endDate) query = query.lte('timestamp', endDate);

    const { data, error } = await query;
    if (error) throw error;
    return data as GeofenceEvent[];
  }

  async getReportTemplates(companyId: string) {
    const { data, error } = await this.supabase
      .from('report_templates')
      .select('*')
      .eq('company_id', companyId)
      .order('name');

    if (error) throw error;
    return data as ReportTemplate[];
  }

  async generateReport(
    companyId: string,
    templateId: string,
    name: string,
    periodStart: string,
    periodEnd: string
  ) {
    const { data, error } = await this.supabase
      .from('generated_reports')
      .insert({
        company_id: companyId,
        template_id: templateId,
        name,
        period_start: periodStart,
        period_end: periodEnd,
        status: 'generating'
      })
      .select()
      .single();

    if (error) throw error;
    return data;
  }

  async getGeneratedReports(companyId: string) {
    const { data, error } = await this.supabase
      .from('generated_reports')
      .select(`
        *,
        template:report_templates(*)
      `)
      .eq('company_id', companyId)
      .order('created_at', { ascending: false });

    if (error) throw error;
    return data as GeneratedReport[];
  }

  async getGeneratedReport(reportId: string) {
    const { data, error } = await this.supabase
      .from('generated_reports')
      .select(`
        *,
        template:report_templates(*)
      `)
      .eq('id', reportId)
      .single();

    if (error) throw error;
    return data as GeneratedReport;
  }
}
