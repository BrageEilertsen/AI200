using Ai200Trainer.Components;
using Ai200Trainer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The bank is shared and read-only at runtime; progress is a single local file.
builder.Services.AddSingleton<QuestionBank>();
builder.Services.AddSingleton<ProgressStore>();

// One run in flight per browser circuit.
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
