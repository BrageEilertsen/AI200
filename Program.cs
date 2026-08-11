using Ai200Trainer.Components;
using Ai200Trainer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The bank is shared and read-only at runtime, so one instance serves every visitor.
builder.Services.AddSingleton<QuestionBank>();

// Progress and the run in flight are per visitor: scoped to the Blazor circuit.
// ProgressStore persists to the browser's localStorage, not to the server — see the
// class comment for why that matters on a publicly reachable deployment.
builder.Services.AddScoped<ProgressStore>();
builder.Services.AddScoped<SessionHost>();

var app = builder.Build();

app.Services.GetRequiredService<QuestionBank>().Load();

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
