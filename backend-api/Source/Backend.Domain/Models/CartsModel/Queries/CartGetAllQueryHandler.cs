﻿using Backend.Domain.Core.Queries;
using Backend.Domain.Core.Results;
using Backend.Domain.Models.CartModel.Repositories;
using System.Collections.Generic;
using System.Linq;

namespace Backend.Domain.Models.CartModel.Queries
{
    public sealed class CartGetAllQueryHandler : QueryHandler<CartGetAllQuery, List<Cart>>
    {
        private readonly ICartRepository _repository;

        public CartGetAllQueryHandler(ICartRepository repository)
        {
            _repository = repository;
        }

        public override AbstractOperationResult<List<Cart>> Handle(CartGetAllQuery query)
        {
            var result = _repository.GetAll().Result.ToList();

            return new SuccessOperationResult<List<Cart>>(result);
        }
    }
}
