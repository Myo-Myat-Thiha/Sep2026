/* Local-device sign-in. Existing profiles remain intact and need initial password setup.
   No passwords are assigned by this script. Safe to run more than once. */
USE [Gemino];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'Gemino'
    THROW 50000, 'Select Gemino before applying the password schema.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 50001, 'Create the Users table before applying this script.', 1;
IF COL_LENGTH(N'dbo.Users', N'PasswordHash') IS NULL
    ALTER TABLE dbo.Users ADD PasswordHash varchar(512) COLLATE Latin1_General_100_BIN2 NULL;
GO
