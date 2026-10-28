using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Mp4BoxKit.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#blazor-root");
await builder.Build().RunAsync();
