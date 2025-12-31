using Code.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register custom services
builder.Services.AddScoped<IMemberEmailService, MemberEmailService>();
builder.Services.AddScoped<IMemberAuthService, MemberAuthService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICrewService, CrewService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseHttpsRedirection();

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
