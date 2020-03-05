﻿using Backend.Domain.Core.Entities;
using Backend.Domain.Models.ProductModel;
using System;

namespace Backend.Domain.Models.CartModel
{
    public class CartItem : Entity<Guid>
    {
        public CartItem()
        {

        }

        public CartItem(Product product)
        {
            id = product.id;
            scale = product.price.scale;
            curencyCode = product.price.currencyCode;
        }

        public long? price { get; set; }
        public long? scale { get; set; }
        public string curencyCode { get; set; }
    }

    public class CartEditItem
    {
        public string sku { get; set; }
        public long? quantity { get; set; }
    }
}
