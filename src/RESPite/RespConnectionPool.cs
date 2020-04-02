//using Respite.Internal;
//using System;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Respite
//{
//    public sealed class RespConnectionPool
//    {
//        readonly Pool<RespConnection> _pool;
//        readonly Func<RespConnection> _factory;
//        public RespConnectionPool(Func<RespConnection> factory, PoolOptions? options = null)
//        {
//            _factory = factory;
//            _pool = new Pool<RespConnection>(options ?? PoolOptions.Default);
//        }
        

//        public Lifetime<RespConnection> Rent() => _pool.Rent();
//        public ValueTask<Lifetime<RespConnection>> RentAsync(CancellationToken cancellationToken = default)
//            => _pool.RentAsync(cancellationToken);
//    }
//}
