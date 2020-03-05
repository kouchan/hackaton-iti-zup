using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Newtonsoft.Json;
using Backend.Domain.Models.CartModel;
using Backend.Domain.Models.CartModel.Repositories;

namespace Backend.Infra.Repositories
{
    public class CartRepository : ICartRepository
    {
        private readonly AmazonDynamoDBClient dbClient;

        public CartRepository(AmazonDynamoDBClient dbClient)
        {
            this.dbClient = dbClient;
        }

        public bool ExistsById(string id)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            return table.Query(new QueryFilter("id", QueryOperator.Equal, new List<AttributeValue>()
            {
                new AttributeValue()
                {
                    S = id
                }
            })).Count > 0;
        }

        public bool ExistsByCustomerId(string customerId)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new ScanFilter();
            filter.AddCondition("customerId", ScanOperator.Equal, customerId);
            filter.AddCondition("status", ScanOperator.Equal, "PENDING");

            return table.Scan(filter).Count > 0;
        }

        public async Task<Cart> GetById(string id)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new QueryFilter();
            filter.AddCondition("id", QueryOperator.Equal, id);

            var queryConfig = new QueryOperationConfig
            {
                Filter = filter,
                Limit = 1
            };

            var result = table.Query(queryConfig);

            if (result.IsDone)
            {
                throw new ArgumentException("id");
            }
            else
            {
                var querySet = await result.GetNextSetAsync();

                var document = querySet.First();

                Cart cart = CartFromDocument(document);

                return cart;
            }
        }

        public async Task<Cart> GetByCustomerId(string customerId)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new ScanFilter();
            filter.AddCondition("customerId", ScanOperator.Equal, customerId);
            filter.AddCondition("status", ScanOperator.Equal, "PENDING");

            var result = table.Scan(filter);

            if (result.IsDone)
            {
                throw new ArgumentException("customerId");
            }
            else
            {
                var querySet = await result.GetNextSetAsync();

                var document = querySet.First();

                Cart cart = CartFromDocument(document);

                return cart;
            }
        }

        public async Task<IEnumerable<Cart>> GetAll()
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var scanFilter = new ScanFilter();

            var scanOperation = new ScanOperationConfig()
            {
                Filter = scanFilter
            };

            var result = table.Scan(scanOperation);

            var carts = new List<Cart>();

            while (!result.IsDone)
            {
                var querySet = await result.GetNextSetAsync();

                foreach (var document in querySet)
                {
                    Cart cart = CartFromDocument(document);
                    carts.Add(cart);
                }
            }

            return carts;
        }

        public async Task Add(Cart cart)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");
            Document document = DocumentFromCart(cart);

            await table.PutItemAsync(document);
        }

        public async Task Update(Cart cart)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");
            Document document = DocumentFromCart(cart);

            await table.UpdateItemAsync(document);
        }

        public async Task Delete(string id)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new QueryFilter();
            filter.AddCondition("id", QueryOperator.Equal, id);

            var queryConfig = new QueryOperationConfig
            {
                Filter = filter,
                Limit = 1
            };

            var result = table.Query(queryConfig);

            if (result.IsDone)
            {
                throw new ArgumentException("id");
            }
            else
            {
                var querySet = await result.GetNextSetAsync();

                var document = querySet.First();

                document["status"] = "CANCEL";

                await table.UpdateItemAsync(document);
            }
        }

        public async Task DeleteItem(string id, string item_id)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new QueryFilter();
            filter.AddCondition("id", QueryOperator.Equal, id);

            var queryConfig = new QueryOperationConfig
            {
                Filter = filter,
                Limit = 1
            };

            var result = table.Query(queryConfig);

            if (result.IsDone)
            {
                throw new ArgumentException("id");
            }
            else
            {
                var querySet = await result.GetNextSetAsync();

                var document = querySet.First();

                var items = JsonConvert.DeserializeObject<List<CartItem>>(document["items"].AsString());
                var item = items.FirstOrDefault(x => x.id.ToString() == item_id);
                items.Remove(item);

                await table.UpdateItemAsync(document);
            }
        }

        public async Task Checkout(string id)
        {
            var table = Table.LoadTable(dbClient, "carts_id_cid");

            var filter = new QueryFilter();
            filter.AddCondition("id", QueryOperator.Equal, id);

            var queryConfig = new QueryOperationConfig
            {
                Filter = filter,
                Limit = 1
            };

            var result = table.Query(queryConfig);

            if (result.IsDone)
            {
                throw new ArgumentException("id");
            }
            else
            {
                var querySet = await result.GetNextSetAsync();

                var document = querySet.First();

                document["status"] = "DONE";

                await table.UpdateItemAsync(document);
            }
        }

        private static Document DocumentFromCart(Cart cart)
        {
            var document = new Document();

            document["id"] = cart.id;
            document["customerId"] = cart.customerId;
            document["status"] = cart.status;
            document["items"] = JsonConvert.SerializeObject(cart.items);

            return document;
        }

        private static Cart CartFromDocument(Document document)
        {
            var cart = new Cart();

            cart.id = document["id"].AsGuid();
            cart.customerId = document["customerId"].AsString();
            cart.status = document["status"].AsString();
            cart.items = JsonConvert.DeserializeObject<List<CartItem>>(document["items"].AsString());

            return cart;
        }
    }
}