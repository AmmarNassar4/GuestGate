-- Converts KioskSessions.Kid and ConsentRequests.Kid from text values to int values.
-- Run once before deploying or immediately after deploying the API version that maps Kid as int.
-- Supports values like '1', '01', 'K1', 'k1', 'KIOSK-01', and 'kiosk-01'.

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
            CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), Kid))) AS RawKid) r
            CROSS APPLY (SELECT CASE
                WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
                WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
                WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
                WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
                ELSE r.RawKid
            END AS NormalizedKid) n
            WHERE TRY_CONVERT(int, n.NormalizedKid) IS NULL
               OR TRY_CONVERT(int, n.NormalizedKid) <= 0
        )
        BEGIN
            SELECT DISTINCT Kid AS InvalidKid FROM dbo.KioskSessions
            CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), Kid))) AS RawKid) r
            CROSS APPLY (SELECT CASE
                WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
                WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
                WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
                WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
                ELSE r.RawKid
            END AS NormalizedKid) n
            WHERE TRY_CONVERT(int, n.NormalizedKid) IS NULL
               OR TRY_CONVERT(int, n.NormalizedKid) <= 0;

            THROW 50001, 'KioskSessions contains Kid values that cannot be converted to a positive int.', 1;
        END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.KioskSessions') AND name = N'IX_KioskSessions_Kid_Status')
            DROP INDEX [IX_KioskSessions_Kid_Status] ON [dbo].[KioskSessions];

        ALTER TABLE dbo.KioskSessions ADD KidInt int NULL;

        UPDATE s
           SET KidInt = TRY_CONVERT(int, n.NormalizedKid)
        FROM dbo.KioskSessions s
        CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), s.Kid))) AS RawKid) r
        CROSS APPLY (SELECT CASE
            WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
            WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
            WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
            WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
            ELSE r.RawKid
        END AS NormalizedKid) n;

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
            CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), Kid))) AS RawKid) r
            CROSS APPLY (SELECT CASE
                WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
                WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
                WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
                WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
                ELSE r.RawKid
            END AS NormalizedKid) n
            WHERE TRY_CONVERT(int, n.NormalizedKid) IS NULL
               OR TRY_CONVERT(int, n.NormalizedKid) <= 0
        )
        BEGIN
            SELECT DISTINCT Kid AS InvalidKid FROM dbo.ConsentRequests
            CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), Kid))) AS RawKid) r
            CROSS APPLY (SELECT CASE
                WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
                WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
                WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
                WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
                ELSE r.RawKid
            END AS NormalizedKid) n
            WHERE TRY_CONVERT(int, n.NormalizedKid) IS NULL
               OR TRY_CONVERT(int, n.NormalizedKid) <= 0;

            THROW 50002, 'ConsentRequests contains Kid values that cannot be converted to a positive int.', 1;
        END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ConsentRequests') AND name = N'IX_ConsentRequests_Kid_Status')
            DROP INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests];

        ALTER TABLE dbo.ConsentRequests ADD KidInt int NULL;

        UPDATE c
           SET KidInt = TRY_CONVERT(int, n.NormalizedKid)
        FROM dbo.ConsentRequests c
        CROSS APPLY (SELECT LTRIM(RTRIM(CONVERT(nvarchar(80), c.Kid))) AS RawKid) r
        CROSS APPLY (SELECT CASE
            WHEN TRY_CONVERT(int, r.RawKid) IS NOT NULL THEN r.RawKid
            WHEN UPPER(r.RawKid) LIKE N'KIOSK-%' THEN SUBSTRING(r.RawKid, CHARINDEX(N'-', r.RawKid) + 1, 80)
            WHEN UPPER(r.RawKid) LIKE N'KIOSK%' THEN SUBSTRING(r.RawKid, 6, 80)
            WHEN LEFT(r.RawKid, 1) IN (N'K', N'k') THEN SUBSTRING(r.RawKid, 2, 80)
            ELSE r.RawKid
        END AS NormalizedKid) n;

        ALTER TABLE dbo.ConsentRequests DROP COLUMN Kid;
        EXEC sp_rename 'dbo.ConsentRequests.KidInt', 'Kid', 'COLUMN';
        ALTER TABLE dbo.ConsentRequests ALTER COLUMN Kid int NOT NULL;
        CREATE INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests] ([Kid], [Status]);
    END
END;

COMMIT TRANSACTION;
