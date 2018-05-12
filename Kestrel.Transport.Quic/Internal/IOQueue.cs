using System;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    public class IOQueue : PipeScheduler
    {
        public override void Schedule(Action<object> action, object state)
        {
            throw new NotImplementedException();
        }
    }
}