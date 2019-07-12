﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Job.BlockchainBalancesReport.Clients.EosAuthorityApi;
using Lykke.Job.BlockchainBalancesReport.Clients.EosParkApi;
using Lykke.Job.BlockchainBalancesReport.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Lykke.Job.BlockchainBalancesReport.Blockchains.Eos
{
    public class EosBalanceProvider : IBalanceProvider
    {
        public string BlockchainType => "Eos";

        private readonly ILogger<EosBalanceProvider> _logger;
        private readonly EosParkApiClient _eosParkClient;
        private readonly EosAuthorityApiClient _eosAuthorityClient;
        private readonly (string Code, string Symbol) _nativeAsset;

        public EosBalanceProvider(
            ILogger<EosBalanceProvider> logger,
            IOptions<EosSettings> settings)
        {
            _logger = logger;
            _eosParkClient = new EosParkApiClient(settings.Value.ParkApiUrl, settings.Value.ApiKey);
            _eosAuthorityClient = new EosAuthorityApiClient(settings.Value.EosAuthorityUrl);

            _nativeAsset = ("eosio.token", "EOS");
        }

        public async Task<IReadOnlyDictionary<Asset, decimal>> GetBalancesAsync(string address, DateTime at)
        {
            var page = 1;
            var balances = new Dictionary<(string Code, string Symbol), decimal>();
            var transactionsRead = 0;

            var genesisResponseTask = Policy
                .Handle<Exception>(ex =>
                {
                    _logger.LogWarning(ex, $"Failed to get genesis info of {address}. Operation will be retried.");
                    return true;
                })
                .OrResult<EosAuthorityApiAccountGenesisResponse>(x =>
                {
                    if (x.Status != null && x.Message != "Not a genesis account")
                    {
                        _logger.LogWarning($"Failed to get genesis info of {address}: {x.Status}: {x.StatusCode} - {x.Message}. Operation will be retried.");
                        return true;
                    }
                    return false;
                })
                .WaitAndRetryForeverAsync(i => TimeSpan.FromSeconds(Math.Min(i, 5)))
                .ExecuteAsync(async () => await _eosAuthorityClient.GetAccountGenesisAsync(address));

            var tokensListResponse = await Policy
                .Handle<Exception>(ex =>
                {
                    _logger.LogWarning(ex, $"Failed to get tokens list of {address}. Operation will be retried.");
                    return true;
                })
                .OrResult<EosParkApiAccountTokensListResponse>(x =>
                {
                    if (x.ErrNo != 0)
                    {
                        _logger.LogWarning($"Failed to get tokens list of {address}: {x.ErrNo} - {x.ErrMsg}. Operation will be retried.");
                        return true;
                    }
                    return false;
                })
                .WaitAndRetryForeverAsync(i => TimeSpan.FromSeconds(Math.Min(i, 5)))
                .ExecuteAsync(async () => await _eosParkClient.GetAccountTokensList(address));
            var tokenCodes = tokensListResponse.Data.SymbolList.Select(x => x.Code).ToHashSet();

            do
            {
                var response = await Policy
                    .Handle<Exception>(ex =>
                    {
                        _logger.LogWarning(ex, $"Failed to get transactions page {page} of {address}. Operation will be retried.");
                        return true;
                    })
                    .OrResult<EosParkApiAccountTransactionsResponse>(x =>
                    {
                        if (x.ErrNo != 0)
                        {
                            _logger.LogWarning($"Failed to get transactions page {page} of {address}: {x.ErrNo} - {x.ErrMsg}. Operation will be retried.");
                            return true;
                        }
                        return false;
                    })
                    .WaitAndRetryForeverAsync(i => TimeSpan.FromSeconds(Math.Min(i, 5)))
                    .ExecuteAsync(async () => await _eosParkClient.GetAccountTransactions(address, page));

                foreach (var tx in response.Data.TraceList)
                {
                    ++transactionsRead;

                    if (tx.Status != "executed")
                    {
                        throw new NotSupportedException($"Only executed transactions are supported. Implement support of new status - {tx.Status}");
                    }

                    if (tx.Timestamp > at)
                    {
                        continue;
                    }

                    if (!tokenCodes.Contains(tx.Code))
                    {
                        continue;
                    }

                    var txAmount = decimal.Parse(tx.Quantity, CultureInfo.InvariantCulture);
                    var balanceChange = address.Equals(tx.Receiver, StringComparison.InvariantCultureIgnoreCase)
                        ? txAmount
                        : -txAmount;

                    if (!balances.TryGetValue((tx.Code, tx.Symbol), out var balance))
                    {
                        balances.Add((tx.Code, tx.Symbol), balanceChange);
                    }
                    else
                    {
                        balances[(tx.Code, tx.Symbol)] = balance + balanceChange;
                    }
                }

                if (transactionsRead >= response.Data.TraceCount)
                {
                    break;
                }

                ++page;
            }
            while (true);

            var genesisResponse = await genesisResponseTask;

            if (genesisResponse.Status == null)
            {
                var genesisBalance = decimal.Parse(genesisResponse.BalanceTotal, CultureInfo.InvariantCulture);

                // true for hezdemrzhege, not sure for another
                genesisBalance -= 10;

                if (address != "hezdemrzhege")
                {
                    throw new NotSupportedException($"Check if genesis balance is calculated correctly for {address}");
                }

                if (!balances.TryGetValue(_nativeAsset, out var balance))
                {
                    balances.Add(_nativeAsset, genesisBalance);
                }
                else
                {
                    balances[_nativeAsset] = balance + genesisBalance;
                }
            }

            return balances.ToDictionary(x => GetBalancesKey(x.Key.Code, x.Key.Symbol), x => x.Value);
        }

        private Asset GetBalancesKey(string code, string symbol)
        {
            return code == _nativeAsset.Code
                ? new Asset(symbol, code, "782e7e92-2ce0-4b21-b425-6096983351af") 
                : new Asset(symbol, code, null);
        }
    }
}
