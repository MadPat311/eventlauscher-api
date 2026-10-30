START TRANSACTION;

ALTER TABLE "Events" ADD "SourcePlatform" character varying(20);

ALTER TABLE "Events" ADD "SourceUrl" character varying(2000);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261010233001_AddEventSourceLinks', '8.0.31');

COMMIT;

