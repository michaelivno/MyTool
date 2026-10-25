using Cockpit.Client;
using Cockpit.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API lives on the same origin that serves the app (one container). No external calls anywhere.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<AuthSignal>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AppState>();
builder.Services.AddSingleton<ToastService>();

await builder.Build().RunAsync();
