CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TABLE IF NOT EXISTS users (
    "Id" character varying(64) NOT NULL,
    "Username" character varying(100) NOT NULL,
    "Password" character varying(255) NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_Username" ON users ("Username");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012174712_InitialCreate_UsersOnly', '8.0.11')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Dữ liệu mẫu (Seed Data) tài khoản admin phục vụ đăng nhập
INSERT INTO users ("Id", "Username", "Password")
VALUES ('usr_super_admin_01', 'admin', 'Admin@123')
ON CONFLICT ("Username") DO NOTHING;

COMMIT;
