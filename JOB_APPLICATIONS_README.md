# Job Application System

This document describes the job application system for Blue Bridge Volunteer Management.

## Overview

The job application system allows:
- **Admins/Schedulers** to create job postings for crews
- **Volunteers** to browse and apply for available jobs
- **Admins/Schedulers** to review and approve/reject applications
- **Automated email notifications** when applications are accepted with ticket purchase links

## Features

### 1. Job Management
- Create jobs for specific crews with title, description, and number of positions
- Track filled vs. available positions
- Activate/deactivate jobs
- Edit job details

### 2. Application Workflow
- **Public View**: Anyone can browse available jobs
- **Authentication Required**: Users must sign in or create an account to apply
- **Application Status**: Pending → Accepted/Rejected
- **Email Notifications**: Automatic email sent when application is accepted
- **Ticket Links**: Include ticket purchase link in acceptance email

### 3. Admin Dashboard
- View all pending, accepted, and rejected applications
- Filter by status tabs
- One-click approve/reject actions
- Add ticket links when approving
- Scheduler permissions: Only see applications for their managed crews

## Database Schema

### CrewJobs Table
- `Id` - Primary key
- `CrewContentId` - Umbraco content ID for the crew page
- `CrewKey` - Umbraco content GUID
- `Title` - Job title
- `Description` - Job description (HTML)
- `TotalPositions` - Total number of positions available
- `FilledPositions` - Number of positions filled
- `IsActive` - Whether job is accepting applications
- `CreatedDate` / `UpdatedDate` - Timestamps

### JobApplications Table
- `Id` - Primary key
- `CrewJobId` - Foreign key to CrewJobs
- `MemberId` / `MemberKey` - Umbraco member ID and GUID
- `MemberEmail` / `MemberName` - Cached member info
- `Status` - Enum: Pending(0), Accepted(1), Rejected(2), Withdrawn(3)
- `ApplicationMessage` - Optional message from applicant
- `SubmittedDate` - When application was submitted
- `ReviewedDate` / `ReviewedByMemberId` - Review info
- `TicketLink` - Link to ticket purchase (optional)
- `AdminNotes` - Internal notes (optional)

## Architecture

### Services

**IJobService / JobService** (`Code/Services/JobService.cs`)
- `CreateJobAsync()` - Create a new job
- `GetAvailableJobsAsync()` - Get all active jobs with available positions
- `SubmitApplicationAsync()` - Submit a job application
- `ReviewApplicationAsync()` - Approve/reject application
- `GetApplicationsForReviewAsync()` - Get applications for admin/scheduler
- Plus additional management methods

### Controllers

**JobApplicationSurfaceController** (`Web/Controllers/JobApplicationSurfaceController.cs`)
- Frontend controller for job applications
- `SubmitApplication()` - POST endpoint for submitting applications
- `WithdrawApplication()` - POST endpoint for withdrawing applications

**JobApplicationBackofficeController** (`Web/Controllers/JobApplicationBackofficeController.cs`)
- Backoffice API controller for admin operations
- `/umbraco/backoffice/JobApplications/JobApplicationBackoffice/*`
- CRUD operations for jobs and applications
- Requires BackOffice authentication

### Views

**AvailableJobs.cshtml** (`Web/Views/AvailableJobs.cshtml`)
- Public-facing page listing all available jobs
- Shows job details, crew information, available positions
- Application form modal
- Login prompt for unauthenticated users
- Application status indicators for logged-in users

### Umbraco Dashboard

**Job Applications Dashboard** (`Web/App_Plugins/JobApplications/`)
- Located in Members section of Umbraco backoffice
- Three tabs: Pending, Accepted, Rejected
- Statistics cards showing application counts
- Click to select application, then approve/reject
- Prompts for ticket link when approving
- Real-time updates after actions

## Setup Instructions

### 1. Database Migration

The database tables are automatically created on first run using EF Core migrations:

```bash
# The migration happens automatically when the application starts
# See Program.cs lines 40-45
```

### 2. Create Umbraco Content Type (Required)

You need to create a content type in Umbraco backoffice for the Available Jobs page:

1. Go to Umbraco backoffice → Settings → Document Types
2. Create new document type: `BbvAvailableJobs`
3. Properties:
   - No special properties needed (uses the view directly)
4. Template: `AvailableJobs`

### 3. Create Content Page

1. Go to Content section
2. Create a new page using `BbvAvailableJobs` content type
3. Name it "Ledige Stillinger" or "Available Jobs"
4. Publish the page
5. Note the URL (e.g., `/available-jobs`)

### 4. Create Jobs via Backoffice API

Jobs must be created through the backoffice API or directly in the database:

**Option A: Using Backoffice Dashboard (Recommended)**
1. Navigate to Members section → Job Applications dashboard
2. Use the API endpoints to create jobs programmatically

**Option B: Direct API Call**
```javascript
POST /umbraco/backoffice/JobApplications/JobApplicationBackoffice/CreateJob
Authorization: Bearer {token}
Content-Type: application/json

{
  "crewContentId": 1234,
  "crewKey": "a1b2c3d4-...",
  "title": "Bar Assistant",
  "description": "<p>Help serve drinks at the festival bar</p>",
  "totalPositions": 10
}
```

**Option C: Create a Job Management UI**
You can extend the dashboard to include a job creation form, or create a separate dashboard for job management.

### 5. Testing the Workflow

1. **Create a job** (via API or dashboard extension)
2. **Browse jobs** - Visit `/available-jobs` as a guest
3. **Apply for job** - Sign in and click "Ansøg nu"
4. **Review application** - Go to Members → Job Applications dashboard
5. **Approve application** - Click on application, enter ticket link, click "Godkend"
6. **Check email logs** - Application acceptance email will be logged to console

## Email Configuration

Currently using **MOCK email service** (`MemberEmailService.cs`). Emails are logged to console.

### To Enable Real Emails:

1. Choose an email provider (SMTP, SendGrid, etc.)
2. Update `MemberEmailService.cs` implementation
3. Add configuration to `appsettings.json`:
```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.example.com",
    "SmtpPort": 587,
    "FromEmail": "noreply@bluebridge.dk",
    "FromName": "Blue Bridge Festival"
  }
}
```

### Email Template for Accepted Applications

When an application is accepted, the following email is sent:

**Subject**: Din ansøgning til {JobTitle} hos {CrewName} er godkendt!

**Body**:
```
Tillykke {MemberName}!

Din ansøgning til stillingen "{JobTitle}" hos {CrewName} er blevet godkendt.

Klik på linket nedenfor for at købe din billet:
{TicketLink}

Vi glæder os til at se dig på Blue Bridge Festival 2026!

Med venlig hilsen,
Blue Bridge Festival Team
```

## Permissions

### Volunteers (Frivillige)
- Browse all available jobs (no authentication required)
- Apply for jobs (authentication required)
- View their own applications
- Withdraw pending applications

### Schedulers (Vagtplanlæggere)
- Review applications for crews they manage
- Approve/reject applications for their crews
- View statistics for their crews

### Admins
- Full access to all features
- Review all applications across all crews
- Create, edit, and delete jobs
- View global statistics

## API Endpoints

### Frontend (Surface Controller)
- `POST /umbraco/surface/JobApplicationSurface/SubmitApplication`
- `POST /umbraco/surface/JobApplicationSurface/WithdrawApplication`

### Backoffice (API Controller)
All require BackOffice authentication:

**Applications**:
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetApplicationsForReview`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetApplicationDetail?applicationId={id}`
- `POST /umbraco/backoffice/JobApplications/JobApplicationBackoffice/ReviewApplication`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetApplicationsForJob?jobId={id}`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetApplicationsForCrew?crewContentId={id}`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetPendingCount`

**Jobs**:
- `POST /umbraco/backoffice/JobApplications/JobApplicationBackoffice/CreateJob`
- `POST /umbraco/backoffice/JobApplications/JobApplicationBackoffice/UpdateJob`
- `POST /umbraco/backoffice/JobApplications/JobApplicationBackoffice/DeleteJob`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetJobsForCrew?crewContentId={id}`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetJobDetail?jobId={id}`
- `GET /umbraco/backoffice/JobApplications/JobApplicationBackoffice/GetStatistics`

## Future Enhancements

### Recommended Additions:
1. **Job Creation UI in Dashboard** - Add a form to create/edit jobs directly in the backoffice dashboard
2. **Application Notifications** - Badge showing pending application count in backoffice
3. **Email Templates** - HTML email templates with branding
4. **Application History** - Audit log of all status changes
5. **Bulk Actions** - Accept/reject multiple applications at once
6. **Advanced Filtering** - Filter applications by crew, date range, etc.
7. **Export to CSV** - Export applications for reporting
8. **Application Deadlines** - Set closing dates for job applications
9. **Auto-Assignment** - Automatically assign member to crew when application is accepted
10. **Member Profiles in Dashboard** - View applicant details without leaving the dashboard

## Troubleshooting

### Database Not Created
- Check connection string in `appsettings.json`
- Ensure SQL Server is running
- Check Web project console for migration errors

### Applications Not Showing
- Verify jobs exist in `CrewJobs` table
- Check `IsActive` flag is true
- Verify `FilledPositions < TotalPositions`

### Email Not Sending
- Remember: Currently using mock service (emails logged to console)
- To send real emails, implement `IMemberEmailService` with actual SMTP

### Dashboard Not Loading
- Check browser console for JavaScript errors
- Verify user is logged into Umbraco backoffice
- Check API endpoint responses in Network tab

### Permissions Issues
- Schedulers only see their crews - verify `scheduleSupervisor` property on crew pages
- Admins need to be in the `admin` member group (GUID: `99e1edbb-8181-421d-a74b-e66a2f1e1148`)

## Technical Notes

### Why Two Projects (Code + Web)?
- **Code**: Class library for business logic, entities, services (portable, testable)
- **Web**: Umbraco web application (controllers, views, startup)

### Database Context Lifecycle
- **Scoped**: DbContext is scoped per request
- **Migration**: Automatic on application startup
- **Connection**: Uses same connection as Umbraco (`umbracoDbDSN`)

### Application Status Enum
```csharp
public enum ApplicationStatus
{
    Pending = 0,    // Default when submitted
    Accepted = 1,   // Approved by admin/scheduler
    Rejected = 2,   // Declined by admin/scheduler
    Withdrawn = 3   // Cancelled by applicant
}
```

### Security Considerations
- All backoffice endpoints require authentication
- Member applications only visible to application owner and admins/schedulers
- Ticket links should use secure tokens (not implemented - recommendation)
- Consider rate limiting on application submissions

## Support

For questions or issues:
1. Check this documentation
2. Review code comments in service implementations
3. Check Umbraco logs in `/umbraco/Logs/`
4. Review browser console for frontend errors
5. Check SQL Server for database issues
