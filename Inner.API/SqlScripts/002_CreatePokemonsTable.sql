---- Create the Pokemons table

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Pokemons]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Pokemons] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [Name] nvarchar(255) NOT NULL,
        [Height] int NOT NULL,
        [Weight] int NOT NULL,
        CONSTRAINT [PK_Pokemons] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [IX_Pokemons_Name] UNIQUE NONCLUSTERED ([Name] ASC)
    ) ON [PRIMARY]
END
GO