using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using core6.Events;
using core6.Repository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace core6.Repository
{
    public class RabbitRepository : IRabbitRepository
    {
        private static readonly ActivitySource Activity = new(nameof(RabbitRepository));
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        private readonly ILogger<RabbitRepository> _logger;
        private readonly IConfiguration _configuration;

        public RabbitRepository(
            ILogger<RabbitRepository> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public void Publish(IEvent evt)
        {
            try
            {
                using (var activity = Activity.StartActivity("RabbitMq Publish", ActivityKind.Producer))
                {
                    // var factory = new ConnectionFactory { HostName = _configuration["RabbitMq:Host"] };
                    var factory = new ConnectionFactory() { HostName = _configuration["RabbitMq:Host"], UserName = _configuration["RabbitMq:Username"], Password = _configuration["RabbitMq:Password"], DispatchConsumersAsync = true };
                    using (var connection = factory.CreateConnection())
                    using (var channel = connection.CreateModel())
                    {
                        var props = channel.CreateBasicProperties();

                        AddActivityToHeader(activity, props);

                        channel.QueueDeclare(queue: "sample_2",
                            durable: false,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

                        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evt));
                        _logger.LogInformation("Publishing message to queue");

                        channel.BasicPublish(exchange: "",
                            routingKey: "sample_2",
                            basicProperties: props,
                            body: body);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error trying to publish a message", e);
                throw;
            }

        }

        private void AddActivityToHeader(Activity activity, IBasicProperties props)
        {
            if (activity != null)
            {
                Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), props, InjectContextIntoHeader);
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination_kind", "queue");
                activity?.SetTag("messaging.rabbitmq.queue", "sample_2");
            }
            else
            {
                _logger.LogWarning("Error on activity for publish a message");
            }
        }

        private void InjectContextIntoHeader(IBasicProperties props, string key, string value)
        {
            try
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject trace context.");
            }
        }
    }
}
