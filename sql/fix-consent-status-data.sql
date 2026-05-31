-- Normalizes existing consent rows after switching ConsentStatus to a string-backed enum conversion.
-- Run once on the production database after deploying the API change.

UPDATE dbo.ConsentRequests
   SET Status = LOWER(LTRIM(RTRIM(Status)))
 WHERE Status IS NOT NULL
   AND Status <> LOWER(LTRIM(RTRIM(Status)));

UPDATE dbo.ConsentRequests
   SET Kid = UPPER(LTRIM(RTRIM(Kid)))
 WHERE Kid IS NOT NULL
   AND Kid <> UPPER(LTRIM(RTRIM(Kid)));
