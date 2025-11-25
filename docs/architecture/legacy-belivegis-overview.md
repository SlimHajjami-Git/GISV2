# Legacy BeliveGis System – Functional & Technical Overview

This document summarises how the original BeliveGis platform is structured (projects, classes, main functions) so that GIS_V2 can faithfully reproduce or improve each capability.

## Solution structure (Visual Studio solution `BeliveGis.sln`)
| Project | Role | Key components |
|---------|------|----------------|
| `BeliveGis` | ASP.NET WebForms application used by end-users | Pages (`*.aspx` + code behind), resources (JS/CSS), Ajax controllers |
| `Belive.GPRSListener` | Windows service handling TCP/UDP ingestion from GPS devices | `TcpServer`, `UdpServer_*`, `GPSData`, logging |
| `BeliveGisMng.Objects` | Business objects & helper classes | `TcpOperate`, `CommonOperater`, domain POCOs (`Car`, `Vehicle`, `User`, etc.) |
| `BeliveGisMng.Data` | Data access helpers | Providers for SQL/EF operations |
| `BeliveGisMng.ApplicationBlocks` | Utility blocks (SQLHelper, logging) | ADO.NET helpers |
| `GisMngService` / `GisMngServiceSetup` | Auxiliary Windows services + installer | Threads for mileage/alerts |
| `BeliveGis.Packer` | Packaging/deployment utility | Build scripts |

## Web application (`BeliveGis`)
### Global flow
1. **Login (`LoginFrm.aspx.cs`)**
   - `btnLogin_Click`: validates CAPTCHA, checks credentials via `CommonOperater.CheckPasswordBySqlServer`, ensures contract/regulation compliance (table `Reglement`).
   - Initializes `Session` values (UserName, ClientID, Rank, ParentUser, permissions) and records login in `ConnectionHistory` (with `LogoutTime = 1900-01-01`).

2. **Main frame (`MainFrameFrm.aspx`)**
   - Hosts navigation menu and enforces `CheckSession()` so every page validates user context before executing queries or Ajax methods.

3. **Real-time monitoring (`RealMonFrm.aspx`)**
   - JavaScript polls Ajax endpoints to retrieve vehicles and their last positions. Uses `AjaxFrm.aspx.cs.GetAllMonVehData` which wraps stored procedures `SelectLastRecNew` or `SelectLastRecByEmail` depending on filter.

4. **Vehicle tracking & playback (`VehTrackingFrm.aspx`, `TracePlayback.aspx`)**
   - Provide historical path playback. Server-side queries `DynLastData` for time ranges and returns simplified track segments.

5. **Management pages**
   - `VehManage.aspx`: CRUD on vehicles via `BBLGisReports.VehiculeManageList` and stored procedures (e.g., `Action=SaveVehicle`).
   - `UserManage.aspx`: handles user CRUD, referencing `tbl_UserDepartment` for department permissions.
   - `ClientManage.aspx`, `AlerteManage.aspx`, `GeoFencing.aspx`, `FencingZone.aspx`, `MarqueursFrm.aspx` provide other admin capabilities (POIs, alerts, geofences).

6. **Reporting pages**
   - `KilometrageFrm.aspx`, `ExcesVitesseJournalier.aspx`, `InfractionFrm.aspx`, `ConnectionHistory.aspx`, `VehiculeStartStopReporting.aspx` rely on precomputed tables populated by background services.

### Ajax controller (`AjaxFrm.aspx.cs`)
Centralizes methods annotated with `[AjaxMethod]`, consumed by ExtJS/Coolite front-end components.
Key methods:
- `GetAddrByLngLat`, `GetAddress`: reverse geocoding through `TcpOperate.GetAddrByLngLat` or Bing Maps API.
- `GetAllMonVehData`, `LoadVehList`, `GetSpecialVehLastData`, `GetVehDetailsClick`: call stored procedures to fetch vehicle positions/details.
- `GetIoDefine`, `GetIoDefineByVehID`: fetch IO configuration for devices/users.
- `LoadCategoriesList`, `LoadCategorieMarkersList`: manage POI categories and markers.
- `GetTourneeData`, `GetVehiculeArrets`: (newer feature) compute commercial/technician itineraries and stops using `DynLastData` analytics.

### JavaScript front-end (`BeliveGis/Source/release/*.js`)
- `mainFrm.js` & related files orchestrate UI components, map rendering (Bing/Google), event handlers for selecting vehicles, sending commands, color coding statuses, etc.

## Business objects & helpers (`BeliveGisMng.Objects`)
### TcpOperate
- Handles network communication with trackers for commands and reverse geocoding.
- Methods:
  - `SendCommand(DevID, Cmd, bImgCapture, UserName)`: chooses UDP port based on `DevID` (fifth char) and sends command packets (START/STOP engine, etc.), handles protocol-specific ACK responses (`StructGISCommandReq`, `GISCommandResp`).
  - `GetAddrByLngLat`, `GetRegion`: contact remote geocoding service over TCP to resolve addresses.
- Contains structures describing protocol frames (NR024, AAP ack, base location responses).

### CommonOperater
- Utility methods used everywhere: `isNumber`, `GetXYOffsetGoogleAddrNew`, `CheckPasswordBySqlServer`, date/time conversions, etc.

### Domain models
- `Car`, `Vehicle`, `User`, `Client`, `Categorie`, `Marqueur` – simple DTOs shared between UI and services.

## Windows services
### Belive.GPRSListener
- Starts TCP server (port 6908) and UDP servers (ports 6903/6904 + device-specific 6800-6806).
- Key classes:
  - `TcpServer`: async accept loop, tracks `ConnectionState` per device.
  - `EchoServiceProvider`: handles `OnAcceptConnection`, `OnReceiveData` events, logging to `GPRSInformation.log` / `GPRSException.log`.
  - `GPSData`: core parser.
    - `SaveTcpData(Message)`: spawn thread to parse NMEA or binary frames.
    - `ExtractData`: decode `GPGGA`, `GPRMC`, `GPGLL`, or NR024 structures.
    - `UpdateGpsData`: saves to DB using stored procedure `SaveDynData` (table `DynLastData`).
    - `GetDevIdByIMEI`: resolves device IDs.
- Threads for additional processing:
  - `GisMngSpeedMileageThread`: computes speeds/kilometres for reporting.
  - `GisActivitesThread`, `AlertesThread`, `AlertesEmailsThread`: handle analytics and notifications.

### GisMngService
- Background Windows service for maintenance tasks (alerts evaluation, start/stop reports, etc.). Uses same data access helpers as web app.

## Data layer & database
- Mixed approach: ADO.NET with `SqlConnection`/`SqlCommand` + `Entity Framework` (`BeliveGisMngEntities` from `BeliveGisMngModel.edmx`).
- Connection string stored in `Web.config` (`ConfigurationManager.AppSettings["conn"]`).
- Common stored procedures:
  - `SelectLastRecNew`, `SelectLastRecByEmail`, `SelectLastRecByVehID` – retrieve latest telemetry.
  - `WebGisSelectVehByUSer`, `SelectDefineIOByUser`, `SelectDefineIOByVehID` – configuration lookups.
  - `SelectImageDev`, `WebGisSelectCategoriesByUSer`, `WebGisSelectMarkersByCategorie` – UI support.
  - `SaveDynData` – persists telemetry data from listeners.
- Tables overview (see `SQL/` scripts & EF model): `Vehicle`, `tbl_user`, `tbl_UserDepartment`, `Client`, `DynLastData`, `Gis_GPS`, `ConnectionHistory`, `SendCmdLog`, `Images`, `Reglement`, `Categorie`, `Marqueur`.

## Key workflows
1. **Login & session management**
   - `CheckSession()` appears in most pages. Invalid sessions redirect to `LoginFrm.aspx`.
   - `Session_End` in `Global.asax` updates `ConnectionHistory.LogoutTime`.

2. **Telemetry ingestion to UI**
   - Tracker → `Belive.GPRSListener` → `SaveDynData` → `DynLastData` → Ajax `GetAllMonVehData` → JavaScript map.

3. **Command dispatch**
   - UI button (e.g., cut engine) → Ajax → `TcpOperate.SendCommand` → UDP port to tracker → response logged (`SendCmdLog`).

4. **Reporting**
   - Background threads compute aggregates (kilometrage, events) and store in dedicated tables consumed by WebForms pages or Telerik reports.

5. **Commercial/Technician tour tracking** (newer feature `SuiviTournee.aspx`)
   - Uses `TourneeModel`, `EtapeTourneeModel` (`Models/TourneeModel.cs`).
   - Ajax methods `GetTourneeData`, `GetVehiculeArrets` analyze historical GPS data, detect stops (>5 min), color-code schedule adherence.

## Takeaways for GIS_V2
- The legacy system couples UI, business logic, and data access within WebForms and Windows services.
- Real-time telemetry flow, command operations, and reporting logic must be preserved but reimplemented with modern architectures (microservices, Rust ingestion, CQRS.NET, Angular).
- Tables like `DynLastData`, `ConnectionHistory`, `Vehicle`, etc., inform the new normalized schema (trips, stops, fuel, telemetry).
- Custom protocol handling (NR024, AAP, GT60, TK102/103) currently resides in `Belive.GPRSListener` & `TcpOperate` and must be ported to the Rust ingestion layer.
