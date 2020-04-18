using Respite;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    interface IBatchedOperation
    {
        Lifetime<Memory<RespValue>> ConsumeArgs();
        void ProcessResponse(in RespValue response);
        bool TrySetException(Exception exception);
    }
    internal sealed class PooledBatch : PooledBase, IBatch
    {
        private readonly PooledBase _gateway;
        public PooledBatch(PooledBase parent) : base(parent.Multiplexer, parent.Database)
            => _gateway = parent;

        List<IBatchedOperation>? _pending = null;
        void AddPending(IBatchedOperation handler) => (_pending ??= new List<IBatchedOperation>()).Add(handler);
        List<IBatchedOperation>? Flush()
        {
            var pending = _pending;
            _pending = null;
            return pending;
        }

        class Handler : TaskCompletionSource<bool>, IBatchedOperation
        {
            private Lifetime<Memory<RespValue>> _args;
            private readonly Action<RespValue>? _inspector;
            public Handler(in Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                _args = args;
                _inspector = inspector;
            }
            Lifetime<Memory<RespValue>> IBatchedOperation.ConsumeArgs()
            {
                var tmp = _args;
                _args = default;
                return tmp;
            }
            void IBatchedOperation.ProcessResponse(in RespValue value)
            {
                _inspector?.Invoke(value);
                TrySetResult(true);
            }
        }
        class Handler<T> : TaskCompletionSource<T>, IBatchedOperation
        {
            private Lifetime<Memory<RespValue>> _args;
            private readonly Func<RespValue, T> _selector;
            public Handler(in Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                _args = args;
                _selector = selector;
            }
            Lifetime<Memory<RespValue>> IBatchedOperation.ConsumeArgs()
            {
                var tmp = _args;
                _args = default;
                return tmp;
            }
            void IBatchedOperation.ProcessResponse(in RespValue value)
                => TrySetResult(_selector(value));
        }
        // private readonly List<(Lifetime<Memory<RespValue>>, Delegate)> _pending = new List<(Lifetime<Memory<RespValue>>, Delegate)>();
        protected override Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null)
        {
            var handler = new Handler(args, inspector);
            AddPending(handler);
            return handler.Task;
        }
            
        protected override Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
        {
            var handler = new Handler<T>(args, selector);
            AddPending(handler);
            return handler.Task;
        }

        void IBatch.Execute()
        {
            var pending = Flush();
            if (pending != null)
            {
                var send = _gateway.CallAsync(pending, default);
                if (_gateway is LeasedDatabase) Multiplexer.Wait(send);
            }
        }
    }
}
