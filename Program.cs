using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Collections.Generic;
using System;
using core6.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using core6.Healthy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;

var builder = WebApplication.CreateBuilder(args);

// ====================================================================================================
//                                   Add services to DI container
// ----------------------------------------------------------------------------------------------------

var serviceName = Environment.GetEnvironmentVariable("PROJECT_NAME") ?? builder.Configuration.GetValue<string>("Otlp:ServiceName");
string serviceVersion = Environment.GetEnvironmentVariable("PROJECT_VERSION") ?? builder.Configuration.GetValue<string>("Otlp:ServiceVersion");

string JIRA_PROJECT_ID = Environment.GetEnvironmentVariable("JIRA_PROJECT_ID") ?? "1";
string IMAGE = Environment.GetEnvironmentVariable("IMAGE") ?? "localhost";
string TEMPLATE_NAME = Environment.GetEnvironmentVariable("TEMPLATE_NAME") ?? "dotnetcore6";
string STAGE = Environment.GetEnvironmentVariable("STAGE") ?? "production";
string TEAM_NAME = Environment.GetEnvironmentVariable("TEAM_NAME") ?? "web_backend"; // or TEAM_NAME=logic,web_front,devops,it,pm,po,mobile,qa,database,creep,...
string ContainerName = Dns.GetHostName();
string HOST_ID = Environment.GetEnvironmentVariable("HOST_ID") ?? "localhostId";
string HOST_NAME = Environment.GetEnvironmentVariable("HOST_NAME") ?? "localhost";
string SUBDOMAIN = Environment.GetEnvironmentVariable("SUBDOMAIN") ?? "localhost";
string HOST_TYPE = Environment.GetEnvironmentVariable("HOST_TYPE") ?? "arm64";
string OS_NAME = Environment.GetEnvironmentVariable("OS_NAME") ?? "windows";
string OS_VERSION = Environment.GetEnvironmentVariable("OS_VERSION") ?? "2010";
string CRM_KEY = Environment.GetEnvironmentVariable("CRM_KEY") ?? "HW-511";
string SERVICE_NAMESPACE = Environment.GetEnvironmentVariable("SERVICE_NAMESPACE") ?? "devops";

Action<ResourceBuilder> configureResource = r =>
{
    r.AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName);
    //r.AddService("Redis", serviceVersion: "1.0.0", serviceInstanceId: Environment.MachineName);
    r.AddAttributes(new Dictionary<string, object>
    {
        ["environment.name"] = STAGE,
        ["deployment.environment"] = STAGE, // staging
        ["team.name"] = TEAM_NAME,
        ["team.user"] = Environment.UserName,
        ["host.id"] = HOST_ID,
        ["host.name"] = HOST_NAME,
        ["host.hostname"] = SUBDOMAIN,
        ["host.type"] = HOST_TYPE,
        ["os.name"] = OS_NAME,
        ["os.version"] = OS_VERSION,
        ["issue.project.id"] = JIRA_PROJECT_ID,
        ["issue.crm.key"] = CRM_KEY,
        ["service.namespace"] = SERVICE_NAMESPACE,
        ["telemetry.sdk.language"] = "dotnet",
        ["telemetry.sdk.name"] = "opentelemetry",
        ["container.runtime"] = "docker",
        ["container.name"] = ContainerName,
        ["container.image.name"] = IMAGE,
        ["container.image.tag"] = serviceVersion,
        ["service.template"] = TEMPLATE_NAME
    });
};

// -----------------------------------------------
//                     SQL SERVER
// -----------------------------------------------

builder.Services.AddTransient<ISqlRepository, SqlRepository>();
builder.Services.AddTransient<IRabbitRepository, RabbitRepository>();
// Add services to the container.
builder.Services.AddControllers().AddNewtonsoftJson();

// -----------------------------------------------
//                     TRACE
// -----------------------------------------------

var tracingExporter = builder.Configuration.GetValue<string>("UseTracingExporter").ToLowerInvariant();
builder.Services.AddHttpClient();

builder.Services.AddOpenTelemetryTracing(options =>
{
    options
        //.AddConsoleExporter()
        .ConfigureResource(configureResource)
        .AddSource(nameof(RabbitRepository))
        .AddSource(nameof(ProxyNetwork))
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddSqlClientInstrumentation();

    switch (tracingExporter)
    {
        case "otlp":
            options.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("Otlp:Endpoint"));
            });
            break;

        default:
            options.AddConsoleExporter();
            break;
    }
});

// -----------------------------------------------
//                     LOG
// -----------------------------------------------
// For options which can be bound from IConfiguration.

builder.Services.Configure<AspNetCoreInstrumentationOptions>(builder.Configuration.GetSection("AspNetCoreInstrumentation"));

builder.Logging.ClearProviders();

builder.Logging.AddOpenTelemetry(options =>
{
    options.ConfigureResource(configureResource);

    // Switch between Console/OTLP by setting UseLogExporter in appsettings.json.
    var logExporter = builder.Configuration.GetValue<string>("UseLogExporter").ToLowerInvariant();
    switch (logExporter)
    {
        case "otlp":
            options.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("Otlp:Endpoint"));
                otlpOptions.Protocol = OtlpExportProtocol.Grpc;
            });
            break;
        default:
            options.AddConsoleExporter();
            break;
    }
});

builder.Services.Configure<OpenTelemetryLoggerOptions>(opt =>
{
    opt.IncludeScopes = true;
    opt.ParseStateValues = true;
    opt.IncludeFormattedMessage = true;
});

// -----------------------------------------------
//                     Metrics
// -----------------------------------------------
// Switch between Prometheus/OTLP/Console by setting UseMetricsExporter in appsettings.json.

var metricsExporter = builder.Configuration.GetValue<string>("UseMetricsExporter").ToLowerInvariant();

var meter = new Meter(serviceName);
builder.Services.AddOpenTelemetryMetrics(options =>
{
    options.ConfigureResource(configureResource)
        .AddMeter(meter.Name)
        .AddRuntimeInstrumentation()
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation();

    switch (metricsExporter)
    {
        case "otlp":
            options.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("Otlp:Endpoint"));
                otlpOptions.Protocol = OtlpExportProtocol.Grpc;
            });
            break;
        default:
            options.AddConsoleExporter();
            break;
    }
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// -----------------------------------------------
//                 Health Check
// -----------------------------------------------
// https://andrewlock.net/deploying-asp-net-core-applications-to-kubernetes-part-6-adding-health-checks-with-liveness-readiness-and-startup-probes/

// redis , elastic , sqlserver , rabbitmq , app1 , google (external network)
string rabbitConnectionString = $"amqp://{builder.Configuration["RabbitMq:Username"]}:{builder.Configuration["RabbitMq:Password"]}@{builder.Configuration["RabbitMq:Host"]}:5672/";
string sqlServerConnectionString = builder.Configuration["SqlDbConnString"]??"";
string redisConnectionString = $"{builder.Configuration["Redis:Host"]}:{builder.Configuration["Redis:Port"]}";
builder.Services
    .AddHealthChecks()
    .AddCheck<App1HealthCheck>("App1 check", tags: new[] { "companyService", "test", "app3", "app1" })
    .AddCheck<ExternalNetwork>("External Network check", tags: new[] { "companyService", "test", "app3", "Google" })
    .AddCheck<ProxyNetwork>("Proxy Network check", tags: new[] { "companyService", "test", "app3", "YouTube", "Twitter" })
    .AddSqlServer(connectionString: sqlServerConnectionString, tags: new[] { "otherService", "test" })
    .AddRedis(redisConnectionString: redisConnectionString, tags: new[] { "otherService", "test" })
    .AddApplicationInsightsPublisher()
    .AddRabbitMQ(rabbitConnectionString: rabbitConnectionString, tags: new[] { "otherService", "test" });

// -----------------------------------------------
//                   Swagger
// -----------------------------------------------

builder.Services.AddSwaggerGen();

var app = builder.Build();

var MyActivitySource = new ActivitySource(serviceName);
// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}

// -----------------------------------------------
//             Middleware Config
// -----------------------------------------------

app.UseDeveloperExceptionPage();
app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(config =>
{
    config.MapHealthChecks("/health/startup", new HealthCheckOptions
    {
        Predicate = _ => true,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
});
app.UseEndpoints(config =>
{
    config.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        Predicate = _ => true,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
});
app.UseEndpoints(config =>
{
    config.MapHealthChecks("/ready", new HealthCheckOptions
    {
        Predicate = _ => true,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
});

app.MapControllers();

/*app.MapGet("/health", () =>
{
    // Track work inside of the request
    using var activity = MyActivitySource.StartActivity("SayHello");
    activity?.SetTag("foo", 1);
    activity?.SetTag("bar", "Hello, World!");
    activity?.SetTag("baz", new int[] { 1, 2, 3 });

    return "Ok";
});*/

app.Run();
