using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using static System.Net.Mime.MediaTypeNames;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? Guid.NewGuid().ToString()
                ?? "fixed_key", 
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = Application.Json;
        context.HttpContext.Response.WriteAsJsonAsync(new
        {
            Error = "Too many requests"
        });

        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = Application.Json;

        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        var error = feature?.Error;

        if (error is ApplicationException)
        {
            await context.Response.WriteAsJsonAsync(new
            {
                Error = error.Message
            });
        }
        else
        {
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "Something went wrong. Please try again later."
            });
        }
    });
});

app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();