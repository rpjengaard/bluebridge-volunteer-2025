# Blue Bridge Volunteer Management System

A volunteer ("frivillig") management system built with **Umbraco CMS v17.0.2** on **.NET 10.0**. The application manages volunteer information including crew assignments, personal details, and work history for the Blue Bridge organization.

## Features

- **Volunteer Management**: Track volunteer information including personal details, contact info, and work history
- **Crew System**: Organize volunteers into crews with dedicated crew pages
- **Member Authentication**: Login, signup, password reset functionality
- **CSV Import**: Bulk import volunteers from CSV files via backoffice dashboard
- **Member Invitation System**: Send email invitations to new volunteers
- **Dashboard**: Personalized dashboard for logged-in volunteers
- **Member Export API**: API key-protected JSON endpoint for extracting member data to external systems

## Tech Stack

- **CMS**: Umbraco 17.0.2
- **Framework**: .NET 10.0
- **Database**: SQL Server
- **Architecture**: Two-project structure (Web host + Code class library)

## Project Structure

```
Blue Bridge Voluntier/
├── Web/                          # Main Umbraco web application
│   ├── App_Plugins/              # Custom backoffice dashboards
│   │   ├── MemberImporter/       # CSV import dashboard
│   │   └── MemberInvitation/     # Invitation management dashboard
│   ├── Controllers/              # Surface controllers
│   ├── ViewModels/               # View models for forms
│   ├── Views/                    # Razor views
│   │   ├── Partials/             # Partial views (blockgrid, blocklist)
│   │   └── Shared/               # Layout templates
│   ├── wwwroot/                  # Static files
│   └── umbraco/models/           # Auto-generated content models
│
├── Code/                         # Shared class library
│   └── Services/                 # Business logic services
│       ├── CrewService           # Crew management
│       ├── DashboardService      # Dashboard data
│       ├── InvitationService     # Member invitations
│       ├── MemberAuthService     # Authentication logic
│       └── MemberEmailService    # Email functionality
│
└── Blue Bridge Voluntier.slnx    # Solution file
```

## Prerequisites

- .NET 10.0 SDK
- SQL Server instance

## Getting Started

### 1. Configure Database

Update the connection string in `Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "umbracoDbDSN": "Server=YOUR_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True"
  }
}
```

### 2. Build the Solution

```bash
dotnet build Web/Web.csproj
```

### 3. Run the Application

```bash
dotnet run --project Web/Web.csproj
```

### 4. Access the Application

- **Frontend**: https://localhost:44331 (or configured port)
- **Backoffice**: https://localhost:44331/umbraco

## Content Types

### Document Types (bbv prefix)

| Type | Description |
|------|-------------|
| `BbvFrontpage` | Front page |
| `BbvDashboard` | User dashboard |
| `BbvCrewPage` | Individual crew page |
| `BbvCrewList` | Crew listing page |
| `BbvLoginPage` | Login page |
| `BbvSignUp` | Sign-up page |
| `BbvSiteSettings` | Global site settings |

### Member Type

**BbvMember** (Frivillig/Volunteer):
- `FirstName` (Fornavn)
- `LastName` (Efternavn)
- `Birthdate` (Fødselsdato)
- `Phone` (Telefon)
- `Crews` - Content picker for crew assignment
- `TidligereArbejdssteder` (Previous workplaces)

## CSV Import Format

The Member CSV Importer expects the following columns:

| Column | Description | Required |
|--------|-------------|----------|
| `Email` | Member email (used as username) | Yes |
| `Fornavn` | First name | No |
| `Efternavn` | Last name | No |
| `Telefon` | Phone number | No |
| `Arbejdssteder` | Previous workplaces | No |

## Member Export API

A JSON API for extracting member data to external systems (scripts, Excel/Power BI, integrations).

### Endpoint

```
GET /api/members/export
```

### Authentication

Requires an API key sent in the `X-Api-Key` header. The key is configured under `MemberExportApi:ApiKey`:

- **Development**: set in `appsettings.Development.json`
- **Production**: set via environment variable `MemberExportApi__ApiKey` (never commit the real key)

If no key is configured, the endpoint is disabled and returns `403`.

### Query Parameters

| Parameter | Description | Required |
|-----------|-------------|----------|
| `group` | Filter by member group (role) name, e.g. `Admin`. Exact match, case-insensitive. Omit to return all members. Unknown group returns an empty list. | No |

### Response

All members are returned in one response (no pagination). Canceled and non-accepted members are included and flagged so consumers can filter themselves.

```json
{
  "meta": {
    "count": 421,
    "statusCode": 200,
    "durationMs": 1114
  },
  "members": [
    {
      "firstName": "Anna",
      "lastName": "Jensen",
      "email": "anna@example.dk",
      "crews": ["Bar", "Scene"],
      "memberGroups": ["Frivillige"],
      "signupDate": "2026-02-16T17:31:59Z",
      "isCanceled": false,
      "accepted2026": true
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `firstName` / `lastName` | From the member's `firstName`/`lastName` properties |
| `email` | Member email |
| `crews` | Names of assigned crews |
| `memberGroups` | Member group (role) names |
| `signupDate` | `acceptedDate` property, falling back to the Umbraco record's create date (same logic as the `/members` page) |
| `isCanceled` | `true` if the member has canceled |
| `accepted2026` | `true` if the member has accepted for 2026 |

Error responses use the same envelope with an `error` message instead of `members`:

| Status | Meaning |
|--------|---------|
| `401` | Missing or invalid `X-Api-Key` |
| `403` | No API key configured (endpoint disabled) |

### Example

```bash
curl -H "X-Api-Key: <your-key>" "https://<host>/api/members/export?group=Frivillige"
```

**Implementation**: `Web/Controllers/MemberExportApiController.cs` + `IMemberListService.GetMemberExportAsync` in `Code/Services`.

## Development Notes

### ModelsBuilder

Umbraco uses ModelsBuilder to generate strongly-typed models. In development mode (`SourceCodeAuto`), models regenerate automatically when content types change.

**Do not manually edit files in `Web/umbraco/models/`** - they are auto-generated.

### Environment Configuration

- **Development**: Debug mode, auto-generated models, HTTPS disabled
- **Production**: HTTPS enforced, models pre-built, unattended upgrades enabled

## License

Proprietary - Blue Bridge Organization
