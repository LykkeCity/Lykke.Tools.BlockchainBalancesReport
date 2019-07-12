﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lykke.Job.BlockchainBalancesReport.Clients.InsightApi;
using Lykke.Job.BlockchainBalancesReport.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lykke.Job.BlockchainBalancesReport.Blockchains.Decred
{
    public class DecredBalanceProvider : IBalanceProvider
    {
        public string BlockchainType => "Decred";

        private readonly InsightApiBalanceProvider _balanceProvider;
        
        public DecredBalanceProvider(
            ILoggerFactory loggerFactory,
            IOptions<DecredSettings> settings):this(loggerFactory, settings.Value.InsightApiUrl)
        {
        }

        public DecredBalanceProvider(
            ILoggerFactory loggerFactory,
            string insightApiUrl)
        {
            _balanceProvider = new InsightApiBalanceProvider
            (
                loggerFactory.CreateLogger<InsightApiBalanceProvider>(),
                new InsightApiClient(insightApiUrl),
                NormalizeOrDefault
            );
        }

        public async Task<IReadOnlyDictionary<Asset, decimal>> GetBalancesAsync(string address,
            DateTime at)
        {
            var balance = await _balanceProvider.GetBalanceAsync2(address, at);

            return new Dictionary<Asset, decimal>
            {
                {new Asset("DCR", "DCR", "02154b48-7ed9-4211-b614-e87679fd4f5a"), balance}
            };
        }

        private string NormalizeOrDefault(string address)
        {
            return address;
        }
    }
}
