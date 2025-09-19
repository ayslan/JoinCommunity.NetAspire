import React, { useState } from 'react';
import { PokemonSummary, pokemonApi } from '../services/pokemonApi';
import './PokemonSearch.css';

const PokemonSearch: React.FC = () => {
    const [pokemonName, setPokemonName] = useState('');
    const [summary, setSummary] = useState<PokemonSummary | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleSearch = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!pokemonName.trim()) return;

        setLoading(true);
        setError(null);
        setSummary(null);

        try {
            // Get both summary and details
            const [summaryResponse] = await Promise.all([
                pokemonApi.getPokemonSummary(pokemonName.toLowerCase())
            ]);

            setSummary(summaryResponse);
        } catch (err) {
            setError(`Pokemon "${pokemonName}" not found or server error.`);
            console.error('Error fetching Pokemon:', err);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="pokemon-search">
            <h1>Pokemon Search</h1>
            <form onSubmit={handleSearch} className="search-form">
                <input
                    type="text"
                    value={pokemonName}
                    onChange={(e) => setPokemonName(e.target.value)}
                    placeholder="Enter Pokemon name (e.g., pikachu)"
                    className="search-input"
                    disabled={loading}
                />
                <button type="submit" className="search-button" disabled={loading || !pokemonName.trim()}>
                    {loading ? 'Searching...' : 'Search'}
                </button>
            </form>

            {error && <div className="error-message">{error}</div>}

            {summary && (
                <div className="summary-section">
                    <h2>Summary</h2>
                    <p>{summary.info}</p>
                </div>
            )}
        </div>
    );
};

export default PokemonSearch;