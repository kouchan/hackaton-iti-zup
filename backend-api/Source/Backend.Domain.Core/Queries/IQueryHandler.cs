using Backend.Domain.Core.Results;

namespace Backend.Domain.Core.Queries
{
    public interface IQueryHandler<in TQuery, TResult>
        where TQuery : IQuery
    {
        AbstractOperationResult<TResult> Handle(TQuery query);
    }
}
