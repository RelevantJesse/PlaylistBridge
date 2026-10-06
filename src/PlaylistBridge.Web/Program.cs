using PlaylistBridge.Core;
using PlaylistBridge.Web;
using PlaylistBridge.Web.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection().SetApplicationName("PlaylistBridge");
builder.Services.AddSingleton<EncryptedConnectionStore>();
builder.Services.AddSingleton<SpotifyOAuthSessionStore>();
builder.Services.AddScoped<TrackMatcher>();
builder.Services.AddScoped<PlaylistTransferService>();
builder.Services.AddScoped<ServiceCredentialStore>();
builder.Services.AddScoped<PlaylistTransferFacade>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapSpotifyOAuth();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
