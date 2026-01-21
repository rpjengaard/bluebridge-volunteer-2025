using Code.Services;
using Microsoft.Extensions.Options;

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

// Register email service based on provider configuration
builder.Services.AddScoped<IMemberEmailService>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<EmailSettings>>().Value;

    return settings.Provider?.ToLower() switch
    {
        "postmark" => new PostmarkEmailService(
            sp.GetRequiredService<ILogger<PostmarkEmailService>>(),
            sp.GetRequiredService<IOptions<EmailSettings>>()),
        _ => new SmtpEmailService(
            sp.GetRequiredService<ILogger<SmtpEmailService>>(),
            sp.GetRequiredService<IOptions<EmailSettings>>())
    };
});
builder.Services.AddScoped<IMemberAuthService, MemberAuthService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICrewService, CrewService>();
builder.Services.AddScoped<IMemberImpersonationService, MemberImpersonationService>();
builder.Services.AddScoped<IApplicationsService, ApplicationsService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

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
