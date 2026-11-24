/* Link Portfolio: apply after 001 (and 002 for older databases).
   Preserves existing rows and leaves existing portfolios unlinked.
   Re-runnable; run with sqlcmd -b or SSMS, outside an existing transaction. */
USE [Gemino];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'Gemino' THROW 50000, 'Select Gemino before running this script.', 1;
IF OBJECT_ID(N'dbo.Portfolios', N'U') IS NULL THROW 50001, 'Run the schema bootstrap first.', 1;
IF @@TRANCOUNT <> 0 THROW 50002, 'Run outside an existing transaction.', 1;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.CK_Holdings_NonEmptyCostBasis', N'C') IS NOT NULL
        ALTER TABLE dbo.Holdings DROP CONSTRAINT CK_Holdings_NonEmptyCostBasis;
    IF OBJECT_ID(N'dbo.CK_Holdings_ShareCount_NonNegative', N'C') IS NOT NULL
        ALTER TABLE dbo.Holdings DROP CONSTRAINT CK_Holdings_ShareCount_NonNegative;
    IF OBJECT_ID(N'dbo.CK_Holdings_AveragePurchasePrice_NonNegative', N'C') IS NOT NULL
        ALTER TABLE dbo.Holdings DROP CONSTRAINT CK_Holdings_AveragePurchasePrice_NonNegative;
    IF OBJECT_ID(N'dbo.DF_Holdings_AveragePurchasePrice', N'D') IS NOT NULL
        ALTER TABLE dbo.Holdings DROP CONSTRAINT DF_Holdings_AveragePurchasePrice;
    IF OBJECT_ID(N'dbo.DF_Holdings_ShareCount', N'D') IS NOT NULL
        ALTER TABLE dbo.Holdings DROP CONSTRAINT DF_Holdings_ShareCount;
    ALTER TABLE dbo.Holdings ALTER COLUMN ShareCount decimal(28,9) NOT NULL;
    ALTER TABLE dbo.Holdings ALTER COLUMN AveragePurchasePrice decimal(19,8) NULL;
    ALTER TABLE dbo.Holdings ADD CONSTRAINT DF_Holdings_ShareCount DEFAULT (0) FOR ShareCount;
    ALTER TABLE dbo.Holdings WITH CHECK ADD CONSTRAINT CK_Holdings_ShareCount_NonNegative CHECK (ShareCount >= 0);
    ALTER TABLE dbo.Holdings WITH CHECK ADD CONSTRAINT CK_Holdings_AveragePurchasePrice_NonNegative
        CHECK (AveragePurchasePrice IS NULL OR AveragePurchasePrice >= 0);

    IF COL_LENGTH(N'dbo.Portfolios', N'DisplayName') IS NULL
        ALTER TABLE dbo.Portfolios ADD DisplayName nvarchar(200) NULL;
    IF OBJECT_ID(N'dbo.CK_Portfolios_DisplayName', N'C') IS NULL
        EXEC(N'ALTER TABLE dbo.Portfolios WITH CHECK ADD CONSTRAINT CK_Portfolios_DisplayName
            CHECK (DisplayName IS NULL OR LEN(LTRIM(RTRIM(DisplayName))) > 0);');
    IF OBJECT_ID(N'dbo.CK_Portfolios_PortfolioType', N'C') IS NOT NULL
        ALTER TABLE dbo.Portfolios DROP CONSTRAINT CK_Portfolios_PortfolioType;
    ALTER TABLE dbo.Portfolios WITH CHECK ADD CONSTRAINT CK_Portfolios_PortfolioType
        CHECK (PortfolioType IN (0,1,2,3,4,5,255)); -- 255: provider classification unspecified
    IF OBJECT_ID(N'dbo.AK_Portfolios_OwnerId_PortfolioId', N'UQ') IS NULL
        ALTER TABLE dbo.Portfolios ADD CONSTRAINT AK_Portfolios_OwnerId_PortfolioId UNIQUE (OwnerId, PortfolioId);

    IF OBJECT_ID(N'dbo.ProviderConnections', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProviderConnections (
            ConnectionId uniqueidentifier NOT NULL CONSTRAINT PK_ProviderConnections PRIMARY KEY,
            OwnerId uniqueidentifier NOT NULL,
            PortfolioId uniqueidentifier NOT NULL,
            Provider tinyint NOT NULL,
            Environment tinyint NOT NULL,
            ExternalAccountId nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CredentialReference uniqueidentifier NOT NULL,
            ConnectionStatus tinyint NOT NULL CONSTRAINT DF_ProviderConnections_Status DEFAULT (0),
            LastAttemptAt datetimeoffset(7) NOT NULL,
            LastSuccessfulSyncAt datetimeoffset(7) NOT NULL,
            FetchStartedAt datetimeoffset(7) NOT NULL,
            FetchCompletedAt datetimeoffset(7) NOT NULL,
            ProviderObservedAt datetimeoffset(7) NULL,
            ExternalAccountType nvarchar(100) NULL,
            LastError nvarchar(500) NULL,
            SyncGeneration bigint NOT NULL CONSTRAINT DF_ProviderConnections_Generation DEFAULT (1),
            RowVersion rowversion NOT NULL,
            CONSTRAINT UQ_ProviderConnections_Account UNIQUE (OwnerId, Provider, Environment, ExternalAccountId),
            CONSTRAINT UQ_ProviderConnections_Portfolio UNIQUE (PortfolioId),
            CONSTRAINT UQ_ProviderConnections_Credential UNIQUE (OwnerId, CredentialReference),
            CONSTRAINT FK_ProviderConnections_Portfolios FOREIGN KEY (OwnerId, PortfolioId)
                REFERENCES dbo.Portfolios (OwnerId, PortfolioId),
            CONSTRAINT CK_ProviderConnections_Provider CHECK (Provider IN (0)),
            CONSTRAINT CK_ProviderConnections_Environment CHECK (Environment IN (0,1)),
            CONSTRAINT CK_ProviderConnections_Status CHECK (ConnectionStatus IN (0,1,2)),
            CONSTRAINT CK_ProviderConnections_Account CHECK (LEN(LTRIM(RTRIM(ExternalAccountId))) > 0),
            CONSTRAINT CK_ProviderConnections_Generation CHECK (SyncGeneration >= 1),
            CONSTRAINT CK_ProviderConnections_Times CHECK (FetchCompletedAt >= FetchStartedAt)
        );
    END;
    IF OBJECT_ID(N'dbo.ProviderSecurityMappings', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProviderSecurityMappings (
            MappingId uniqueidentifier NOT NULL CONSTRAINT PK_ProviderSecurityMappings PRIMARY KEY,
            Provider tinyint NOT NULL,
            Environment tinyint NOT NULL,
            ExternalAssetId nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SecurityId uniqueidentifier NOT NULL,
            CONSTRAINT UQ_ProviderSecurityMappings_Asset UNIQUE (Provider, Environment, ExternalAssetId),
            CONSTRAINT UQ_ProviderSecurityMappings_Security UNIQUE (Provider, Environment, SecurityId),
            CONSTRAINT FK_ProviderSecurityMappings_Securities FOREIGN KEY (SecurityId) REFERENCES dbo.Securities (SecurityId),
            CONSTRAINT CK_ProviderSecurityMappings_Provider CHECK (Provider IN (0)),
            CONSTRAINT CK_ProviderSecurityMappings_Environment CHECK (Environment IN (0,1)),
            CONSTRAINT CK_ProviderSecurityMappings_Asset CHECK (LEN(LTRIM(RTRIM(ExternalAssetId))) > 0)
        );
    END;
    COMMIT TRANSACTION;
    PRINT N'Link Portfolio schema ready. Existing records preserved.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
