﻿namespace Lykke.Job.BlockchainBalancesReport.Settings
{
    public class AzureSqlRepositorySettings
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public bool CreateTable { get; set; }
    }
}
