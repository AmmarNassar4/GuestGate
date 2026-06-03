-- Optional performance indexes after Kid has been converted to int.
-- Run after sql/convert-kid-to-int.sql.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.KioskSessions') AND name = N'IX_KioskSessions_Kid_Status_Id_ActiveLookup')
BEGIN
    CREATE INDEX [IX_KioskSessions_Kid_Status_Id_ActiveLookup]
    ON [dbo].[KioskSessions] ([Kid], [Status], [Id] DESC)
    INCLUDE ([EditToken], [ExpiresAt], [TemplateId]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ConsentRequests') AND name = N'IX_ConsentRequests_Kid_Status_Id_ActiveLookup')
BEGIN
    CREATE INDEX [IX_ConsentRequests_Kid_Status_Id_ActiveLookup]
    ON [dbo].[ConsentRequests] ([Kid], [Status], [Id])
    INCLUDE ([GuestName], [IdentityNumber], [CheckInTime], [Language], [UpdatedAt], [SignedAt], [PdfPath]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ConsentRequests') AND name = N'IX_ConsentRequests_UpdatedAt_FallbackWatcher')
BEGIN
    CREATE INDEX [IX_ConsentRequests_UpdatedAt_FallbackWatcher]
    ON [dbo].[ConsentRequests] ([UpdatedAt])
    INCLUDE ([Id], [Kid], [Status], [PdfPath]);
END;
