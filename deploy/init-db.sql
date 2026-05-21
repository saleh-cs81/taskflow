IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [ActivityLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NULL,
        [Action] nvarchar(150) NOT NULL,
        [EntityType] nvarchar(100) NULL,
        [EntityId] bigint NULL,
        [MetadataJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NULL,
        [TableName] nvarchar(128) NOT NULL,
        [RecordId] nvarchar(64) NOT NULL,
        [ChangeType] int NOT NULL,
        [OldValuesJson] nvarchar(max) NULL,
        [NewValuesJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Group] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Slug] nvarchar(80) NOT NULL,
        [LogoUrl] nvarchar(max) NULL,
        [SubscriptionStatus] int NOT NULL,
        [TrialEndsUtc] datetime2 NULL,
        [DefaultLocale] nvarchar(5) NOT NULL DEFAULT N'en',
        [TimeZone] nvarchar(64) NOT NULL DEFAULT N'UTC',
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsSystemRole] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] rowversion NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Roles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] bigint NOT NULL IDENTITY,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [Locale] nvarchar(5) NOT NULL DEFAULT N'en',
        [TimeZone] nvarchar(64) NOT NULL DEFAULT N'UTC',
        [IsActive] bit NOT NULL,
        [EmailConfirmed] bit NOT NULL,
        [LastLoginUtc] datetime2 NULL,
        [PasswordResetTokenHash] nvarchar(max) NULL,
        [PasswordResetExpiresUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] rowversion NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [Invitations] (
        [Id] bigint NOT NULL IDENTITY,
        [Email] nvarchar(256) NOT NULL,
        [RoleId] bigint NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [AcceptedUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_Invitations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invitations_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] bigint NOT NULL,
        [PermissionId] bigint NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [RevokedUtc] datetime2 NULL,
        [ReplacedByTokenHash] nvarchar(256) NULL,
        [DeviceInfo] nvarchar(512) NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedById] bigint NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [UpdatedById] bigint NULL,
        [RowVersion] varbinary(max) NULL,
        [TenantId] bigint NOT NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        [DeletedById] bigint NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_TenantId_CreatedAtUtc] ON [ActivityLogs] ([TenantId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_TableName_RecordId] ON [AuditLogs] ([TenantId], [TableName], [RecordId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_Invitations_RoleId] ON [Invitations] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_Invitations_TenantId_Email] ON [Invitations] ([TenantId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [Permissions] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_TenantId_UserId] ON [RefreshTokens] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_TenantId_NormalizedName] ON [Roles] ([TenantId], [NormalizedName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Slug] ON [Tenants] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_TenantId_NormalizedEmail] ON [Users] ([TenantId], [NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260520185216_InitialIdentityAndTenancy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260520185216_InitialIdentityAndTenancy', N'9.0.0');
END;

COMMIT;
GO

