using Ai200Trainer.Components;
using Ai200Trainer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Every exam under Data/ is loaded once and shared: read-only at runtime.
builder.Services.AddSingleton<ExamCatalog>();

// Per visitor, scoped to the Blazor circuit: which exam they are studying, their progress
// for each exam, and the run currently in flight. ProgressStore persists to the browser's
// localStorage rather than the server — see its class comment for why that matters on a
// publicly reachable deployment.
builder.Services.AddScoped<ExamContext>();
builder.Services.AddScoped<ProgressStore>();
builder.Services.AddScoped<SessionHost>();

var app = builder.Build();

app.Services.GetRequiredService<ExamCatalog>().Load();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
