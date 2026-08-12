using Code.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add session support for impersonation
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configure email settings
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Register custom services
builder.Services.AddScoped<IMemberEmailService, MemberEmailService>();
builder.Services.AddScoped<IMemberAuthService, MemberAuthService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
// [CHANGE: crew invitation feature] Related: Code/Services/CrewInvitationService.cs, Web/Controllers/CrewInvitationSurfaceController.cs
builder.Services.AddScoped<ICrewInvitationService, CrewInvitationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICrewService, CrewService>();
builder.Services.AddScoped<IMemberImpersonationService, MemberImpersonationService>();
builder.Services.AddScoped<IApplicationsService, ApplicationsService>();
builder.Services.AddScoped<ICrewMessageService, CrewMessageService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IMemberListService, MemberListService>();
// [CHANGE: Billetto ticket status dashboard] Related: Code/Services/BillettoTicketService.cs, Web/Controllers/BillettoTicketStatusController.cs, Web/App_Plugins/BillettoTicketStatus/*
builder.Services.AddHttpClient();
builder.Services.AddScoped<IBillettoTicketService, BillettoTicketService>();
// [CHANGE: Billetto sales dashboard] Related: Code/Services/BillettoSalesService.cs, Web/Controllers/BillettoSalesController.cs, Web/App_Plugins/BillettoSales/*
builder.Services.AddScoped<IBillettoSalesService, BillettoSalesService>();
// [CHANGE: SuperAdmin ticket sales page] Related: Code/Services/SuperAdminService.cs, Web/Controllers/BbvTicketSalesController.cs, Web/Controllers/TicketSalesApiController.cs
builder.Services.AddScoped<ISuperAdminService, SuperAdminService>();
builder.Services.AddSingleton<IEmailLogService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var logger = sp.GetRequiredService<ILogger<EmailLogService>>();
    var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
    return new EmailLogService(dataDir, logger);
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

// Trust X-Forwarded-For / X-Forwarded-Proto from the reverse proxy so that
// HTTPS is detected correctly and antiforgery token validation doesn't fail.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

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
