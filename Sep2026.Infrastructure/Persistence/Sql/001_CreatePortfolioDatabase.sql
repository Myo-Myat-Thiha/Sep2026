/*
    Initial SQL Server schema for Gemino portfolio tracking and market-data analysis.

    Run the complete file in SQL Server Management Studio or sqlcmd.
    GO is a client batch separator; do not submit the whole file as one EF command.

    Database: Gemino
    Creates missing objects; never drops tables or removes existing data.
    This is a bootstrap script, not a migration for changing existing tables.

    PortfolioType: 0 Individual, 1 Joint, 2 Retirement, 3 Education, 4 Trust, 5 Corporate.
    Status:        0 Active, 1 Archived.
    CurrencyType:  USD, EUR, GBP, JPY, AUD, CAD, MMK (stored as three-letter codes).
    MarketPriceSnapshots stores observed latest-trade prices, not account executions.
    Broker order submission and execution history are deferred to later scripts.
*/

USE [master];
GO

IF DB_ID(N'Gemino') IS NULL
BEGIN
    -- Database creation must run outside the schema transaction below.
    EXEC(N'CREATE DATABASE [Gemino];');
END;
GO

USE [Gemino];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Prevent schema creation in the wrong database if an earlier batch failed.
IF DB_NAME() <> N'Gemino'
    THROW 50000, 'Select Gemino before creating the schema.', 1;

-- Run as a standalone script, not inside a caller-owned transaction.
IF @@TRANCOUNT <> 0
    THROW 50001, 'Run this script without an existing transaction.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[Users]
        (
            [UserId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_Users_UserId] DEFAULT NEWSEQUENTIALID(),
            [Name] nvarchar(200) NOT NULL,
            [Email] nvarchar(254) COLLATE Latin1_General_100_CI_AS NOT NULL,

            CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([UserId]),
            CONSTRAINT [UQ_Users_Email] UNIQUE ([Email]),
            CONSTRAINT [CK_Users_Name_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([Name]))) > 0),
            CONSTRAINT [CK_Users_Email_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([Email]))) > 0)
        );
    END;

    IF OBJECT_ID(N'dbo.Portfolios', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[Portfolios]
        (
            [PortfolioId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_Portfolios_PortfolioId] DEFAULT NEWSEQUENTIALID(),
            [OwnerId] uniqueidentifier NOT NULL,
            [CashBalance] decimal(19, 8) NOT NULL
                CONSTRAINT [DF_Portfolios_CashBalance] DEFAULT (0),
            [PortfolioType] tinyint NOT NULL
                CONSTRAINT [DF_Portfolios_PortfolioType] DEFAULT (0),
            [CurrencyType] char(3) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT [DF_Portfolios_CurrencyType] DEFAULT ('USD'),
            [Status] tinyint NOT NULL
                CONSTRAINT [DF_Portfolios_Status] DEFAULT (0),
            [CreatedAt] datetimeoffset(7) NOT NULL
                CONSTRAINT [DF_Portfolios_CreatedAt]
                DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
            [RowVersion] rowversion NOT NULL,

            CONSTRAINT [PK_Portfolios] PRIMARY KEY CLUSTERED ([PortfolioId]),
            CONSTRAINT [FK_Portfolios_Users]
                FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[Users] ([UserId]),
            CONSTRAINT [CK_Portfolios_CashBalance_NonNegative]
                CHECK ([CashBalance] >= 0),
            CONSTRAINT [CK_Portfolios_PortfolioType]
                CHECK ([PortfolioType] IN (0, 1, 2, 3, 4, 5)),
            CONSTRAINT [CK_Portfolios_CurrencyType]
                CHECK ([CurrencyType] IN ('USD', 'EUR', 'GBP', 'JPY', 'AUD', 'CAD', 'MMK')),
            CONSTRAINT [CK_Portfolios_Status]
                CHECK ([Status] IN (0, 1))
        );

        CREATE INDEX [IX_Portfolios_OwnerId]
            ON [dbo].[Portfolios] ([OwnerId]);
    END;

    IF OBJECT_ID(N'dbo.Securities', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[Securities]
        (
            [SecurityId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_Securities_SecurityId] DEFAULT NEWSEQUENTIALID(),
            [TickerSymbol] nvarchar(32) COLLATE Latin1_General_100_CI_AS NOT NULL,
            [SecurityType] nvarchar(50) NOT NULL,
            -- Optional latest-price cache; NULL means a quote is unavailable.
            [CurrentValue] decimal(19, 8) NULL,
            [CurrencyType] char(3) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT [DF_Securities_CurrencyType] DEFAULT ('USD'),
            [QuoteAt] datetimeoffset(7) NULL,

            CONSTRAINT [PK_Securities] PRIMARY KEY CLUSTERED ([SecurityId]),
            CONSTRAINT [UQ_Securities_TickerSymbol] UNIQUE ([TickerSymbol]),
            CONSTRAINT [CK_Securities_TickerSymbol_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([TickerSymbol]))) > 0),
            CONSTRAINT [CK_Securities_SecurityType_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([SecurityType]))) > 0),
            CONSTRAINT [CK_Securities_CurrentValue_NonNegative]
                CHECK ([CurrentValue] IS NULL OR [CurrentValue] >= 0),
            CONSTRAINT [CK_Securities_QuoteMetadata]
                CHECK (([CurrentValue] IS NULL AND [QuoteAt] IS NULL)
                    OR ([CurrentValue] IS NOT NULL AND [QuoteAt] IS NOT NULL)),
            CONSTRAINT [CK_Securities_CurrencyType]
                CHECK ([CurrencyType] IN ('USD', 'EUR', 'GBP', 'JPY', 'AUD', 'CAD', 'MMK'))
        );
    END;

    IF OBJECT_ID(N'dbo.Holdings', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[Holdings]
        (
            [HoldingId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_Holdings_HoldingId] DEFAULT NEWSEQUENTIALID(),
            [PortfolioId] uniqueidentifier NOT NULL,
            [SecurityId] uniqueidentifier NOT NULL,
            [ShareCount] int NOT NULL
                CONSTRAINT [DF_Holdings_ShareCount] DEFAULT (0),
            [AveragePurchasePrice] decimal(19, 8) NOT NULL
                CONSTRAINT [DF_Holdings_AveragePurchasePrice] DEFAULT (0),
            [RowVersion] rowversion NOT NULL,

            CONSTRAINT [PK_Holdings] PRIMARY KEY CLUSTERED ([HoldingId]),
            CONSTRAINT [FK_Holdings_Portfolios]
                FOREIGN KEY ([PortfolioId]) REFERENCES [dbo].[Portfolios] ([PortfolioId]),
            CONSTRAINT [FK_Holdings_Securities]
                FOREIGN KEY ([SecurityId]) REFERENCES [dbo].[Securities] ([SecurityId]),
            CONSTRAINT [UQ_Holdings_PortfolioId_SecurityId]
                UNIQUE ([PortfolioId], [SecurityId]),
            CONSTRAINT [CK_Holdings_ShareCount_NonNegative]
                CHECK ([ShareCount] >= 0),
            CONSTRAINT [CK_Holdings_AveragePurchasePrice_NonNegative]
                CHECK ([AveragePurchasePrice] >= 0),
            CONSTRAINT [CK_Holdings_NonEmptyCostBasis]
                CHECK ([ShareCount] = 0 OR [AveragePurchasePrice] > 0)
        );

        -- The unique portfolio/security index already supports portfolio lookups.
        CREATE INDEX [IX_Holdings_SecurityId]
            ON [dbo].[Holdings] ([SecurityId]);
    END;

    IF OBJECT_ID(N'dbo.MarketPriceSnapshots', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[MarketPriceSnapshots]
        (
            [SnapshotId] bigint IDENTITY(1, 1) NOT NULL,
            [SecurityId] uniqueidentifier NOT NULL,
            [Price] decimal(19, 8) NOT NULL,
            [CurrencyType] char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            -- Timestamp of the market observation supplied by the provider.
            [MarketTimestamp] datetimeoffset(7) NOT NULL,
            -- Timestamp when Gemino stored the observation, independent of market time.
            [ReceivedAt] datetimeoffset(7) NOT NULL
                CONSTRAINT [DF_MarketPriceSnapshots_ReceivedAt]
                DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
            [Provider] nvarchar(50) COLLATE Latin1_General_100_CI_AS NOT NULL,
            [Feed] nvarchar(50) COLLATE Latin1_General_100_CI_AS NOT NULL,

            CONSTRAINT [PK_MarketPriceSnapshots] PRIMARY KEY CLUSTERED ([SnapshotId]),
            CONSTRAINT [FK_MarketPriceSnapshots_Securities]
                FOREIGN KEY ([SecurityId]) REFERENCES [dbo].[Securities] ([SecurityId]),
            -- Repeated polling of the same source observation must not add duplicates.
            CONSTRAINT [UQ_MarketPriceSnapshots_Observation]
                UNIQUE ([SecurityId], [Provider], [Feed], [CurrencyType], [MarketTimestamp]),
            CONSTRAINT [CK_MarketPriceSnapshots_Price_NonNegative]
                CHECK ([Price] >= 0),
            CONSTRAINT [CK_MarketPriceSnapshots_CurrencyType]
                CHECK ([CurrencyType] IN ('USD', 'EUR', 'GBP', 'JPY', 'AUD', 'CAD', 'MMK')),
            CONSTRAINT [CK_MarketPriceSnapshots_Provider_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([Provider]))) > 0),
            CONSTRAINT [CK_MarketPriceSnapshots_Feed_NotBlank]
                CHECK (LEN(LTRIM(RTRIM([Feed]))) > 0)
        );

        CREATE INDEX [IX_MarketPriceSnapshots_SecurityId_MarketTimestamp]
            ON [dbo].[MarketPriceSnapshots] ([SecurityId], [MarketTimestamp] DESC)
            INCLUDE ([Price], [CurrencyType], [Provider], [Feed], [ReceivedAt]);
    END;

    COMMIT TRANSACTION;
    PRINT N'Gemino schema bootstrap completed. Existing tables were left unchanged.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
