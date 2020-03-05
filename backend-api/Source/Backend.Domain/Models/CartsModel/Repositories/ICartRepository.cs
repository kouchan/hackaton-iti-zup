﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Backend.Domain.Models.CartModel.Repositories
{
    public interface ICartRepository
    {
        bool ExistsById(string id);
        bool ExistsByCustomerId(string id);
        Task<Cart> GetById(string id);
        Task<Cart> GetByCustomerId(string customerId);
        Task<IEnumerable<Cart>> GetAll();
        Task Add(Cart cart);
        Task Delete(string id);
        Task DeleteItem(string id, string item_id);
    }
}
