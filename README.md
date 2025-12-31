# Blue Bridge Volunteer Management System

A volunteer ("frivillig") management system built with **Umbraco CMS v17.0.2** on **.NET 10.0**. The application manages volunteer information including crew assignments, personal details, and work history for the Blue Bridge organization.

## Features

- **Volunteer Management**: Track volunteer information including personal details, contact info, and work history
- **Crew System**: Organize volunteers into crews with dedicated crew pages
- **Member Authentication**: Login, signup, password reset functionality
- **CSV Import**: Bulk import volunteers from CSV files via backoffice dashboard
- **Member Invitation System**: Send email invitations to new volunteers
- **Dashboard**: Personalized dashboard for logged-in volunteers

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

## Development Notes

### ModelsBuilder

Umbraco uses ModelsBuilder to generate strongly-typed models. In development mode (`SourceCodeAuto`), models regenerate automatically when content types change.

**Do not manually edit files in `Web/umbraco/models/`** - they are auto-generated.

### Environment Configuration

- **Development**: Debug mode, auto-generated models, HTTPS disabled
- **Production**: HTTPS enforced, models pre-built, unattended upgrades enabled

## License

Proprietary - Blue Bridge Organization
