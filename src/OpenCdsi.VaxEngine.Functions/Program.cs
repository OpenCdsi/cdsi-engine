/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.ReferenceData;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// FunctionsApplication.CreateBuilder(args) + ConfigureFunctionsWebApplication() is the current
// Microsoft-documented pattern for isolated-worker functions with ASP.NET Core HTTP
// integration (confirmed against Microsoft Learn's own guide, not just a blog post) - it
// mirrors WebApplication.CreateBuilder(args)'s own shape deliberately, the same style already
// used in OpenCdsi.VaxEngine.Api.
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Same data-loading design as OpenCdsi.VaxEngine.Api: CDSI_DATA_PATH wins if set (the real deployed/Docker
// scenario), otherwise walk up from the executable's location looking for OpenCdsi.VaxEngine.sln (the same
// FindDataDirectory pattern already proven in OpenCdsi.VaxEngine.Demo and OpenCdsi.VaxEngine.Api) for local development
// with no environment variable needed at all.
var dataRoot = builder.Configuration["CDSI_DATA_PATH"] ?? FindDataDirectory();
builder.Services.AddSingleton(_ =>
{
    var antigensPath = Path.Combine(dataRoot, "antigens");
    var schedulePath = Path.Combine(dataRoot, "schedule", "ScheduleSupportingData.xml");
    return ReferenceDataRepository.Load(antigensPath, schedulePath);
});

var app = builder.Build();

// UNLIKE OpenCdsi.VaxEngine.Api's own Program.cs, this deliberately does NOT resolve ReferenceDataRepository
// eagerly here before Run() - the isolated-worker Functions host's startup lifecycle isn't
// something this sandbox could verify behaves identically to WebApplication's own Build()/
// pre-Run() service resolution, and getting that wrong could break startup entirely rather than
// just delay when a real error surfaces. Data loads lazily on the first real request instead -
// a real, deliberate difference from OpenCdsi.VaxEngine.Api, not an oversight, flagged here so it isn't
// mistaken for one.
app.Run();

static string FindDataDirectory()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCdsi.VaxEngine.sln")))
    {
        dir = dir.Parent;
    }
    if (dir is null)
    {
        throw new InvalidOperationException(
            "Couldn't find the repo root (OpenCdsi.VaxEngine.sln) walking up from the executable's directory, " +
            "and CDSI_DATA_PATH was not set. Set the CDSI_DATA_PATH environment variable " +
            "(in local.settings.json for local development), or run this from within the " +
            "cdsi-engine checkout.");
    }
    return Path.Combine(dir.FullName, "data");
}
