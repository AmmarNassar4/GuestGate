
IF DB_ID(N'GuestGate') IS NULL
BEGIN
  CREATE DATABASE [GuestGate];
END
GO
USE [GuestGate];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Drop legacy triggers if any (safe)
IF OBJECT_ID('dbo.trg_Templates_UpdatedAt','TR') IS NOT NULL DROP TRIGGER dbo.trg_Templates_UpdatedAt;
IF OBJECT_ID('dbo.trg_Guests_UpdatedAt','TR')    IS NOT NULL DROP TRIGGER dbo.trg_Guests_UpdatedAt;
IF OBJECT_ID('dbo.trg_KioskSessions_UpdatedAt','TR') IS NOT NULL DROP TRIGGER dbo.trg_KioskSessions_UpdatedAt;
GO

-- Templates
IF OBJECT_ID('dbo.Templates','U') IS NULL
BEGIN
  CREATE TABLE dbo.Templates(
    Id         NVARCHAR(50)  NOT NULL PRIMARY KEY,
    DataJson   NVARCHAR(MAX) NOT NULL,
    CreatedAt  DATETIME2(7)  NOT NULL CONSTRAINT DF_Templates_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt  DATETIME2(7)  NOT NULL CONSTRAINT DF_Templates_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Templates_DataJson_IsJson CHECK (ISJSON(DataJson)=1)
  );
END
GO

-- Guests JSON-only
IF OBJECT_ID('dbo.Guests','U') IS NULL
BEGIN
  CREATE TABLE dbo.Guests(
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    DataJson   NVARCHAR(MAX) NOT NULL,
    CreatedAt  DATETIME2(7)  NOT NULL CONSTRAINT DF_Guests_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt  DATETIME2(7)  NOT NULL CONSTRAINT DF_Guests_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Guests_DataJson_IsJson CHECK (ISJSON(DataJson)=1)
  );
END
GO

-- Sessions with PrefillJson
IF OBJECT_ID('dbo.KioskSessions','U') IS NULL
BEGIN
  CREATE TABLE dbo.KioskSessions(
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Kid          NVARCHAR(50)      NOT NULL,
    EditToken    UNIQUEIDENTIFIER  NOT NULL UNIQUE,
    GuestId      INT               NULL FOREIGN KEY REFERENCES dbo.Guests(Id),
    Status       TINYINT           NOT NULL, -- 1=Active, 2=Completed, 3=Cancelled, 4=Expired
    ExpiresAt    DATETIME2(7)      NOT NULL,
    TemplateId   NVARCHAR(50)      NULL,
    PrefillJson  NVARCHAR(MAX)     NULL,
    CreatedAt    DATETIME2(7)      NOT NULL CONSTRAINT DF_KS_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt    DATETIME2(7)      NOT NULL CONSTRAINT DF_KS_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_KS_Prefill_IsJson CHECK (PrefillJson IS NULL OR ISJSON(PrefillJson)=1)
  );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_KioskSessions_ActiveKid' AND object_id=OBJECT_ID('dbo.KioskSessions'))
  CREATE UNIQUE INDEX UX_KioskSessions_ActiveKid ON dbo.KioskSessions(Kid) WHERE Status = 1;
GO

PRINT '✅ Database ready (no triggers; EF handles timestamps).';

-- Consent requests for kiosk approval/signature flow
IF OBJECT_ID('dbo.ConsentRequests','U') IS NULL
BEGIN
  CREATE TABLE dbo.ConsentRequests(
    Id                    INT IDENTITY(1,1) PRIMARY KEY,
    Kid                   NVARCHAR(50)      NOT NULL,
    GuestName             NVARCHAR(200)     NOT NULL CONSTRAINT DF_CR_GuestName DEFAULT N'',
    IdentityNumber        NVARCHAR(80)      NOT NULL CONSTRAINT DF_CR_IdentityNumber DEFAULT N'',
    CheckInTime           NVARCHAR(50)      NOT NULL CONSTRAINT DF_CR_CheckInTime DEFAULT N'',
    Language              NVARCHAR(5)       NOT NULL CONSTRAINT DF_CR_Language DEFAULT N'en',
    TermsEn               NVARCHAR(MAX)     NOT NULL,
    TermsAr               NVARCHAR(MAX)     NOT NULL,
    Status                NVARCHAR(20)      NOT NULL CONSTRAINT DF_CR_Status DEFAULT N'waiting',
    Accepted              BIT               NOT NULL CONSTRAINT DF_CR_Accepted DEFAULT 0,
    SignatureImageDataUrl NVARCHAR(MAX)     NULL,
    PdfPath               NVARCHAR(500)     NULL,
    SignedAt              DATETIME2(7)      NULL,
    CreatedAt             DATETIME2(7)      NOT NULL CONSTRAINT DF_CR_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt             DATETIME2(7)      NOT NULL CONSTRAINT DF_CR_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_CR_Status CHECK (Status IN (N'waiting', N'assigned', N'signed', N'cancelled')),
    CONSTRAINT CK_CR_Language CHECK (Language IN (N'en', N'ar'))
  );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ConsentRequests_Kid_Status' AND object_id=OBJECT_ID('dbo.ConsentRequests'))
  CREATE INDEX IX_ConsentRequests_Kid_Status ON dbo.ConsentRequests(Kid, Status, Id);
GO
