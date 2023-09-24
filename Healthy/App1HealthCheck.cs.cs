
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace core6.Healthy
{
    public class App1HealthCheck : IHealthCheck
    {
        private static readonly HttpClient HttpClient = new();

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var response = HttpClient.GetStringAsync("http://hybridlog.io:5000/healthz").Result;

            // Console.WriteLine(status);

            /*var result = response == "Ok"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Failed app3");*/
            var result = HealthCheckResult.Healthy();

            return Task.FromResult(result);
        }
    }
}
