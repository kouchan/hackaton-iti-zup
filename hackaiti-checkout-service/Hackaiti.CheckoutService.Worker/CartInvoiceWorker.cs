using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Confluent.Kafka;
using Hackaiti.CheckoutService.Worker.Model;
using Hackaiti.CheckoutService.Worker.Model.CartMessage;
using Hackaiti.CheckoutService.Worker.Model.InvoiceApi;
using Hackaiti.CheckoutService.Worker.Model.KafkaOrder;
using Hackaiti.CheckoutService.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hackaiti.CheckoutService.Worker
{
    public class CartInvoiceWorker : BackgroundService
    {
        private readonly ILogger<CartInvoiceWorker> _logger;
        private readonly ICurrenciesApiService _currenciesService;
        private readonly IHackatonZupApiService _hackaZupApiService;

        public CartInvoiceWorker(ILogger<CartInvoiceWorker> logger, ICurrenciesApiService currenciesService, IHackatonZupApiService hackaZupApiService)
        {
            _hackaZupApiService = hackaZupApiService;
            _currenciesService = currenciesService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Initializing {nameof(CartInvoiceWorker)}.");

            var client = new AmazonSQSClient();

            var receiveMessageRequest = new ReceiveMessageRequest()
            {
                QueueUrl = WorkerConfig.StartCheckoutQueueURL,
                MaxNumberOfMessages = 1,
                VisibilityTimeout = 121,
                WaitTimeSeconds = 15
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Getting messages from {queueURL}", WorkerConfig.StartCheckoutQueueURL);

                var receiveMessageResponse = await client.ReceiveMessageAsync(receiveMessageRequest);

                foreach (var message in receiveMessageResponse.Messages)
                {
                    try
                    {
                        await ProccessMessage(message);

                        await client.DeleteMessageAsync(new DeleteMessageRequest()
                        {
                            QueueUrl = WorkerConfig.StartCheckoutQueueURL,
                            ReceiptHandle = message.ReceiptHandle
                        });
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogError(ex, "SQS message with erro");
                    }
                }
            }
        }

        private async Task ProccessMessage(Message message)
        {
            _logger.LogInformation("Processing {message}", message.Body);

            var cartMessage = JsonConvert.DeserializeObject<CartMessage>(message.Body);

            var total = await GetTotal(cartMessage);

            var invoiceApiPayload = CreateInvoiceApiPayload(cartMessage, total);

            try
            {
                await _hackaZupApiService.PostInvoice(invoiceApiPayload, cartMessage.Invoice.XTeamControl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error ocurred while posting data do invoices API");
                throw;
            }

            await SendToKafka(cartMessage, invoiceApiPayload);

            await SendToDynamoDb(cartMessage, invoiceApiPayload);
        }

        private async Task SendToDynamoDb(CartMessage cartMessage, Invoice invoiceApiPayload)
        {
            AmazonDynamoDBClient client = new AmazonDynamoDBClient();
            string tableName = WorkerConfig.DynamoTransactionRegisterTableName;

            _logger.LogInformation("Sendig transaction register do DynamoDB");

            var request = new PutItemRequest
            {
                TableName = tableName,
                Item = new Dictionary<string, AttributeValue>()
                {
                    { "cartId", new AttributeValue { S = invoiceApiPayload.Id }},
                    { "amount", new AttributeValue { N = invoiceApiPayload.Total.Amount.ToString() }},
                    { "scale", new AttributeValue { N = invoiceApiPayload.Total.Scale.ToString() }},
                    { "currencyCode", new AttributeValue { S = invoiceApiPayload.Total.CurrencyCode }},
                    { "x-team-control", new AttributeValue { S = cartMessage.Invoice.XTeamControl }},
                    { "timestamp", new AttributeValue { S = DateTime.Now.ToString() }},
                }
            };

            await client.PutItemAsync(request);
        }

        private async Task SendToKafka(CartMessage cartMessage, Invoice invoiceApiPayload)
        {
            _logger.LogInformation("Sending message to kafka...");

            var config = new ProducerConfig()
            {
                BootstrapServers = WorkerConfig.KafkaBootstrapServers
            };

            var kafkaPayload = CreateKafkaOrderPayload(cartMessage, invoiceApiPayload);

            _logger.LogInformation("Kafka payload: {payload}", kafkaPayload);

            // TODO: don't recreate this f** producer every single time
            using (var producer = new ProducerBuilder<Null, string>(config).Build())
            {
                try
                {
                    var message = new Message<Null, string>()
                    {
                        Value = kafkaPayload
                    };

                    var deliveryResult = await producer.ProduceAsync("orders_topic", message);

                    _logger.LogInformation("kafka delivered '{value}' to '{topicPartitionOffset}'.", deliveryResult.Value, deliveryResult.TopicPartitionOffset);
                }
                catch (ProduceException<Null, string> ex)
                {
                    _logger.LogError(ex, "Kafka delivery failed: {reason}", ex.Error.Reason);
                    throw;
                }
            }
        }

        private string CreateKafkaOrderPayload(CartMessage cartMessage, Invoice invoiceApiPayload)
        {
            var order = new KafkaOrder()
            {
                Headers = new KafkaOrderHeader()
                {
                    XTeamControl = cartMessage.Invoice.XTeamControl
                },
                Payload = new KafkaOrderPayload()
                {
                    CartId = cartMessage.Cart.Id,
                    Price = new KafkaOrderPayloadPrice()
                    {
                        Amount = invoiceApiPayload.Total.Amount,
                        CurrencyCode = invoiceApiPayload.Total.CurrencyCode,
                        Scale = invoiceApiPayload.Total.Scale
                    }
                }
            };

            return JsonConvert.SerializeObject(order);
        }

        private Invoice CreateInvoiceApiPayload(CartMessage cartMessage, InvoiceTotal total)
        {
            return new Invoice()
            {
                Id = cartMessage.Cart.Id,
                CustomerId = cartMessage.Cart.CustomerId,
                Status = cartMessage.Cart.Status,
                Total = total,
                Items = cartMessage.Cart.Items.Select(t => new InvoiceItem()
                {
                    Id = t.Product.Id,
                    CurrencyCode = t.CurrencyCode,
                    ImageURL = t.Product.ImageURL,
                    Name = t.Product.Name,
                    Price = t.Price,
                    Scale = t.Scale
                })
            };
        }

        private async Task<InvoiceTotal> GetTotal(CartMessage cartMessage)
        {
            var currencies = await _currenciesService.GetCurrencies();

            var usd2brl = currencies.Single(t => string.Equals(t.CurrencyCode, "USD_TO_BRL", StringComparison.CurrentCultureIgnoreCase));
            var usd2eur = currencies.Single(t => string.Equals(t.CurrencyCode, "USD_TO_EUR", StringComparison.CurrentCultureIgnoreCase));

            var usd2brl_factor = GetFactor(usd2brl.CurrencyValue, usd2brl.Scale);
            var usd2eur_factor = GetFactor(usd2eur.CurrencyValue, usd2eur.Scale);
            var brl2usd_factor = 1 / usd2brl_factor;
            var eur2usd_factor = 1 / usd2eur_factor;

            var currencyConversionTable = new Dictionary<string, double>()
            {
                { "USD-BRL", usd2brl_factor },
                { "USD-EUR", usd2eur_factor },
                { "USD-USD", 1 },

                { "EUR-BRL", eur2usd_factor * usd2brl_factor },
                { "EUR-EUR", 1 },
                { "EUR-USD", eur2usd_factor },

                { "BRL-BRL", 1 },
                { "BRL-EUR", brl2usd_factor * usd2eur_factor },
                { "BRL-USD", brl2usd_factor }
            };

            var total = new InvoiceTotal()
            {
                Amount = 0,
                Scale = 2,
                CurrencyCode = cartMessage.Invoice.CurrencyCode
            };

            foreach (var item in cartMessage.Cart.Items)
            {
                var itemPrice = item.Price / Math.Pow(10, item.Scale);
                var conversionKey = $"{item.CurrencyCode}-{cartMessage.Invoice.CurrencyCode}".ToUpper();
                var conversionFactor = currencyConversionTable[conversionKey];
                var itemAmount = Convert.ToInt64(itemPrice * conversionFactor * 100);

                total.Amount += itemAmount;
            }

            return total;
        }

        private double GetFactor(long currencyValue, long scale)
        {
            return currencyValue / Math.Pow(10, scale);
        }

        public override void Dispose()
        {
            // TODO: liberar recursos do Kafka            
        }
    }
}
