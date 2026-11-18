using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using System.Text;
using System.Text.Json;

namespace RAG.API.Services
{
    public class FileProcessingConsumerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly RabbitMqOptions _rabbitOptions;
        private readonly ILogger<FileProcessingConsumerService> _logger;

        private IConnection? _connection;
        private IChannel? _channel;

        public FileProcessingConsumerService(
            IServiceScopeFactory scopeFactory,
            BlobContainerClient blobContainerClient,
            IOptions<RabbitMqOptions> rabbitOptions,
            ILogger<FileProcessingConsumerService> logger)
        {
            _scopeFactory = scopeFactory;
            _blobContainerClient = blobContainerClient;
            _rabbitOptions = rabbitOptions.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileProcessingConsumerService is starting.");

            await ConnectAndConsumeAsync(stoppingToken);
        }

        private async Task ConnectAndConsumeAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
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

                    _connection = await factory.CreateConnectionAsync(stoppingToken);
                    _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

                    var exchangeName = string.IsNullOrWhiteSpace(_rabbitOptions.ExchangeName) ? "rag.events.exchange" : _rabbitOptions.ExchangeName;
                    var queueName = string.IsNullOrWhiteSpace(_rabbitOptions.QueueName) ? "rag.file.processing.queue" : _rabbitOptions.QueueName;
                    var routingKey = string.IsNullOrWhiteSpace(_rabbitOptions.RoutingKey) ? "file.uploaded" : _rabbitOptions.RoutingKey;

                    await _channel.ExchangeDeclareAsync(
                        exchange: exchangeName,
                        type: ExchangeType.Direct,
                        durable: true,
                        cancellationToken: stoppingToken);

                    await _channel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        cancellationToken: stoppingToken);

                    await _channel.QueueBindAsync(
                        queue: queueName,
                        exchange: exchangeName,
                        routingKey: routingKey,
                        cancellationToken: stoppingToken);

                    // PrefetchCount = 1: Consumer processes ONE document at a time for fair load distribution
                    await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

                    var consumer = new AsyncEventingBasicConsumer(_channel);
                    consumer.ReceivedAsync += async (sender, ea) =>
                    {
                        await ProcessMessageAsync(ea, stoppingToken);
                    };

                    await _channel.BasicConsumeAsync(
                        queue: queueName,
                        autoAck: false,
                        consumer: consumer,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation("Subscribed to queue: {QueueName}. Waiting for file processing messages...", queueName);

                    // Keep consumer alive until shutdown
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RabbitMQ connection lost in consumer. Retrying in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
        {
            if (_channel == null) return;

            FileUploadEventModel? eventData = null;
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                eventData = JsonSerializer.Deserialize<FileUploadEventModel>(json);

                if (eventData == null || eventData.DocumentId == Guid.Empty)
                {
                    _logger.LogWarning("Malformed message received. Rejecting and discarding message.");
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                    return;
                }

                _logger.LogInformation("Received processing request for Document: {DocumentId}, File: {FileName}",
                    eventData.DocumentId, eventData.FileName);

                //Creating scope service to get fresh object of be
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var textExtractor = scope.ServiceProvider.GetRequiredService<ITextExtractorService>();
                var chunkingService = scope.ServiceProvider.GetRequiredService<IChunkingService>();
                var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
                var azureSearchService = scope.ServiceProvider.GetRequiredService<IAzureSearchService>();

                //Checking if document is available in DB
                var document = await dbContext.UserDocuments.FirstOrDefaultAsync(
                    d => d.Id == eventData.DocumentId, cancellationToken);

                if (document == null)
                {
                    _logger.LogWarning("Document {DocumentId} does not exist in DB. Acknowledging message to prevent requeue.",
                        eventData.DocumentId);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                    return;
                }

                //Checking if document is already processed to prevent duplicate processing
                if (document.Status == DocumentStatus.Completed)
                {
                    _logger.LogInformation("Document {DocumentId} is already marked as Completed. Skipping duplicate processing (Idempotent).",
                        eventData.DocumentId);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                    return;
                }

               //Marking as Processing
                document.Status = DocumentStatus.Processing;
                await dbContext.SaveChangesAsync(cancellationToken);

               //Downloading file from Azure Blob to process it
                var blobUri = new Uri(eventData.BlobUrl);
                var blobName = Uri.UnescapeDataString(string.Concat(blobUri.Segments.Skip(2)));

                var blobClient = _blobContainerClient.GetBlobClient(blobName);
                await using var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                //Extracting Text
                var extractedText = await textExtractor.ExtractTextAsync(
                    memoryStream, eventData.FileExtension, cancellationToken);

                //Chunking text
                var chunks = chunkingService.CreateChunks(extractedText, chunkSize: 1000, chunkOverlap: 200);
                _logger.LogInformation("Generated {ChunkCount} chunks for Document {DocumentId}",
                    chunks.Count, eventData.DocumentId);

                if (chunks.Count > 0)
                {
                    //Generate embedding
                    var chunkTexts = chunks.Select(c => c.Content);
                    var vectors = await embeddingService.GenerateEmbeddingsAsync(chunkTexts, cancellationToken);

                   //Uploading vector to Azure AI search
                    var searchChunks = new List<DocumentChunkModel>();
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        searchChunks.Add(new DocumentChunkModel
                        {
                            Id = $"{eventData.DocumentId}_{i}", // Deterministic key
                            DocumentId = eventData.DocumentId.ToString(),
                            UserId = eventData.UserId.ToString(),
                            FileName = eventData.FileName,
                            Content = chunks[i].Content,
                            ChunkIndex = i,
                            ContentVector = vectors[i].ToArray()
                        });
                    }

                    await azureSearchService.InsertChunksAsync(searchChunks, cancellationToken);
                }

                //Processing completed. Marking as completed
                document.Status = DocumentStatus.Completed;
                document.ChunkCount = chunks.Count;
                await dbContext.SaveChangesAsync(cancellationToken);

               
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                _logger.LogInformation("Successfully completed RAG ingestion for Document {DocumentId}", eventData.DocumentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document {DocumentId}", eventData?.DocumentId);

                try
                {
                    if (eventData != null && eventData.DocumentId != Guid.Empty)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var doc = await dbContext.UserDocuments.FirstOrDefaultAsync(
                            d => d.Id == eventData.DocumentId, cancellationToken);

                        if (doc != null)
                        {
                            doc.Status = DocumentStatus.Failed;
                            await dbContext.SaveChangesAsync(cancellationToken);
                        }
                    }

                    // NACK with requeue=false to avoid poison message loops
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: cancellationToken);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, "Failed to NACK message for tag {DeliveryTag}", ea.DeliveryTag);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FileProcessingConsumerService is stopping.");

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
