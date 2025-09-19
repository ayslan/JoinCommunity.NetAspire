import axios from 'axios';

export interface Pokemon {
    id: number;
    name: string;
    height: number;
    weight: number;
    imageUrl?: string;
}

export interface PokemonSummary {
    info: string;
}

const api = axios.create({
    baseURL: 'https://localhost:7137',
    timeout: 10000,
});

export const pokemonApi = {
    // Get Pokemon summary from Main.API
    getPokemonSummary: async (name: string): Promise<PokemonSummary> => {
        const response = await api.get(`/summary/${name}`);
        return response.data;
    },

    // Direct call to Inner.API for full Pokemon data
    getPokemonDetails: async (name: string): Promise<Pokemon> => {
        const response = await api.get(`https://localhost:7137/summary/${name}`);
        return response.data;
    },
};