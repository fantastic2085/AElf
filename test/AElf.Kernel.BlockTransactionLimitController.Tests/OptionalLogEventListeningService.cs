using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.SmartContractExecution.Application;
using AElf.Types;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.BlockTransactionLimitController.Tests
{
    public class OptionalLogEventListeningService<T> : ILogEventListeningService<T> where T:ILogEventHandler
    {
        private LogEventListeningService<T> _inner;

        public OptionalLogEventListeningService(LogEventListeningService<T> inner)
        {
            _inner = inner;
        }

        public static bool Enabled { get; set; }

        public async Task ApplyAsync(IEnumerable<Block> blocks)
        {
            if (Enabled)
            {
                await _inner.ApplyAsync(blocks);
            }
        }
    }
}