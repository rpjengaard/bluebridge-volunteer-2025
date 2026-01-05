using Code.Data;
using Code.Services;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add session support for impersonation
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configure database context for job applications
builder.Services.AddDbContext<JobApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("umbracoDbDSN");
    options.UseSqlServer(connectionString);
});

// Register custom services
builder.Services.AddScoped<IMemberEmailService, MemberEmailService>();
builder.Services.AddScoped<IMemberAuthService, MemberAuthService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICrewService, CrewService>();
builder.Services.AddScoped<IMemberImpersonationService, MemberImpersonationService>();
builder.Services.AddScoped<IApplicationsService, ApplicationsService>();
builder.Services.AddScoped<IJobService, JobService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<JobApplicationDbContext>();
    dbContext.Database.Migrate();
}

await app.BootUmbracoAsync();

app.UseHttpsRedirection();

// Enable session middleware
app.UseSession();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
