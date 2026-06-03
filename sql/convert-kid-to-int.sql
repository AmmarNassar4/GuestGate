-- Converts KioskSessions.Kid and ConsentRequests.Kid from text values to int values.
-- Run once before deploying the API version that maps Kid as int.
-- This script supports old values like 'K1' and numeric text like '1'.

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.KioskSessions', N'Kid') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.KioskSessions')
          AND c.name = N'Kid'
          AND t.name IN (N'nvarchar', N'varchar', N'nchar', N'char')
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.KioskSessions
            WHERE TRY_CONVERT(int, CASE WHEN LEFT(LTRIM(RTRIM(Kid)), 1) IN (N'K', N'k') THEN SUBSTRING(LTRIM(RTRIM(Kid)), 2, 50) ELSE LTRIM(RTRIM(Kid)) END) IS NULL
        )
        BEGIN
            THROW 50001, 'KioskSessions contains Kid values that cannot be converted to int.', 1;
        END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.KioskSessions') AND name = N'IX_KioskSessions_Kid_Status')
            DROP INDEX [IX_KioskSessions_Kid_Status] ON [dbo].[KioskSessions];

        ALTER TABLE dbo.KioskSessions ADD KidInt int NULL;

        UPDATE dbo.KioskSessions
           SET KidInt = TRY_CONVERT(int, CASE WHEN LEFT(LTRIM(RTRIM(Kid)), 1) IN (N'K', N'k') THEN SUBSTRING(LTRIM(RTRIM(Kid)), 2, 50) ELSE LTRIM(RTRIM(Kid)) END);

        ALTER TABLE dbo.KioskSessions DROP COLUMN Kid;
        EXEC sp_rename 'dbo.KioskSessions.KidInt', 'Kid', 'COLUMN';
        ALTER TABLE dbo.KioskSessions ALTER COLUMN Kid int NOT NULL;
        CREATE INDEX [IX_KioskSessions_Kid_Status] ON [dbo].[KioskSessions] ([Kid], [Status]);
    END
END;

IF COL_LENGTH(N'dbo.ConsentRequests', N'Kid') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.ConsentRequests')
          AND c.name = N'Kid'
          AND t.name IN (N'nvarchar', N'varchar', N'nchar', N'char')
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.ConsentRequests
            WHERE TRY_CONVERT(int, CASE WHEN LEFT(LTRIM(RTRIM(Kid)), 1) IN (N'K', N'k') THEN SUBSTRING(LTRIM(RTRIM(Kid)), 2, 50) ELSE LTRIM(RTRIM(Kid)) END) IS NULL
        )
        BEGIN
            THROW 50002, 'ConsentRequests contains Kid values that cannot be converted to int.', 1;
        END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ConsentRequests') AND name = N'IX_ConsentRequests_Kid_Status')
            DROP INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests];

        ALTER TABLE dbo.ConsentRequests ADD KidInt int NULL;

        UPDATE dbo.ConsentRequests
           SET KidInt = TRY_CONVERT(int, CASE WHEN LEFT(LTRIM(RTRIM(Kid)), 1) IN (N'K', N'k') THEN SUBSTRING(LTRIM(RTRIM(Kid)), 2, 50) ELSE LTRIM(RTRIM(Kid)) END);

        ALTER TABLE dbo.ConsentRequests DROP COLUMN Kid;
        EXEC sp_rename 'dbo.ConsentRequests.KidInt', 'Kid', 'COLUMN';
        ALTER TABLE dbo.ConsentRequests ALTER COLUMN Kid int NOT NULL;
        CREATE INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests] ([Kid], [Status]);
    END
END;

COMMIT TRANSACTION;
