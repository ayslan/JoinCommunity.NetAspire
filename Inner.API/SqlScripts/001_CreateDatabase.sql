-- Create the database if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'PokemonDb')
BEGIN
    CREATE DATABASE [PokemonDb]
END
GO

USE [PokemonDb]
GO