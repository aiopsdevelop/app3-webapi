
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Trace;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace core6.Healthy
{
    public class ProxyNetwork : IHealthCheck
    {
        private static readonly HttpClient HttpClient = new();

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var tracer = TracerProvider.Default.GetTracer(nameof(ProxyNetwork));

            using (var youTubeSpan = tracer.StartSpan("YouTube"))
            {

                youTubeSpan.AddEvent("checkYouTubeProxyStart", DateTimeOffset.Now);
                youTubeSpan.SetAttribute("proxy", "youTube");
                var response = HttpClient.GetStringAsync("https://www.speedtest.net").Result;

                youTubeSpan.AddEvent("checkYouTubeProxyFinish", DateTimeOffset.Now);
                using (var twitterSpan = tracer.StartSpan("Twitter"))
                {
                    twitterSpan.AddEvent("checkTwitterProxyStart", DateTimeOffset.Now);
                    twitterSpan.SetAttribute("proxy", "twitter");
                    var responseTw = HttpClient.GetStringAsync("https://github.com").Result;
                    twitterSpan.AddEvent("checkTwitterProxyFinish", DateTimeOffset.Now);
                }
            }

            // Console.WriteLine(status);

            /*var result = response == "Ok"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Failed app3");*/
            var result = HealthCheckResult.Healthy();

            return Task.FromResult(result);
        }
    }
}
