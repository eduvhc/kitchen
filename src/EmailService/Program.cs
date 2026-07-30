using EmailService.Features.Dispatch;
using EmailService.Features.Emails;
using EmailService.Features.Templates;
using EmailService.Options;
using EmailService.Persistence;
using EmailService.Templating;
using EmailService.Transport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceOptions(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScribanTemplating();
builder.Services.AddSmtpTransport();

builder.Services.AddEmails();
builder.Services.AddTemplates();
builder.Services.AddDispatch();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<EmailDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapEmails();
app.MapTemplates();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigrateAsync();
}

app.Run();

public partial class Program;
