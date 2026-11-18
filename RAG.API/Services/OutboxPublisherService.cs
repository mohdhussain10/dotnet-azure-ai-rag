using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RAG.API.Models;
using RAG.Infrastructure.Data;
using System.Text;

namespace RAG.API.Services
{
    public class OutboxPublisherService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RabbitMqOptions _rabbitOptions;
        private readonly ILogger<OutboxPublisherService> _logger;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);
        private const int BatchSize = 20;

        private IConnection? _connection;
        private IChannel? _channel;

        public OutboxPublisherService(
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqOptions> rabbitOptions,
            ILogger<OutboxPublisherService> logger)
        {
            _scopeFactory = scopeFactory;
            _rabbitOptions = rabbitOptions.Value;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await InitializeRabbitMqAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }

        private async Task InitializeRabbitMqAsync(CancellationToken cancellationToken)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = string.IsNullOrWhiteSpace(_rabbitOptions.HostName) ? "localhost" : _rabbitOptions.HostName,
                    Port = _rabbitOptions.Port == 0 ? 5672 : _rabbitOptions.Port,
                    UserName = string.IsNullOrWhiteSpace(_rabbitOptions.UserName) ? "guest" : _rabbitOptions.UserName,
                    Password = string.IsNullOrWhiteSpace(_rabbitOptions.Password) ? "guest" : _rabbitOptions.Password,
                    AutomaticRecoveryEnabled = true
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

                var exchangeName = string.IsNullOrWhiteSpace(_rabbitOptions.ExchangeName) ? "rag.events.exchange" : _rabbitOptions.ExchangeName;
                var queueName = string.IsNullOrWhiteSpace(_rabbitOptions.QueueName) ? "rag.file.processing.queue" : _rabbitOptions.QueueName;
                var routingKey = string.IsNullOrWhiteSpace(_rabbitOptions.RoutingKey) ? "file.uploaded" : _rabbitOptions.RoutingKey;

                await _channel.ExchangeDeclareAsync(
                    exchange: exchangeName,
                    type: ExchangeType.Direct,
                    durable: true,
                    cancellationToken: cancellationToken);

                await _channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                await _channel.QueueBindAsync(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("RabbitMQ connection established and queue declared: {QueueName}", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ broker is not yet reachable. Will retry during execution cycles.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxPublisherService is starting.");

            using var timer = new PeriodicTimer(_pollingInterval);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessOutboxBatchAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while polling or publishing outbox events.");
                }
            }
        }

        private async Task ProcessOutboxBatchAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // If channel is closed, attempt reconnection
            if (_channel == null || !_channel.IsOpen)
            {
                await InitializeRabbitMqAsync(cancellationToken);
                if (_channel == null || !_channel.IsOpen)
                {
                    return; 
                }
            }
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var pendingEvents = await dbContext.OutboxEvents
                .FromSqlRaw(@"
                    SELECT * FROM public.""OutboxEvents""
                    WHERE ""IsProcessed"" = FALSE
                    ORDER BY ""CreatedAt"" ASC
                    LIMIT {0}
                    FOR UPDATE SKIP LOCKED", BatchSize)
                .ToListAsync(cancellationToken);

            if (pendingEvents.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Found {Count} pending outbox events to publish.", pendingEvents.Count);

            var exchangeName = string.IsNullOrWhiteSpace(_rabbitOptions.ExchangeName) ? "rag.events.exchange" : _rabbitOptions.ExchangeName;
            var routingKey = string.IsNullOrWhiteSpace(_rabbitOptions.RoutingKey) ? "file.uploaded" : _rabbitOptions.RoutingKey;

            foreach (var outboxEvent in pendingEvents)
            {
                try
                {
                    var body = Encoding.UTF8.GetBytes(outboxEvent.PayloadJson);

                    var props = new BasicProperties
                    {
                        MessageId = outboxEvent.Id.ToString(),            // For Idempotency  
                        CorrelationId = outboxEvent.AggregateId.ToString(),
                        Type = outboxEvent.EventType,
                        ContentType = "application/json",
                        DeliveryMode = DeliveryModes.Persistent //It will store message in hard disk and not in memory. if it is DeliveryModes.Transient it will store in ram
                    };

                    await _channel.BasicPublishAsync(
                        exchange: exchangeName,
                        routingKey: routingKey,
                        mandatory: true,
                        basicProperties: props,
                        body: body,
                        cancellationToken: cancellationToken);

                    outboxEvent.IsProcessed = true;
                    outboxEvent.ProcessedAt = DateTime.UtcNow;
                    outboxEvent.Error = null;

                    _logger.LogInformation("Published OutboxEvent {EventId} for Aggregate {AggregateId} to RabbitMQ.",
                        outboxEvent.Id, outboxEvent.AggregateId);
                }
                catch (Exception ex)
                {
                    outboxEvent.RetryCount++;
                    outboxEvent.Error = ex.Message;
                    _logger.LogError(ex, "Failed to publish OutboxEvent {EventId}. Retry count: {RetryCount}",
                        outboxEvent.Id, outboxEvent.RetryCount);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OutboxPublisherService is stopping.");

            if (_channel != null)
            {
                await _channel.CloseAsync(cancellationToken);
                _channel.Dispose();
            }

            if (_connection != null)
            {
                await _connection.CloseAsync(cancellationToken);
                _connection.Dispose();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
