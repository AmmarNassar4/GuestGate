using Microsoft.EntityFrameworkCore;

namespace GuestGate.Api.Data;

internal static class ConsentSchemaInitializer
{
    public static async Task EnsureConsentRequestsTableAsync(AppDb db)
    {
        if (!db.Database.IsSqlServer()) return;

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'[dbo].[ConsentRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConsentRequests]
    (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ConsentRequests] PRIMARY KEY,
        [Kid] nvarchar(50) NOT NULL,
        [GuestName] nvarchar(200) NOT NULL CONSTRAINT [DF_ConsentRequests_GuestName] DEFAULT N'',
        [IdentityNumber] nvarchar(80) NOT NULL CONSTRAINT [DF_ConsentRequests_IdentityNumber] DEFAULT N'',
        [CheckInTime] nvarchar(50) NOT NULL CONSTRAINT [DF_ConsentRequests_CheckInTime] DEFAULT N'',
        [Language] nvarchar(5) NOT NULL CONSTRAINT [DF_ConsentRequests_Language] DEFAULT N'en',
        [TermsEn] nvarchar(max) NOT NULL CONSTRAINT [DF_ConsentRequests_TermsEn] DEFAULT N'',
        [TermsAr] nvarchar(max) NOT NULL CONSTRAINT [DF_ConsentRequests_TermsAr] DEFAULT N'',
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_ConsentRequests_Status] DEFAULT N'waiting',
        [Accepted] bit NOT NULL CONSTRAINT [DF_ConsentRequests_Accepted] DEFAULT CONVERT(bit, 0),
        [SignatureImageDataUrl] nvarchar(max) NULL,
        [PdfPath] nvarchar(max) NULL,
        [SignedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL
    );

    CREATE INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests] ([Kid], [Status]);
END
");

        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ConsentRequests', N'IdentityNumber') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConsentRequests]
    ADD [IdentityNumber] nvarchar(80) NOT NULL CONSTRAINT [DF_ConsentRequests_IdentityNumber] DEFAULT N'';
END

IF COL_LENGTH(N'dbo.ConsentRequests', N'CheckInTime') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConsentRequests]
    ADD [CheckInTime] nvarchar(50) NOT NULL CONSTRAINT [DF_ConsentRequests_CheckInTime] DEFAULT N'';
END
");
    }
}
